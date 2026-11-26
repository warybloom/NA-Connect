using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using APS.WebApp.Services.LocalAuth;
using APS.WebApp.Services.Permissions;
using Microsoft.Extensions.Options;

namespace APS.WebApp.Services.Tv.Portal;

/// <summary>
/// "TvPortal:DeviceSession". Sessão de DISPOSITIVO só para conta TV restrita (perfil TV + tv.view), registrada
/// automaticamente pela própria TV logada. Ligada por padrão; Enabled=false desliga (cookie ignorado, registro recusado).
/// </summary>
public sealed class TvDeviceSessionOptions
{
    public const string Section = "TvPortal:DeviceSession";
    public bool Enabled { get; set; } = true;
    /// <summary>Sem nenhuma requisição da TV por este período, o dispositivo expira.</summary>
    public int IdleDays { get; set; } = 14;
    /// <summary>Teto absoluto desde o registro; depois disso a TV exige novo login.</summary>
    public int MaxDays { get; set; } = 365;
    /// <summary>Renova a expiração deslizante e ROTACIONA o token (arquivo + cookie) no máximo uma vez neste intervalo.</summary>
    public int RenewMinutes { get; set; } = 60;
    /// <summary>Requisições já em voo com o token anterior seguem válidas por este tempo após a rotação.</summary>
    public int RotationInFlightSeconds { get; set; } = 120;
    /// <summary>
    /// Se a resposta com o cookie novo se perder (queda de rede), o token anterior ainda é aceito por este prazo e gera
    /// outra rotação. Assim que o token novo é usado (confirmado), o anterior deixa de valer.
    /// </summary>
    public int PreviousTokenGraceHours { get; set; } = 24;
    /// <summary>Ao registrar além deste limite, os dispositivos ativos mais antigos do usuário são revogados.</summary>
    public int MaxDevicesPerUser { get; set; } = 5;
}

/// <summary>
/// Nunca guarda o token: só SHA-256 dele. CredentialStamp = PasswordUpdatedAt (troca/reset de senha invalida).
/// PreviousTokenHash = token anterior à última rotação, aceito só até o novo ser confirmado (ver TvDeviceSessionOptions).
/// </summary>
public sealed record TvDeviceRecord(string Id, int UserId, string TokenHash, long CredentialStamp, string Label,
    DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt = null, string? RevokedReason = null,
    string? PreviousTokenHash = null, DateTimeOffset? RotatedAt = null)
{
    public bool Active(DateTimeOffset now, TvDeviceSessionOptions o) =>
        RevokedAt is null && now < ExpiresAt && now < CreatedAt.AddDays(o.MaxDays);
}

/// <summary>Previous = o cookie apresentado é o token anterior à última rotação (ainda não confirmada).</summary>
public sealed record TvDeviceMatch(TvDeviceRecord Record, bool Previous);

public sealed record TvDeviceRegistry(long Revision, TvDeviceRecord[] Devices);

