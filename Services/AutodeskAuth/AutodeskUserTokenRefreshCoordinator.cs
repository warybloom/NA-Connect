using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace APS.WebApp.Services.AutodeskAuth;

/// <summary>
/// Single-flight do refresh 3-legged (SINGLETON). Duas peças:
/// <list type="number">
/// <item>Lock assíncrono por sessão (SemaphoreSlim por Session.Id, com contagem de referências para não vazar entradas).
/// Precisa ser singleton: um semáforo scoped não sincronizaria requisições diferentes.</item>
/// <item>Registro do último refresh, indexado por (sessão, hash do refresh_token CONSUMIDO). Necessário porque a
/// Session do ASP.NET Core é carregada uma vez por requisição e gravada inteira no fim: a requisição B que esperava
/// o lock ainda enxerga o snapshot anterior ao refresh feito por A. Ao encontrar aqui o resultado de A, B adota os
/// tokens novos sem nova chamada HTTP (que falharia se a Autodesk rotacionou o refresh_token).</item>
/// </list>
/// O estado vive na memória do processo — mesmo escopo do AddDistributedMemoryCache usado pela Session hoje.
/// </summary>
public sealed class AutodeskUserTokenRefreshCoordinator(IMemoryCache cache)
{
    private sealed class Gate
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int References;
    }

    private readonly Dictionary<string, Gate> _gates = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string sessionId, CancellationToken ct)
    {
        Gate gate;
        lock (_gates)
        {
            if (!_gates.TryGetValue(sessionId, out var existing)) _gates[sessionId] = existing = new Gate();
            gate = existing;
            gate.References++;
        }
        try
        {
            await gate.Semaphore.WaitAsync(ct);
        }
        catch
        {
            Unreference(sessionId, gate);
            throw;
        }
        return new Releaser(this, sessionId, gate);
    }

    public void RememberRefresh(string sessionId, string consumedRefreshToken, AutodeskUserTokenSet result) =>
        cache.Set(Key(sessionId, consumedRefreshToken), result, AutodeskUserTokenPolicy.RefreshResultRetention);

    public bool TryGetRefresh(string sessionId, string consumedRefreshToken, out AutodeskUserTokenSet result) =>
        cache.TryGetValue(Key(sessionId, consumedRefreshToken), out result!);

    /// <summary>Quantidade de sessões com lock ativo/aguardando (diagnóstico e testes).</summary>
    public int ActiveGates { get { lock (_gates) return _gates.Count; } }

    private void Unreference(string sessionId, Gate gate)
    {
        lock (_gates)
        {
            if (--gate.References == 0) _gates.Remove(sessionId);
        }
    }

    // O refresh_token nunca vira chave em claro.
    private static string Key(string sessionId, string refreshToken) =>
        $"AutodeskUserToken:Refreshed:{sessionId}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)))}";

    private sealed class Releaser(AutodeskUserTokenRefreshCoordinator owner, string sessionId, Gate gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            gate.Semaphore.Release();
            owner.Unreference(sessionId, gate);
        }
    }
}
