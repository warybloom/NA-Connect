using System.Data;
using APS.WebApp.Services.Planning;
using Microsoft.Data.SqlClient;
using NA_Connect.Services.Coordination;

namespace APS.WebApp.Services.Reports;

/// <summary>Sigla e disciplina definidas pela Norma vinculada ao próprio projeto.</summary>
public sealed record JamileNormaSigla(string Sigla, string Disciplina, string? Canonica);

/// <summary>Inconsistência do cadastro da Norma (somente diagnóstico; nada é corrigido automaticamente).</summary>
public sealed record JamileNormaDiagnostico(string Tipo, string Mensagem, IReadOnlyList<string> Disciplinas, IReadOnlyList<string> Siglas);

public sealed record JamileDisciplinasNorma(string ProjectId, int? NormaId, string? NormaNome,
    IReadOnlyList<JamileNormaSigla> Siglas, IReadOnlyList<JamileNormaDiagnostico> Diagnosticos);

public sealed record JamileProgramacaoLinha(int IdRegistro, string? Documento, string? Tarefa, string? DisciplinaWeekly,
    string? SiglaDoc3, string? DisciplinaNorma, string? DisciplinaCanonica, string FonteDisciplina,
    string? Elaborador, string? Verificador, string? Aprovador,
    decimal? HhElaboracao, decimal? HhVerificacao, decimal? HhAprovacao,
    DateTime? Baseline, DateTime? Reprogramado, DateTime DataAtividade, string? Status, string? Fase);

public sealed record JamileProgramacaoDia(DateTime Data, int Quantidade);

/// <summary>Registro DADO sem Replanned nem Baseline: não pertence a semana alguma; é listado à parte, nunca datado artificialmente.</summary>
public sealed record JamileProgramacaoSemData(int IdRegistro, string? Documento, string? Tarefa, string? DisciplinaWeekly,
    string? SiglaDoc3, string? DisciplinaNorma, string? DisciplinaCanonica, string FonteDisciplina,
    string? Elaborador, string? Status, string? Fase, int? LinhaExcel);

public sealed record JamileProgramacao(string ProjectId, DateTime Inicio, DateTime Fim, DateTime? UltimaSincronizacao,
    int RegistrosProjeto, int RegistrosSemData, IReadOnlyList<JamileProgramacaoDia> DatasComProgramacao,
    IReadOnlyList<JamileProgramacaoLinha> Linhas, string? ArquivoOrigem = null,
    IReadOnlyList<JamileProgramacaoSemData>? LinhasSemData = null)
{
    /// <summary>Registros DADO do projeto com Doc4=AT (atividade) retirados de TODA a programação: contadores, datas e linhas.</summary>
    public int AtividadesAtExcluidas { get; init; }
}

