using System.Data;
using APS.WebApp.Services.Planning;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace NA_Connect.Services.Coordination;

/// <summary>
/// Ponto canônico (15/10/2026) para classificar um N&amp;A Document Number por disciplina: DOC3 (3ª parte do código)
/// -&gt; 2 primeiros caracteres -&gt; Norma de Coordenação do PRÓPRIO projeto -&gt; Nome_Padrao global. Usado pelo previsto
/// (LDP) para cair na mesma disciplina que o realizado (Timesheet, PmoActualFactsProvider). Somente leitura.
/// </summary>
public sealed class ProjectDoc3Disciplines
{
    public int? NormaId { get; }
    public IReadOnlyDictionary<string, string> Mapeamentos { get; }
    public IReadOnlyDictionary<string, string> CanonicalNames { get; }

    public ProjectDoc3Disciplines(int? normaId, IReadOnlyDictionary<string, string> mapeamentos, IReadOnlyDictionary<string, string> canonicalNames)
    {
        NormaId = normaId;
        Mapeamentos = mapeamentos;
        CanonicalNames = canonicalNames;
    }

    /// <summary>DOC3 do código do documento (Doc1-Doc2-Doc3-Doc4-Doc5). Menos de 3 segmentos: null (não inferir).</summary>
    public static string? Doc3(string? documentCode)
    {
        var parts = documentCode?.Split('-');
        return parts is { Length: >= 3 } && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2].Trim() : null;
    }

    public CoordinationResolution ResolveDocument(string? documentCode) =>
        CoordinationDisciplineResolver.Resolve(Doc3(documentCode), NormaId, Mapeamentos, CanonicalNames);

    /// <summary>Disciplina cadastrada na Norma do projeto, ou null quando DOC3 não resolve.</summary>
    public string? Canonical(string? documentCode)
    {
        var r = ResolveDocument(documentCode);
        return r.Matched ? r.Discipline : null;
    }

    public static async Task<ProjectDoc3Disciplines> LoadAsync(IConfiguration configuration, string projectId, CancellationToken ct)
    {
        int? normaId = null;
        var mapa = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var conn = new SqlConnection(configuration.GetConnectionString("LocalConnection")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("""
                SELECT n.NormaId, m.Sigla, m.Disciplina
                  FROM coordenacao.ProjetoNorma pn
                  JOIN coordenacao.Norma n ON n.NormaId=pn.NormaId AND n.Ativa=1
                  LEFT JOIN coordenacao.NormaMapeamento m ON m.NormaId=n.NormaId
                 WHERE pn.Project_ID_ACC=@projectId
                """, conn);
            cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 255).Value = projectId;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                normaId = r.GetInt32(0);
                if (!r.IsDBNull(1)) mapa[CoordinationDisciplineResolver.Normalize(r.GetString(1))] = r.GetString(2);
            }
        }
        // Disciplina é definida exclusivamente na Norma do próprio projeto.
        return new(normaId, mapa, new Dictionary<string, string>(StringComparer.Ordinal));
    }
}
