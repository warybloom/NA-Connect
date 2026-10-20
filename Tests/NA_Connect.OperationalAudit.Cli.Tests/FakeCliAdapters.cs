using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>Fakes usados por todos os testes do CLI — nunca SQL Server real, nunca ACC real (Checkpoint 4, seção 18).</summary>
    public sealed class FakeCliSqlQueryExecutor : ICliSqlQueryExecutor
    {
        private readonly List<(Func<string, bool> Casa, DataTable Resultado)> _respostas = new();
        public bool LancarExcecao { get; set; }
        public List<string> SqlExecutados { get; } = new();

        public void ConfigurarResposta(string trechoDoSql, DataTable resultado) =>
            _respostas.Add((sql => sql.Contains(trechoDoSql, StringComparison.OrdinalIgnoreCase), resultado));

        public Task<DataTable> ExecutarAsync(string sql, IReadOnlyDictionary<string, object?>? parametros = null, CancellationToken ct = default)
        {
            SqlExecutados.Add(sql);
            if (LancarExcecao) throw new InvalidOperationException("Falha simulada de conexão.");

            for (int i = _respostas.Count - 1; i >= 0; i--)
            {
                if (_respostas[i].Casa(sql)) return Task.FromResult(_respostas[i].Resultado);
            }
            return Task.FromResult(new DataTable());
        }
    }

    public sealed class FakeAccConnectivityProbe : IAccConnectivityProbe
    {
        public bool DeveTerSucesso { get; set; } = true;
        public Task<bool> TestarTokenEGetAsync(CancellationToken ct = default) => Task.FromResult(DeveTerSucesso);
    }
}