/// <summary>Registro persistente (JSON em App_Data/TvConfiguration:Directory) — sobrevive a restart do backend sem DDL.</summary>
public sealed class TvDeviceSessionStore(string path, TimeProvider? clock = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim mutex = new(1, 1);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private TvDeviceRegistry? cache;
    private DateTime cacheWrite;
    public string FilePath { get; } = Path.GetFullPath(path);

    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static bool WellFormed(string? token) => token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    public async Task<TvDeviceRegistry> ReadAsync(CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try { return await ReadCore(ct); } finally { mutex.Release(); }
    }

    public async Task<TvDeviceRecord?> FindAsync(string token, CancellationToken ct) => (await MatchAsync(token, ct))?.Record;

    public async Task<TvDeviceMatch?> MatchAsync(string token, CancellationToken ct)
    {
        if (!WellFormed(token)) return null;
        var hash = Encoding.ASCII.GetBytes(Hash(token));
        foreach (var d in (await ReadAsync(ct)).Devices)
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(d.TokenHash), hash)) return new(d, false);
            if (d.PreviousTokenHash is { } previous && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(previous), hash))
                return new(d, true);
        }
        return null;
    }

    public async Task<(TvDeviceRecord Record, string Token)> CreateAsync(int userId, long credentialStamp, string label,
        TvDeviceSessionOptions options, CancellationToken ct)
    {
        var token = NewToken();
        var now = time.GetUtcNow();
        var record = new TvDeviceRecord(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), userId, Hash(token), credentialStamp,
            label, now, now, Min(now.AddDays(options.IdleDays), now.AddDays(options.MaxDays)));
        await Mutate(devices =>
        {
            var ativos = devices.Where(d => d.UserId == userId && d.Active(now, options)).OrderByDescending(d => d.CreatedAt)
                .Skip(Math.Max(0, options.MaxDevicesPerUser - 1)).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
            return devices.Select(d => ativos.Contains(d.Id) ? d with { RevokedAt = now, RevokedReason = "limite-dispositivos" } : d)
                .Append(record).ToArray();
        }, ct);
        return (record, token);
    }

    /// <summary>
    /// Renovação com rotação, chamada a cada request válido do dispositivo com o token apresentado:
    /// <list type="bullet">
    /// <item>token atual e renovação vencida (RenewMinutes): token novo + expiração deslizante (limitada ao teto); o atual vira "anterior";</item>
    /// <item>token atual após RotationInFlightSeconds da rotação: confirma (o anterior deixa de valer), sem cookie novo;</item>
    /// <item>token anterior após RotationInFlightSeconds: o navegador não recebeu o cookie novo, então rotaciona de novo a partir dele.</item>
    /// </list>
    /// Retorna o token novo para o cookie (null = nada a enviar). Decidido sob o lock, sobre o registro atual do arquivo:
    /// duas requisições simultâneas nunca rotacionam duas vezes o mesmo token.
    /// </summary>
    public async Task<(TvDeviceRecord Record, string? Token)> RotateIfDueAsync(string presented, TvDeviceSessionOptions options, CancellationToken ct)
    {
        var hash = Hash(presented);
        var inFlight = TimeSpan.FromSeconds(options.RotationInFlightSeconds);
        await mutex.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            var current = await ReadCore(ct);
            var record = current.Devices.FirstOrDefault(d => d.RevokedAt is null && (d.TokenHash == hash || d.PreviousTokenHash == hash))
                ?? throw new InvalidOperationException("Dispositivo TV não encontrado para renovação.");
            var sinceRotation = record.RotatedAt is { } r ? now - r : TimeSpan.MaxValue;
            string? token = null;
            TvDeviceRecord next;
            if (record.PreviousTokenHash == hash)
            {
                if (sinceRotation < inFlight) return (record, null);
                token = NewToken();
                next = record with { TokenHash = Hash(token), RotatedAt = now, LastSeenAt = now, ExpiresAt = Slide(record, now, options) };
            }
            else if (now - record.LastSeenAt >= TimeSpan.FromMinutes(options.RenewMinutes))
            {
                token = NewToken();
                next = record with { TokenHash = Hash(token), PreviousTokenHash = hash, RotatedAt = now, LastSeenAt = now, ExpiresAt = Slide(record, now, options) };
            }
            else if (record.PreviousTokenHash is not null && sinceRotation >= inFlight)
                next = record with { PreviousTokenHash = null };
            else return (record, null);
            await WriteCore(current, current.Devices.Select(d => d.Id == record.Id ? next : d).ToArray(), ct);
            return (next, token);
        }
        finally { mutex.Release(); }
    }

    private static DateTimeOffset Slide(TvDeviceRecord record, DateTimeOffset now, TvDeviceSessionOptions options) =>
        Min(now.AddDays(options.IdleDays), record.CreatedAt.AddDays(options.MaxDays));

    public async Task<bool> RevokeAsync(string id, string reason, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var changed = false;
        await Mutate(devices => devices.Select(d =>
        {
            if (d.Id != id || d.RevokedAt is not null) return d;
            changed = true;
            return d with { RevokedAt = now, RevokedReason = reason };
        }).ToArray(), ct);
        return changed;
    }

    private async Task Mutate(Func<TvDeviceRecord[], TvDeviceRecord[]> change, CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            var current = await ReadCore(ct);
            await WriteCore(current, change(current.Devices), ct);
        }
        finally { mutex.Release(); }
    }

    /// <summary>Chamar com o mutex adquirido.</summary>
    private async Task WriteCore(TvDeviceRegistry current, TvDeviceRecord[] changed, CancellationToken ct)
    {
        string? temp = null;
        try
        {
            var limite = time.GetUtcNow().AddDays(-30);
            // Histórico de revogados/expirados fica 30 dias (auditoria) e depois sai do arquivo.
            var devices = changed.Where(d => (d.RevokedAt ?? d.ExpiresAt) > limite).ToArray();
            var next = new TvDeviceRegistry(checked(current.Revision + 1), devices);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(next, Json), ct);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak"); else File.Move(temp, FilePath);
            cache = next;
            cacheWrite = File.GetLastWriteTimeUtc(FilePath);
        }
        finally { if (temp is not null && File.Exists(temp)) File.Delete(temp); }
    }

    private async Task<TvDeviceRegistry> ReadCore(CancellationToken ct)
    {
        if (!File.Exists(FilePath)) return cache = new(0, []);
        var write = File.GetLastWriteTimeUtc(FilePath);
        if (cache is not null && write == cacheWrite) return cache;
        if (new FileInfo(FilePath).Length > 2_000_000) throw new IOException("Registro de dispositivos TV excessivo.");
        // Arquivo corrompido falha fechado (503 na TV); nunca vira "sem registro = liberado".
        var value = JsonSerializer.Deserialize<TvDeviceRegistry>(await File.ReadAllTextAsync(FilePath, ct), Json)
            ?? throw new IOException("Registro de dispositivos TV inválido.");
        if (value.Devices is null || value.Devices.Any(d => d is null || string.IsNullOrEmpty(d.Id) || d.TokenHash is not { Length: 64 }
            || d.PreviousTokenHash is not (null or { Length: 64 })))
            throw new IOException("Registro de dispositivos TV inválido.");
        cacheWrite = write;
        return cache = value;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public enum TvDeviceCheck { Valid, Invalid, Unavailable }

public sealed record TvDeviceValidation(TvDeviceCheck Check, TvDeviceRecord? Record = null, PermUser? User = null);

/// <summary>
/// Validação do cookie de dispositivo. Tudo que não for comprovadamente válido é Invalid (e revoga o registro quando o
/// motivo é definitivo); falha de banco/arquivo é Unavailable (503, sem revogar e sem liberar).
/// </summary>
public sealed class TvDeviceSessionService(TvDeviceSessionStore store, IOptions<TvDeviceSessionOptions> options,
    IPermissionRepository repository, INaLocalCredentialRepository credentials, TimeProvider? clock = null)
{
    public const string CookieName = "__Host-NaTvDevice";
    public const string SessionDeviceKey = "Auth:TvDeviceId";
    public const string SessionPendingKey = "Auth:TvDevicePending";
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public TvDeviceSessionOptions Options => options.Value;
    public bool Enabled => options.Value.Enabled;
    public TvDeviceSessionStore Store => store;

    public async Task<TvDeviceValidation> ValidateAsync(string? token, CancellationToken ct)
    {
        try
        {
            if (token is null || await store.MatchAsync(token, ct) is not { Record: var record } match) return new(TvDeviceCheck.Invalid);
            var now = time.GetUtcNow();
            if (record.RevokedAt is not null) return new(TvDeviceCheck.Invalid, record);
            // Token anterior fora da carência = cookie velho/copiado: recusado sem revogar a TV legítima (que tem o atual).
            if (match.Previous && (record.RotatedAt is not { } rotated || now - rotated > TimeSpan.FromHours(Options.PreviousTokenGraceHours)))
                return new(TvDeviceCheck.Invalid);
            if (!record.Active(now, Options)) return await Revoke(record, "expirado", ct);
            var user = await repository.ObterUsuarioAsync(record.UserId, ct);
            if (user is not { Ativo: true }) return await Revoke(record, "usuario-inativo", ct);
            var credential = await credentials.ObterCredencialAsync(record.UserId, ct);
            if (credential is null || credential.MustChangePassword || credential.PasswordUpdatedAt.Ticks != record.CredentialStamp)
                return await Revoke(record, "senha-alterada", ct);
            return new(TvDeviceCheck.Valid, record, user);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new(TvDeviceCheck.Unavailable); }
    }

    public Task<bool> RevokeAsync(string id, string reason, CancellationToken ct) => store.RevokeAsync(id, reason, ct);

    public CookieOptions Cookie(DateTimeOffset expires) => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true, Expires = expires,
    };

    public void DeleteCookie(HttpResponse response) =>
        response.Cookies.Delete(CookieName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });

    private async Task<TvDeviceValidation> Revoke(TvDeviceRecord record, string reason, CancellationToken ct)
    {
        await store.RevokeAsync(record.Id, reason, ct);
        return new(TvDeviceCheck.Invalid, record);
    }
}
