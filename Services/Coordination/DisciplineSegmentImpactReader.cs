using System.Data;
using APS.WebApp.Services.DisciplineAlias;
using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Reports;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace NA_Connect.Services.Coordination;

/// <summary>Documentos de um projeto + o resolvedor de sigla que as telas usam para ele. Somente leitura.</summary>
public sealed record ProjetoDocumentosSegmentos(string ProjectId, string Projeto, bool SemLdp, string Fonte,
    DisciplineSegmentRules Atual, int? NormaId, IReadOnlyList<DocumentoSegmentos> Documentos, Func<string?, SiglaResolvida> Resolver);

/// <summary>
/// Lê os documentos de UM projeto nas mesmas fontes das telas: LDP atual (PmoExecutiveData) ou, para projeto sem LDP,
/// o Timesheet corrigido pelo vínculo Projeto Timesheet + CR-OS (mesma consulta de PmoActualFactsProvider).
/// Não grava nada e não altera números de documento.
/// </summary>
public sealed class DisciplineSegmentImpactReader(IConfiguration configuration)
{
    private const string SqlProjeto = """
        SELECT COALESCE(Nome_Projeto, Project_ID_ACC), Sem_LDP, COALESCE(Timesheet_Projeto, '')
          FROM dbo.Projetos WHERE Project_ID_ACC = @projectId AND Is_Ativo = 1;
        """;
    // Espelho do ramo Sem_LDP de PmoActualFactsProvider.LerAsync (uma fonte, sem somar AFE e Doc1).
    private const string SqlDocumentosSemLdp = """
        SELECT t.DocumentoCodigo, t.doc1, t.doc2, t.doc3, t.doc4, t.doc5
          FROM dbo.Projetos p
          INNER JOIN dbo.VW_TimeSheet_NA_Corrigida_Projeto t
              ON UPPER(LTRIM(RTRIM(t.Projeto)))=UPPER(LTRIM(RTRIM(p.Timesheet_Projeto)))
             AND UPPER(LTRIM(RTRIM(t.doc1)))=UPPER(LTRIM(RTRIM(p.CR_OS)))
         WHERE p.Project_ID_ACC=@projectId AND p.Is_Ativo=1 AND p.Sem_LDP=1
           AND NULLIF(LTRIM(RTRIM(p.CR_OS)),'') IS NOT NULL;
        """;

    public async Task<bool> SemLdpAsync(string projectId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(configuration.GetConnectionString("LocalConnection"));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT Sem_LDP FROM dbo.Projetos WHERE Project_ID_ACC=@projectId AND Is_Ativo=1", conn);
        cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 255).Value = projectId;
        return await cmd.ExecuteScalarAsync(ct) is bool semLdp && semLdp;
    }

    /// <summary>
    /// Diagnóstico por documento de projeto SEM LDP (inventário reconstruído do Timesheet, como no Resumo), já com a
    /// posição de sigla configurada para o projeto.
    /// </summary>
    public async Task<ProjetoSiglasDiagnostico?> DiagnosticoSemLdpAsync(string projectId, CancellationToken ct)
    {
        var p = await LerAsync(projectId, ct);
        if (p is null || !p.SemLdp) return null;
        bool Reconhecida(string? s) => p.Resolver(s).Reconhecida;
        return new(p.ProjectId, p.Projeto, p.Documentos.Select(d =>
        {
            var escolha = DisciplineSegments.Escolher(d.Codigo, d.Segmentos, p.Atual, Reconhecida);
            var sigla = string.IsNullOrWhiteSpace(escolha.Valor) ? null : escolha.Valor.Trim();
            var r = p.Resolver(sigla);
            return new DocumentoSiglaDiagnostico("HISTORICO:" + d.Codigo, d.Codigo, null, null, true,
                CoordinationDisciplineResolver.DisciplineKey(sigla), sigla, null, null, r.Reconhecida ? r.Disciplina : null,
                escolha.Diagnostico ?? r.Motivo, p.NormaId, null,
                $"Timesheet (projeto sem LDP){(escolha.ExcecaoId is null ? "" : $" · exceção {escolha.ExcecaoId}: sigla no DOC{escolha.Posicao}")}");
        }).ToArray());
    }

    public async Task<ProjetoDocumentosSegmentos?> LerAsync(string projectId, CancellationToken ct)
    {
        string projeto; bool semLdp; string projetoTimesheet;
        var documentos = new List<DocumentoSegmentos>();
        await using (var conn = new SqlConnection(configuration.GetConnectionString("LocalConnection")))
        {
            await conn.OpenAsync(ct);
            await using (var cmd = new SqlCommand(SqlProjeto, conn))
            {
                cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 255).Value = projectId;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) return null;
                projeto = r.GetString(0).Trim(); semLdp = r.GetBoolean(1); projetoTimesheet = r.GetString(2);
            }
            if (semLdp)
            {
                await using var cmd = new SqlCommand(SqlDocumentosSemLdp, conn) { CommandTimeout = 60 };
                cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 255).Value = projectId;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                var vistos = new HashSet<string>(StringComparer.Ordinal);
                while (await r.ReadAsync(ct))
                {
                    string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
                    var codigo = CoordinationDisciplineResolver.ResolveDocumentCode(S(0), S(1), S(2), S(3), S(4), S(5));
                    var chave = ProjectPlanningRules.Key(codigo);
                    if (chave == "" || !vistos.Add(chave)) continue;
                    // Colunas físicas Doc1..Doc5: o DocumentoCodigo antigo pode ter segmentos vazios.
                    documentos.Add(new(chave, new[] { S(1), S(2), S(3), S(4), S(5) }));
                }
            }
        }

        var norma = await ProjectDoc3Disciplines.LoadAsync(configuration, projectId, ct);
        if (!semLdp)
        {
            var ldp = await PmoExecutiveData.ReadAsync(configuration, projectId, false, ct, currentPlanningSnapshot: true);
            var vistos = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in ldp.Documentos.Where(d => d.IsDocument && !d.SemConteudoDocumental))
            {
                var chave = ProjectPlanningRules.Key(d.Codigo);
                if (!chave.Any(char.IsLetterOrDigit) || !vistos.Add(chave)) continue;
                documentos.Add(new(chave, DisciplineSegments.DoCodigo(d.Codigo)));
            }
        }
        // Projeto com LDP: só a Norma decide (regra vigente). Sem LDP: Norma e, sem correspondência, o Normalizador.
        IReadOnlyList<RegraSigla> regras = semLdp
            ? (await new SqlSiglaDisciplinaStore(configuration).ListarAsync(ct)).Where(r => r.Ativo).ToArray() : [];
        var contexto = new ContextoResolucao(new[] { projectId, projetoTimesheet }, FontesConhecidas.Timesheet);
        return new(projectId, projeto, semLdp,
            semLdp ? "Timesheet corrigido (projeto sem LDP)" : "LDP atual",
            norma.Regras, norma.NormaId, documentos,
            DisciplineSegmentImpact.Resolvedor(norma.NormaId, norma.Mapeamentos, semLdp, regras, contexto));
    }
}