/// <summary>
/// Fonte exclusiva das quatro telas "Gestão à Vista (Jamile)". Somente leitura.
/// Disciplina = DOC3 do número do documento → sigla da Norma de Coordenação vinculada ao projeto → nome da Norma,
/// com o MESMO <see cref="CoordinationDisciplineResolver"/> já usado por PmoExecutiveData/PmoActualFactsProvider.
/// O canônico global (DisciplinaPadrao) é devolvido apenas para casar com o realizado oficial, que já vem agrupado por ele.
/// </summary>
public sealed class GestaoVistaJamileRepository(IConfiguration configuration, CoordinationStandardRepository coordination,
    DisciplineStandardRepository standards)
{
    public const string TipoMesmaCanonica = "MESMA_DISCIPLINA_COM_NOMES_DIFERENTES";
    public const string TipoSemCanonica = "NOME_DA_NORMA_SEM_DISCIPLINA_PADRAO";
    public const int LimiteLinhas = 5000;

    private string ConnectionString => configuration.GetConnectionString("LocalConnection")
        ?? throw new InvalidOperationException("Conexão de relatórios indisponível.");

    public async Task<JamileDisciplinasNorma> DisciplinasAsync(string projectId, CancellationToken ct)
    {
        var (normaId, mapa) = coordination.ObterMapaDoProjeto(projectId);
        string? nomeNorma = null;
        if (normaId is not null)
        {
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT Nome FROM coordenacao.Norma WHERE NormaId=@id", conn);
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = normaId.Value;
            nomeNorma = await cmd.ExecuteScalarAsync(ct) as string;
        }
        var siglas = mapa.Select(kv => new JamileNormaSigla(kv.Key, kv.Value.Trim(), kv.Value.Trim()))
            .OrderBy(s => s.Disciplina, StringComparer.Ordinal).ThenBy(s => s.Sigla, StringComparer.Ordinal).ToArray();
        return new(projectId, normaId, nomeNorma, siglas, Diagnosticar(siglas));
    }

    public static IReadOnlyList<JamileNormaDiagnostico> Diagnosticar(IReadOnlyList<JamileNormaSigla> siglas)
    {
        var diagnosticos = new List<JamileNormaDiagnostico>();
        foreach (var g in siglas.Where(s => s.Canonica is not null).GroupBy(s => s.Canonica!, StringComparer.Ordinal))
        {
            var nomes = g.Select(s => s.Disciplina).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            if (nomes.Length > 1)
                diagnosticos.Add(new(TipoMesmaCanonica,
                    $"A Norma usa {nomes.Length} nomes para a mesma disciplina padrão '{g.Key}': {string.Join(", ", nomes)}.",
                    nomes, g.Select(s => s.Sigla).OrderBy(s => s, StringComparer.Ordinal).ToArray()));
        }
        foreach (var g in siglas.Where(s => s.Canonica is null).GroupBy(s => s.Disciplina, StringComparer.Ordinal))
            diagnosticos.Add(new(TipoSemCanonica,
                $"'{g.Key}' não possui nome informado na Norma de Coordenação deste projeto.",
                new[] { g.Key }, g.Select(s => s.Sigla).OrderBy(s => s, StringComparer.Ordinal).ToArray()));
        return diagnosticos;
    }

    /// <summary>
    /// Programação semanal (dbo.tb_WeeklyEngineeringSchedule, linhas DADO) cuja data da atividade — Reprogramada,
    /// senão Baseline — cai em [inicio, fim]. Sem paginação: o filtro de data é aplicado no SQL.
    /// </summary>
    public async Task<JamileProgramacao> ProgramacaoAsync(string projectId, DateTime inicio, DateTime fim, CancellationToken ct)
    {
        var (normaId, mapa) = coordination.ObterMapaDoProjeto(projectId);
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        var posicao = await DisciplineSegmentStore.ObterAsync(conn, projectId, ct);
        // Gestão à Vista = documentos: Doc4=AT (4º segmento do Numero_NA_Atividade; a programação não tem Doc4 estruturado)
        // sai ANTES de qualquer contagem. Por isso registros/sem data/datas são contados aqui, linha a linha, com a mesma regra.
        await using var cmd = new SqlCommand("""
            SELECT MAX(Data_Sincronizacao) AS UltimaSincronizacao,
                   MAX(Arquivo_Origem) AS ArquivoOrigem
              FROM dbo.tb_WeeklyEngineeringSchedule
             WHERE Project_ID_ACC=@projectId AND Tipo_Registro='DADO';
            SELECT Numero_NA_Atividade, COALESCE(Weekly_Replanned_Date, Weekly_Baseline_Date) AS Data
              FROM dbo.tb_WeeklyEngineeringSchedule
             WHERE Project_ID_ACC=@projectId AND Tipo_Registro='DADO';
            SELECT TOP (@limite) Id_Registro, Numero_NA_Atividade, Nome_Tarefa, Disciplina, Recurso,
                   Weekly_Responsible_Verification, Weekly_Responsible_Approval,
                   Weekly_HH_Previsto, Weekly_HH_Verificacao, Weekly_HH_Aprovacao,
                   Weekly_Baseline_Date, Weekly_Replanned_Date, Weekly_Progress_Status, Weekly_Phase
              FROM dbo.tb_WeeklyEngineeringSchedule
             WHERE Project_ID_ACC=@projectId AND Tipo_Registro='DADO'
               AND COALESCE(Weekly_Replanned_Date, Weekly_Baseline_Date) BETWEEN @inicio AND @fim
             ORDER BY COALESCE(Weekly_Replanned_Date, Weekly_Baseline_Date), Disciplina, Numero_NA_Atividade, Id_Registro;
            SELECT TOP (@limite) Id_Registro, Numero_NA_Atividade, Nome_Tarefa, Disciplina, Recurso,
                   Weekly_Progress_Status, Weekly_Phase, Linha_Excel
              FROM dbo.tb_WeeklyEngineeringSchedule
             WHERE Project_ID_ACC=@projectId AND Tipo_Registro='DADO'
               AND COALESCE(Weekly_Replanned_Date, Weekly_Baseline_Date) IS NULL
             ORDER BY Linha_Excel, Id_Registro;
            """, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 200).Value = projectId;
        cmd.Parameters.Add("@inicio", SqlDbType.Date).Value = inicio.Date;
        cmd.Parameters.Add("@fim", SqlDbType.Date).Value = fim.Date;
        cmd.Parameters.Add("@limite", SqlDbType.Int).Value = LimiteLinhas;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        DateTime? sync = null; string? arquivo = null;
        if (await reader.ReadAsync(ct))
        {
            sync = reader.IsDBNull(0) ? null : reader.GetDateTime(0);
            arquivo = reader.IsDBNull(1) ? null : NullIfBlank(reader.GetString(1));
        }
        await reader.NextResultAsync(ct);
        var registrosProjeto = new List<(string? Documento, DateTime? Data)>();
        while (await reader.ReadAsync(ct))
            registrosProjeto.Add((reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetDateTime(1)));
        var resumo = ResumirSemAtividades(registrosProjeto);
        await reader.NextResultAsync(ct);
        var linhas = new List<JamileProgramacaoLinha>();
        while (await reader.ReadAsync(ct))
        {
            string? Text(int i) => reader.IsDBNull(i) ? null : NullIfBlank(reader.GetString(i));
            decimal? Dec(int i) => reader.IsDBNull(i) ? null : reader.GetDecimal(i);
            DateTime? Date(int i) => reader.IsDBNull(i) ? null : reader.GetDateTime(i);
            var documento = Text(1);
            if (LdpItemRules.IsActivityCode(documento)) continue;
            var (doc3, resolve) = Disciplina(documento, normaId, mapa, posicao);
            var baseline = Date(10); var reprogramado = Date(11);
            linhas.Add(new(reader.GetInt32(0), documento, Text(2), Text(3), doc3,
                resolve.Matched ? resolve.Discipline : null,
                resolve.Matched ? resolve.Discipline : null, resolve.Source,
                Text(4), Text(5), Text(6), Dec(7), Dec(8), Dec(9), baseline, reprogramado,
                (reprogramado ?? baseline)!.Value, Text(12), Text(13)));
        }
        await reader.NextResultAsync(ct);
        var semDataLinhas = new List<JamileProgramacaoSemData>();
        while (await reader.ReadAsync(ct))
        {
            string? Text(int i) => reader.IsDBNull(i) ? null : NullIfBlank(reader.GetString(i));
            var documento = Text(1);
            if (LdpItemRules.IsActivityCode(documento)) continue;
            var (doc3, resolve) = Disciplina(documento, normaId, mapa, posicao);
            semDataLinhas.Add(new(reader.GetInt32(0), documento, Text(2), Text(3), doc3,
                resolve.Matched ? resolve.Discipline : null, resolve.Matched ? resolve.Discipline : null, resolve.Source,
                Text(4), Text(5), Text(6), reader.IsDBNull(7) ? null : reader.GetInt32(7)));
        }
        return new(projectId, inicio.Date, fim.Date, sync, resumo.Registros, resumo.SemData, resumo.Dias, linhas, arquivo, semDataLinhas)
            { AtividadesAtExcluidas = resumo.AtividadesAt };
    }

    /// <summary>Contadores da programação do projeto SEM Doc4=AT (mesma regra das linhas): registros, sem data e quantidade por data.</summary>
    public static (int Registros, int SemData, IReadOnlyList<JamileProgramacaoDia> Dias, int AtividadesAt) ResumirSemAtividades(
        IEnumerable<(string? Documento, DateTime? Data)> registros)
    {
        var lista = registros.ToList();
        var documentos = lista.Where(r => !LdpItemRules.IsActivityCode(r.Documento)).ToList();
        var dias = documentos.Where(r => r.Data.HasValue).GroupBy(r => r.Data!.Value.Date)
            .OrderBy(g => g.Key).Select(g => new JamileProgramacaoDia(g.Key, g.Count())).ToArray();
        return (documentos.Count, documentos.Count(r => !r.Data.HasValue), dias, lista.Count - documentos.Count);
    }

    // Mesma extração de PmoExecutiveData: N&A Document Number = Doc1-Doc2-Doc3-Doc4-Doc5, sigla no DOC3 salvo exceção pontual do projeto.
    private static (string? Doc3, CoordinationResolution Resolve) Disciplina(string? documento, int? normaId,
        IReadOnlyDictionary<string, string> mapa, DisciplineSegmentRules posicao)
    {
        var doc3 = DisciplineSegments.EscolherNaNorma(documento, posicao, normaId, mapa).Valor;
        return (doc3, CoordinationDisciplineResolver.Resolve(doc3, normaId, mapa));
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
