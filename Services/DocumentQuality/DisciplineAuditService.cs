using APS.WebApp.Services.Planning;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.DocumentQuality;

public sealed record DisciplineAuditProject(string ProjectId, string ProjectName);

public sealed record DisciplineAuditRow(
    string? RawDiscipline,
    int? DisciplineStandardId,
    string? StandardName,
    string? StandardPt,
    string? StandardEn,
    string Status,
    int Documents,
    int ProjectsCount,
    IReadOnlyList<string> Projects);

public sealed record DisciplineAuditResult(
    string Scope,
    DateTimeOffset ExecutedAt,
    int ProjectsAnalyzed,
    int Documents,
    int RawNames,
    int StandardizedNames,
    int UnmappedNames,
    IReadOnlyList<DisciplineAuditRow> Rows,string Source="coordenacao.ProjetoNorma -> coordenacao.NormaMapeamento (Documents conta mapeamentos)");

/// <summary>
/// Auditoria global/local de nomes de disciplina. A fonte são os nomes das Normas de Coordenação vinculadas aos projetos;
/// AT não pertence ao universo documental. O resultado final é binário: PADRONIZADO ou NAO_MAPEADO.
/// Variantes/erros de grafia são apenas aliases de uma disciplina padrão global.
/// </summary>
public sealed class DisciplineAuditService(
    IConfiguration configuration,
    DisciplineStandardRepository standards)
{
    public Task<DisciplineAuditResult> ReadAsync(
        string projectId,
        string projectName,
        CancellationToken ct) =>
        ReadAsync([new DisciplineAuditProject(projectId, projectName)], "PROJECT", ct);

    public async Task<IReadOnlyList<DisciplineAuditProject>> ListAllActiveProjectsAsync(CancellationToken ct)
    {
        var result = new List<DisciplineAuditProject>();
        await using var con = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão indisponível."));
        await con.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT Project_ID_ACC, COALESCE(Nome_Projeto, Project_ID_ACC)
            FROM dbo.Projetos
            WHERE Is_Ativo=1
              AND Project_ID_ACC IS NOT NULL
              AND LTRIM(RTRIM(Project_ID_ACC))<>''
            ORDER BY COALESCE(Nome_Projeto, Project_ID_ACC);
            """, con);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new(reader.GetString(0), reader.GetString(1)));
        return result;
    }

    public async Task<DisciplineAuditResult> ReadAsync(
        IReadOnlyList<DisciplineAuditProject> projects,
        string scope,
        CancellationToken ct)
    {
        if (projects.Count == 0)
            return new(scope, DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, []);

        var standardsList = await standards.ListAsync(ct);
        var canonicalMap=DisciplineStandardRepository.CanonicalNameMap(standardsList);
        var standardsByName=standardsList.Where(x=>x.Active).ToDictionary(x=>x.StandardName,StringComparer.Ordinal);
        var aliasMap=canonicalMap.ToDictionary(x=>x.Key,x=>standardsByName[x.Value],StringComparer.Ordinal);

        var rawRows = new List<(string ProjectId, string ProjectName, string? Raw, int Documents)>();
        await using var con = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão indisponível."));
        await con.OpenAsync(ct);

        var parameters = projects.Select((_, i) => $"@p{i}").ToArray();
        var sql = $"""
            SELECT PN.Project_ID_ACC,
                   COALESCE(P.Nome_Projeto, PN.Project_ID_ACC) AS Nome_Projeto,
                   NULLIF(LTRIM(RTRIM(M.Disciplina)), '') AS Discipline,
                   COUNT(*) AS Documents
            FROM coordenacao.ProjetoNorma PN
            JOIN coordenacao.NormaMapeamento M ON M.NormaId=PN.NormaId
            LEFT JOIN dbo.Projetos P ON P.Project_ID_ACC=PN.Project_ID_ACC
            WHERE PN.Project_ID_ACC IN ({string.Join(",", parameters)})
            GROUP BY PN.Project_ID_ACC, COALESCE(P.Nome_Projeto, PN.Project_ID_ACC),
                     NULLIF(LTRIM(RTRIM(M.Disciplina)), '')
            ORDER BY Nome_Projeto, Discipline;
            """;

        await using (var cmd = new SqlCommand(sql, con) { CommandTimeout = 120 })
        {
            for (var i = 0; i < projects.Count; i++)
                cmd.Parameters.AddWithValue(parameters[i], projects[i].ProjectId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rawRows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt32(3)));
        }

        var rows = rawRows
            .GroupBy(x => DisciplineCanonicalRules.Normalize(x.Raw))
            .Select(group =>
            {
                var representative = group.Select(x => x.Raw).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                var key = DisciplineCanonicalRules.Normalize(representative);
                aliasMap.TryGetValue(key, out var standard);
                var projectNames = group.Select(x => x.ProjectName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new DisciplineAuditRow(
                    representative,
                    standard?.DisciplineStandardId,
                    standard?.StandardName,
                    standard?.PortugueseName,
                    standard?.EnglishName,
                    standard is null ? "NAO_MAPEADO" : "PADRONIZADO",
                    group.Sum(x => x.Documents),
                    projectNames.Length,
                    projectNames);
            })
            .OrderBy(x => x.Status == "PADRONIZADO" ? 1 : 0)
            .ThenByDescending(x => x.ProjectsCount)
            .ThenByDescending(x => x.Documents)
            .ThenBy(x => x.RawDiscipline, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(
            scope,
            DateTimeOffset.UtcNow,
            projects.Count,
            rawRows.Sum(x => x.Documents),
            rows.Length,
            rows.Count(x => x.Status == "PADRONIZADO"),
            rows.Count(x => x.Status == "NAO_MAPEADO"),
            rows);
    }
}
