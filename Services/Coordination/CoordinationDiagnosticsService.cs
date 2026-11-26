using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace NA_Connect.Services.Coordination;

/// <summary>
/// Diagnóstico de auditoria da Norma de Coordenação: para os projetos informados, resolve cada doc3
/// distinto de gestao.TimesheetSnapshotAtual usando a Norma do PRÓPRIO projeto (nunca cruza normas entre
/// projetos) e agrega as siglas SEM correspondência por sigla normalizada, com quantidade de lançamentos,
/// HH total e a lista de projetos onde apareceu. Só leitura — nunca exclui/altera lançamento nenhum.
/// </summary>
public sealed class CoordinationDiagnosticsService(IConfiguration configuration, CoordinationStandardRepository repository)
{
    private readonly string _cs = configuration.GetConnectionString("LocalConnection");

    public async Task<List<UnmappedSiglaDiagnosticDto>> ObterSiglasNaoMapeadasAsync(IReadOnlyList<string> projectIds, CancellationToken ct)
    {
        var acumulado = new Dictionary<string, UnmappedSiglaDiagnosticDto>();
        foreach (var projectId in projectIds)
        {
            var (normaId, mapa) = repository.ObterMapaDoProjeto(projectId);
            using var conn = new SqlConnection(_cs);
            await conn.OpenAsync(ct);
            using var cmd = new SqlCommand("SELECT Doc3, HorasTrabalhadas FROM gestao.TimesheetSnapshotAtual WHERE Project_ID_ACC=@p", conn);
            cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId;
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var doc3 = reader.IsDBNull(0) ? null : reader.GetString(0);
                var hh = reader.IsDBNull(1) ? (decimal?)null : reader.GetDecimal(1);
                var resolution = CoordinationDisciplineResolver.Resolve(doc3, normaId, mapa);
                if (resolution.Matched || resolution.NormalizedCode == "") continue; // doc3 vazio não é "sigla desconhecida"
                if (!acumulado.TryGetValue(resolution.NormalizedCode, out var diag))
                {
                    diag = new() { Sigla = resolution.NormalizedCode };
                    acumulado[resolution.NormalizedCode] = diag;
                }
                diag.Lancamentos++;
                diag.Hh = (diag.Hh ?? 0) + (hh ?? 0);
                if (!diag.Projetos.Contains(projectId)) diag.Projetos.Add(projectId);
            }
        }
        return acumulado.Values.OrderByDescending(d => d.Lancamentos).ToList();
    }
}
