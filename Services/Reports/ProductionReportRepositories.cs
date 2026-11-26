using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

// =============================================================================================
// RELATÓRIO DE PRODUÇÃO — repositórios SOMENTE LEITURA (SELECT) sobre fontes já existentes.
// Nenhuma tabela/view nova, nenhuma escrita. Toda consulta é parametrizada e filtrada por
// Project_ID_ACC = @projectId. Todos os contadores/agrupamentos são calculados no BACKEND sobre o
// conjunto inteiro do projeto (nunca sobre a página exibida no frontend).
// Fontes são snapshot por projeto (sincronizador faz DELETE+insert por Project_ID_ACC), então ler por
// projeto = estado atual. Ver RELATORIO_PRODUCAO_PROGRESSO.md.
// =============================================================================================

/// <summary>Contagem por valor bruto como armazenado (NULL continua NULL — o frontend exibe "Não informado").</summary>
public sealed record ContagemPorValor(string? Valor, long Quantidade);

internal static class ProductionSql
{
    public static async Task<SqlConnection> AbrirAsync(IConfiguration configuration, CancellationToken ct)
    {
        var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
        await connection.OpenAsync(ct);
        return connection;
    }

    public static SqlCommand Comando(string sql, SqlConnection connection, string projectId)
    {
        var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        return command;
    }

    public static void Texto(SqlCommand c, string nome, int tamanho, string? valor) =>
        c.Parameters.Add(nome, SqlDbType.NVarChar, tamanho).Value = (object?)valor ?? DBNull.Value;

    public static void Busca(SqlCommand c, string? search) =>
        c.Parameters.Add("@search", SqlDbType.NVarChar, 202).Value = search is null ? DBNull.Value : $"%{search}%";

    public static void Pagina(SqlCommand c, int page, int pageSize)
    {
        c.Parameters.Add("@offset", SqlDbType.Int).Value = checked((page - 1) * pageSize);
        c.Parameters.Add("@pageSize", SqlDbType.Int).Value = pageSize;
    }

    /// <summary>Lê um result set (Valor, Quantidade) e avança para o próximo.</summary>
    public static async Task<IReadOnlyList<ContagemPorValor>> ContagensAsync(SqlDataReader r, CancellationToken ct)
    {
        var lista = new List<ContagemPorValor>();
        while (await r.ReadAsync(ct)) lista.Add(new(r.IsDBNull(0) ? null : Convert.ToString(r.GetValue(0)), r.GetInt64(1)));
        await r.NextResultAsync(ct);
        return lista;
    }

    public static string? Str(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
    public static DateTime? Data(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
}

// ------------------------------------------------------------------------------ CHANGE ORDER ----

public sealed record ChangeOrderRow(int IdRegistro, string? Item, string? NumeroDesvio, string? Descricao, string? Disciplina,
    string? Data, string? QuantidadeHH, string? Status, string? DesvioInterno, string? ChangeOrder);
public sealed record ChangeOrderReport(long TotalRows, long FilteredRows, DateTime? LastSync, int Page, int PageSize,
    IReadOnlyList<ContagemPorValor> PorChangeOrder, IReadOnlyList<ContagemPorValor> PorStatus, IReadOnlyList<ChangeOrderRow> Rows);

public interface IChangeOrderReportRepository
{
    Task<ChangeOrderReport> ReadAsync(string projectId, string? changeOrder, string? search, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// Visão Change Order sobre a MESMA fonte de Desvio de Escopo (dbo.vw_Latest_ScopeDeviation, campo
/// CHANGE_ORDER). A semântica do valor de CHANGE_ORDER (flag ou número) não está comprovada — por isso
/// nenhuma linha é classificada como "é/não é change order"; a distribuição mostra o valor bruto.
/// QUANT_HH/DATA são texto na fonte e nunca são somados/convertidos.
/// </summary>
public sealed class ChangeOrderReportRepository(IConfiguration configuration) : IChangeOrderReportRepository
{
    private const string Source = "SELECT Id_Registro, ITEM, DESCRICAO, DISCIPLINA, N_DO_DESVIO, DATA, QUANT_HH, STATUS, DESVIO_INTERNO, CHANGE_ORDER, Data_Sincronizacao FROM dbo.vw_Latest_ScopeDeviation WHERE Project_ID_ACC = @projectId";
    private const string Filtro = "(@changeOrder IS NULL OR CHANGE_ORDER = @changeOrder) AND (@search IS NULL OR N_DO_DESVIO LIKE @search OR DESCRICAO LIKE @search)";

    public static readonly string Query = $"""
        WITH Source AS ({Source})
        SELECT COUNT_BIG(*), COUNT_BIG(CASE WHEN {Filtro} THEN 1 END), MAX(Data_Sincronizacao) FROM Source;
        WITH Source AS ({Source})
        SELECT CHANGE_ORDER, COUNT_BIG(*) FROM Source GROUP BY CHANGE_ORDER ORDER BY COUNT_BIG(*) DESC, CHANGE_ORDER;
        WITH Source AS ({Source})
        SELECT STATUS, COUNT_BIG(*) FROM Source GROUP BY STATUS ORDER BY COUNT_BIG(*) DESC, STATUS;
        WITH Source AS ({Source})
        SELECT Id_Registro, ITEM, N_DO_DESVIO, DESCRICAO, DISCIPLINA, DATA, QUANT_HH, STATUS, DESVIO_INTERNO, CHANGE_ORDER
        FROM Source WHERE {Filtro}
        ORDER BY Id_Registro OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
        """;

    public async Task<ChangeOrderReport> ReadAsync(string projectId, string? changeOrder, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = await ProductionSql.AbrirAsync(configuration, cancellationToken);
        using var command = ProductionSql.Comando(Query, connection, projectId);
        ProductionSql.Texto(command, "@changeOrder", 4000, changeOrder);
        ProductionSql.Busca(command, search);
        ProductionSql.Pagina(command, page, pageSize);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);
        long total = r.GetInt64(0), filtered = r.GetInt64(1); var lastSync = ProductionSql.Data(r, 2);
        await r.NextResultAsync(cancellationToken);
        var porCo = await ProductionSql.ContagensAsync(r, cancellationToken);
        var porStatus = await ProductionSql.ContagensAsync(r, cancellationToken);
        var rows = new List<ChangeOrderRow>();
        while (await r.ReadAsync(cancellationToken))
            rows.Add(new(r.GetInt32(0), ProductionSql.Str(r, 1), ProductionSql.Str(r, 2), ProductionSql.Str(r, 3), ProductionSql.Str(r, 4),
                ProductionSql.Str(r, 5), ProductionSql.Str(r, 6), ProductionSql.Str(r, 7), ProductionSql.Str(r, 8), ProductionSql.Str(r, 9)));
        return new(total, filtered, lastSync, page, pageSize, porCo, porStatus, rows);
    }
}

// ------------------------------------------------------------------------ DESVIO DE QUALIDADE ----

public sealed record QualityDeviationRow(long Id, string? Disciplina, string? NumeroDocumento, int? QuantidadeDocumentos,
    string? DescricaoInconsistencia, string? TipoInconsistencia, string? Responsavel, DateTime? DataConclusao);
public sealed record QualityDeviationReport(long TotalRows, long FilteredRows, long ComDataConclusao, long SemDataConclusao,
    long? SomaQuantidadeDocumentos, DateTime? LastSync, int Page, int PageSize,
    IReadOnlyList<ContagemPorValor> PorTipo, IReadOnlyList<ContagemPorValor> PorDisciplina, IReadOnlyList<ContagemPorValor> PorResponsavel,
    IReadOnlyList<QualityDeviationRow> Rows);

public interface IQualityDeviationReportRepository
{
    Task<QualityDeviationReport> ReadAsync(string projectId, string? disciplina, string? tipo, string? search, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// Fonte: [dbo].[DESVIO DE QUALIDADE] (snapshot por projeto da Etapa 17). "Com/sem data de conclusão" é
/// leitura direta de DataConclusao — não é classificado como "aberto/fechado" (regra não comprovada).
/// SomaQuantidadeDocumentos soma só valores conhecidos (NULL quando nenhum é conhecido).
/// </summary>
public sealed class QualityDeviationReportRepository(IConfiguration configuration) : IQualityDeviationReportRepository
{
    private const string Source = "SELECT Id, Disciplina, NumeroDocumento, QuantidadeDocumentos, DescricaoInconsistencia, TipoInconsistencia, Responsavel, DataConclusao, Data_Sincronizacao FROM dbo.[DESVIO DE QUALIDADE] WHERE Project_ID_ACC = @projectId";
    private const string Filtro = "(@disciplina IS NULL OR Disciplina = @disciplina) AND (@tipo IS NULL OR TipoInconsistencia = @tipo) AND (@search IS NULL OR NumeroDocumento LIKE @search OR DescricaoInconsistencia LIKE @search)";

    public static readonly string Query = $"""
        WITH Source AS ({Source})
        SELECT COUNT_BIG(*), COUNT_BIG(CASE WHEN {Filtro} THEN 1 END),
               COUNT_BIG(DataConclusao), COUNT_BIG(*) - COUNT_BIG(DataConclusao),
               CAST(SUM(CAST(QuantidadeDocumentos AS BIGINT)) AS BIGINT), MAX(Data_Sincronizacao)
        FROM Source;
        WITH Source AS ({Source})
        SELECT TipoInconsistencia, COUNT_BIG(*) FROM Source GROUP BY TipoInconsistencia ORDER BY COUNT_BIG(*) DESC, TipoInconsistencia;
        WITH Source AS ({Source})
        SELECT Disciplina, COUNT_BIG(*) FROM Source GROUP BY Disciplina ORDER BY COUNT_BIG(*) DESC, Disciplina;
        WITH Source AS ({Source})
        SELECT Responsavel, COUNT_BIG(*) FROM Source GROUP BY Responsavel ORDER BY COUNT_BIG(*) DESC, Responsavel;
        WITH Source AS ({Source})
        SELECT Id, Disciplina, NumeroDocumento, QuantidadeDocumentos, DescricaoInconsistencia, TipoInconsistencia, Responsavel, DataConclusao
        FROM Source WHERE {Filtro}
        ORDER BY Id OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
        """;

    public async Task<QualityDeviationReport> ReadAsync(string projectId, string? disciplina, string? tipo, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = await ProductionSql.AbrirAsync(configuration, cancellationToken);
        using var command = ProductionSql.Comando(Query, connection, projectId);
        ProductionSql.Texto(command, "@disciplina", 150, disciplina);
        ProductionSql.Texto(command, "@tipo", 250, tipo);
        ProductionSql.Busca(command, search);
        ProductionSql.Pagina(command, page, pageSize);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);
        long total = r.GetInt64(0), filtered = r.GetInt64(1), com = r.GetInt64(2), sem = r.GetInt64(3);
        long? soma = r.IsDBNull(4) ? null : r.GetInt64(4); var lastSync = ProductionSql.Data(r, 5);
        await r.NextResultAsync(cancellationToken);
        var porTipo = await ProductionSql.ContagensAsync(r, cancellationToken);
        var porDisc = await ProductionSql.ContagensAsync(r, cancellationToken);
        var porResp = await ProductionSql.ContagensAsync(r, cancellationToken);
        var rows = new List<QualityDeviationRow>();
        while (await r.ReadAsync(cancellationToken))
            rows.Add(new(r.GetInt64(0), ProductionSql.Str(r, 1), ProductionSql.Str(r, 2), r.IsDBNull(3) ? null : r.GetInt32(3),
                ProductionSql.Str(r, 4), ProductionSql.Str(r, 5), ProductionSql.Str(r, 6), ProductionSql.Data(r, 7)));
        return new(total, filtered, com, sem, soma, lastSync, page, pageSize, porTipo, porDisc, porResp, rows);
    }
}

// ------------------------------------------------------------------------------------- RNC ----

public sealed record RncRow(long Id, DateTime? DataRegistro, string? CodigoFormulario, string? Disciplina, string? Responsavel,
    string? DescricaoSituacao, string? ImpactoProjeto, string? TipoRnc, string? TipoAcao, string? ResponsavelAcao, string? Acao,
    string? Status, DateTime? PrazoConclusao, DateTime? DataConclusao, int? DiasEmAberto);
public sealed record RncReport(long TotalRows, long FilteredRows, DateTime? LastSync, int Page, int PageSize,
    IReadOnlyList<ContagemPorValor> PorStatus, IReadOnlyList<ContagemPorValor> PorTipoRnc, IReadOnlyList<ContagemPorValor> PorTipoAcao,
    IReadOnlyList<ContagemPorValor> PorDisciplina, IReadOnlyList<RncRow> Rows);

public interface IRncReportRepository
{
    Task<RncReport> ReadAsync(string projectId, string? status, string? disciplina, string? search, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// Detalhe de RNC sobre [dbo].[NCR NONCONFORMITY REPORT] — mesma fonte de /api/reports/quality (que só
/// expõe grupos Status×Disciplina e continua intacto). Contagem de LINHAS do snapshot, sem deduplicação
/// por formulário (mesma regra do QualityReportRepository). DiasEmAberto é o valor da fonte, nunca recalculado.
/// </summary>
public sealed class RncReportRepository(IConfiguration configuration) : IRncReportRepository
{
    private const string Source = "SELECT Id, DataRegistro, CodigoFormulario, Disciplina, Responsavel, DescricaoSituacao, ImpactoProjeto, TipoRNC, TipoAcao, ResponsavelAcao, Acao, Status, PrazoConclusao, DataConclusao, DiasEmAberto, Data_Sincronizacao FROM dbo.[NCR NONCONFORMITY REPORT] WHERE Project_ID_ACC = @projectId";
    private const string Filtro = "(@status IS NULL OR Status = @status) AND (@disciplina IS NULL OR Disciplina = @disciplina) AND (@search IS NULL OR CodigoFormulario LIKE @search OR DescricaoSituacao LIKE @search)";

    public static readonly string Query = $"""
        WITH Source AS ({Source})
        SELECT COUNT_BIG(*), COUNT_BIG(CASE WHEN {Filtro} THEN 1 END), MAX(Data_Sincronizacao) FROM Source;
        WITH Source AS ({Source})
        SELECT Status, COUNT_BIG(*) FROM Source GROUP BY Status ORDER BY COUNT_BIG(*) DESC, Status;
        WITH Source AS ({Source})
        SELECT TipoRNC, COUNT_BIG(*) FROM Source GROUP BY TipoRNC ORDER BY COUNT_BIG(*) DESC, TipoRNC;
        WITH Source AS ({Source})
        SELECT TipoAcao, COUNT_BIG(*) FROM Source GROUP BY TipoAcao ORDER BY COUNT_BIG(*) DESC, TipoAcao;
        WITH Source AS ({Source})
        SELECT Disciplina, COUNT_BIG(*) FROM Source GROUP BY Disciplina ORDER BY COUNT_BIG(*) DESC, Disciplina;
        WITH Source AS ({Source})
        SELECT Id, DataRegistro, CodigoFormulario, Disciplina, Responsavel, DescricaoSituacao, ImpactoProjeto, TipoRNC, TipoAcao,
               ResponsavelAcao, Acao, Status, PrazoConclusao, DataConclusao, DiasEmAberto
        FROM Source WHERE {Filtro}
        ORDER BY Id OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
        """;

    public async Task<RncReport> ReadAsync(string projectId, string? status, string? disciplina, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = await ProductionSql.AbrirAsync(configuration, cancellationToken);
        using var command = ProductionSql.Comando(Query, connection, projectId);
        ProductionSql.Texto(command, "@status", 100, status);
        ProductionSql.Texto(command, "@disciplina", 150, disciplina);
        ProductionSql.Busca(command, search);
        ProductionSql.Pagina(command, page, pageSize);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);
        long total = r.GetInt64(0), filtered = r.GetInt64(1); var lastSync = ProductionSql.Data(r, 2);
        await r.NextResultAsync(cancellationToken);
        var porStatus = await ProductionSql.ContagensAsync(r, cancellationToken);
        var porTipo = await ProductionSql.ContagensAsync(r, cancellationToken);
        var porAcao = await ProductionSql.ContagensAsync(r, cancellationToken);
        var porDisc = await ProductionSql.ContagensAsync(r, cancellationToken);
        var rows = new List<RncRow>();
        while (await r.ReadAsync(cancellationToken))
            rows.Add(new(r.GetInt64(0), ProductionSql.Data(r, 1), ProductionSql.Str(r, 2), ProductionSql.Str(r, 3), ProductionSql.Str(r, 4),
                ProductionSql.Str(r, 5), ProductionSql.Str(r, 6), ProductionSql.Str(r, 7), ProductionSql.Str(r, 8), ProductionSql.Str(r, 9),
                ProductionSql.Str(r, 10), ProductionSql.Str(r, 11), ProductionSql.Data(r, 12), ProductionSql.Data(r, 13), r.IsDBNull(14) ? null : r.GetInt32(14)));
        return new(total, filtered, lastSync, page, pageSize, porStatus, porTipo, porAcao, porDisc, rows);
    }
}

// --------------------------------------------------------------------- CONTROLE DOCUMENTAL ----

public sealed record DocumentoPorDisciplina(string? Disciplina, long Total, long ComEmissao, long ComTransmittal);
public sealed record DocumentControlReport(long TotalDocumentos, long ComEmissao, long SemEmissao, long ComTransmittal, long Disciplinas,
    DateTime? LastSync, IReadOnlyList<ContagemPorValor> PorStatusInFlow, IReadOnlyList<ContagemPorValor> PorDocumentStatus,
    IReadOnlyList<ContagemPorValor> PorIssuedPurpose, IReadOnlyList<ContagemPorValor> PorRevisao, IReadOnlyList<DocumentoPorDisciplina> PorDisciplina);

public interface IDocumentControlReportRepository
{
    Task<DocumentControlReport> ReadAsync(string projectId, CancellationToken cancellationToken);
}

/// <summary>
/// Visão gerencial documental sobre a LDP atual do projeto (PmoLdpSnapshot.Tabela — mesma fonte canônica
/// dos relatórios). Só contagens diretas: "com emissão" = Emission_Date preenchida; "com transmittal" =
/// Transmittal_Number não vazio. Nenhuma regra de atraso/aderência é aplicada aqui.
/// </summary>
public sealed class DocumentControlReportRepository(IConfiguration configuration) : IDocumentControlReportRepository
{
    private static readonly string Ldp = APS.WebApp.Services.Planning.PmoLdpSnapshot.Tabela;
    private const string Plano = APS.WebApp.Services.Planning.PmoLdpSnapshot.OpcaoPlano;
    private const string ComTransmittal = "LTRIM(RTRIM(ISNULL(Transmittal_Number, ''))) <> ''";
    private static readonly string ApenasDocumentos = APS.WebApp.Services.Planning.LdpItemRules.SqlIsDocument("LdpAtual");

    public static readonly string Query = $"""
        SELECT COUNT_BIG(*), COUNT_BIG(Emission_Date), COUNT_BIG(*) - COUNT_BIG(Emission_Date),
               COUNT_BIG(CASE WHEN {ComTransmittal} THEN 1 END), COUNT_BIG(DISTINCT Discipline), MAX(Data_Sincronizacao)
        FROM {Ldp} WHERE {ApenasDocumentos} {Plano};
        SELECT Status_In_Flow, COUNT_BIG(*) FROM {Ldp} WHERE {ApenasDocumentos} GROUP BY Status_In_Flow ORDER BY COUNT_BIG(*) DESC, Status_In_Flow {Plano};
        SELECT Document_Status, COUNT_BIG(*) FROM {Ldp} WHERE {ApenasDocumentos} GROUP BY Document_Status ORDER BY COUNT_BIG(*) DESC, Document_Status {Plano};
        SELECT Issued_Purpose, COUNT_BIG(*) FROM {Ldp} WHERE {ApenasDocumentos} GROUP BY Issued_Purpose ORDER BY COUNT_BIG(*) DESC, Issued_Purpose {Plano};
        SELECT Revision, COUNT_BIG(*) FROM {Ldp} WHERE {ApenasDocumentos} GROUP BY Revision ORDER BY COUNT_BIG(*) DESC, Revision {Plano};
        SELECT Discipline, COUNT_BIG(*), COUNT_BIG(Emission_Date), COUNT_BIG(CASE WHEN {ComTransmittal} THEN 1 END)
        FROM {Ldp} WHERE {ApenasDocumentos} GROUP BY Discipline ORDER BY COUNT_BIG(*) DESC, Discipline {Plano};
        """;

    public async Task<DocumentControlReport> ReadAsync(string projectId, CancellationToken cancellationToken)
    {
        await using var connection = await ProductionSql.AbrirAsync(configuration, cancellationToken);
        using var command = ProductionSql.Comando(Query, connection, projectId);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);
        long total = r.GetInt64(0), comEmissao = r.GetInt64(1), semEmissao = r.GetInt64(2), comTransmittal = r.GetInt64(3), disciplinas = r.GetInt64(4);
        var lastSync = ProductionSql.Data(r, 5);
        await r.NextResultAsync(cancellationToken);
        var porFlow = await ProductionSql.ContagensAsync(r, cancellationToken);
        var porStatus = await ProductionSql.ContagensAsync(r, cancellationToken);
        var porPurpose = await ProductionSql.ContagensAsync(r, cancellationToken);
        var porRev = await ProductionSql.ContagensAsync(r, cancellationToken);
        var porDisc = new List<DocumentoPorDisciplina>();
        while (await r.ReadAsync(cancellationToken))
            porDisc.Add(new(ProductionSql.Str(r, 0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3)));
        return new(total, comEmissao, semEmissao, comTransmittal, disciplinas, lastSync, porFlow, porStatus, porPurpose, porRev, porDisc);
    }
}

// -------------------------------------------------------------- NIVELAMENTO PROGRAMAÇÃO ----

public sealed record CargaProgramada(string? Chave, long Atividades, long ComHh, decimal? HhPrevisto);
public sealed record ScheduleLevelingReport(long Atividades, long ComHh, long SemHh, decimal? HhPrevistoConhecido, long Recursos, long SemRecurso,
    DateTime? LastSync, IReadOnlyList<CargaProgramada> PorRecurso, IReadOnlyList<CargaProgramada> PorDisciplina);

public interface IScheduleLevelingReportRepository
{
    Task<ScheduleLevelingReport> ReadAsync(string projectId, CancellationToken cancellationToken);
}

/// <summary>
/// Carga programada (tb_WeeklyEngineeringSchedule, Tipo_Registro = 'DADO') por Recurso e por Disciplina:
/// quantidade de atividades e soma de Weekly_HH_Previsto CONHECIDO (HH vazio nunca vira zero; a
/// quantidade sem HH é informada). Capacidade/HH disponível/produtividade NÃO existem na fonte →
/// REGRA_BI_A_CONFIRMAR (nada é estimado aqui).
/// </summary>
public sealed class ScheduleLevelingReportRepository(IConfiguration configuration) : IScheduleLevelingReportRepository
{
    private const string Source = "SELECT Recurso, Disciplina, Weekly_HH_Previsto, Data_Sincronizacao FROM dbo.tb_WeeklyEngineeringSchedule WHERE Project_ID_ACC = @projectId AND Tipo_Registro = N'DADO'";

    public static readonly string Query = $"""
        WITH Source AS ({Source})
        SELECT COUNT_BIG(*), COUNT_BIG(Weekly_HH_Previsto), COUNT_BIG(*) - COUNT_BIG(Weekly_HH_Previsto), SUM(Weekly_HH_Previsto),
               COUNT_BIG(DISTINCT NULLIF(LTRIM(RTRIM(Recurso)), '')), COUNT_BIG(CASE WHEN LTRIM(RTRIM(ISNULL(Recurso, ''))) = '' THEN 1 END),
               MAX(Data_Sincronizacao)
        FROM Source;
        WITH Source AS ({Source})
        SELECT Recurso, COUNT_BIG(*), COUNT_BIG(Weekly_HH_Previsto), SUM(Weekly_HH_Previsto)
        FROM Source GROUP BY Recurso ORDER BY COUNT_BIG(*) DESC, Recurso;
        WITH Source AS ({Source})
        SELECT Disciplina, COUNT_BIG(*), COUNT_BIG(Weekly_HH_Previsto), SUM(Weekly_HH_Previsto)
        FROM Source GROUP BY Disciplina ORDER BY COUNT_BIG(*) DESC, Disciplina;
        """;

    public async Task<ScheduleLevelingReport> ReadAsync(string projectId, CancellationToken cancellationToken)
    {
        await using var connection = await ProductionSql.AbrirAsync(configuration, cancellationToken);
        using var command = ProductionSql.Comando(Query, connection, projectId);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);
        long atividades = r.GetInt64(0), comHh = r.GetInt64(1), semHh = r.GetInt64(2);
        decimal? hh = r.IsDBNull(3) ? null : r.GetDecimal(3);
        long recursos = r.GetInt64(4), semRecurso = r.GetInt64(5); var lastSync = ProductionSql.Data(r, 6);
        await r.NextResultAsync(cancellationToken);
        var porRecurso = await CargasAsync(r, cancellationToken);
        await r.NextResultAsync(cancellationToken);
        var porDisc = await CargasAsync(r, cancellationToken);
        return new(atividades, comHh, semHh, hh, recursos, semRecurso, lastSync, porRecurso, porDisc);
    }

    private static async Task<IReadOnlyList<CargaProgramada>> CargasAsync(SqlDataReader r, CancellationToken ct)
    {
        var lista = new List<CargaProgramada>();
        while (await r.ReadAsync(ct))
            lista.Add(new(ProductionSql.Str(r, 0), r.GetInt64(1), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetDecimal(3)));
        return lista;
    }
}

// ----------------------------------------------------------------- CONTROLE PROGRAMAÇÃO ----

public sealed record StatusPorDisciplina(string? Disciplina, string? Status, long Quantidade);
public sealed record ScheduleControlReport(long Atividades, long ComBaseline, long ComReprogramacao, long ReprogramadasAposBaseline,
    DateTime? LastSync, IReadOnlyList<ContagemPorValor> PorStatus, IReadOnlyList<ContagemPorValor> PorFase,
    IReadOnlyList<StatusPorDisciplina> StatusPorDisciplina);

public interface IScheduleControlReportRepository
{
    Task<ScheduleControlReport> ReadAsync(string projectId, CancellationToken cancellationToken);
}

/// <summary>
/// Controle da programação semanal (tb_WeeklyEngineeringSchedule, 'DADO'): distribuição por
/// Weekly_Progress_Status (texto bruto — a fonte mistura PT/EN e nenhuma equivalência é presumida),
/// por Weekly_Phase e matriz Disciplina×Status. "Reprogramadas após baseline" = Weekly_Replanned_Date &gt;
/// Weekly_Baseline_Date (comparação direta de datas da fonte). Aderência/atraso relativo a hoje e
/// Perc_Previsto_Semana × Perc_Realizado (texto na fonte) → REGRA_BI_A_CONFIRMAR.
/// </summary>
public sealed class ScheduleControlReportRepository(IConfiguration configuration) : IScheduleControlReportRepository
{
    private const string Source = "SELECT Disciplina, Weekly_Phase, Weekly_Progress_Status, Weekly_Baseline_Date, Weekly_Replanned_Date, Data_Sincronizacao FROM dbo.tb_WeeklyEngineeringSchedule WHERE Project_ID_ACC = @projectId AND Tipo_Registro = N'DADO'";

    public static readonly string Query = $"""
        WITH Source AS ({Source})
        SELECT COUNT_BIG(*), COUNT_BIG(Weekly_Baseline_Date), COUNT_BIG(Weekly_Replanned_Date),
               COUNT_BIG(CASE WHEN Weekly_Replanned_Date > Weekly_Baseline_Date THEN 1 END), MAX(Data_Sincronizacao)
        FROM Source;
        WITH Source AS ({Source})
        SELECT Weekly_Progress_Status, COUNT_BIG(*) FROM Source GROUP BY Weekly_Progress_Status ORDER BY COUNT_BIG(*) DESC, Weekly_Progress_Status;
        WITH Source AS ({Source})
        SELECT Weekly_Phase, COUNT_BIG(*) FROM Source GROUP BY Weekly_Phase ORDER BY COUNT_BIG(*) DESC, Weekly_Phase;
        WITH Source AS ({Source})
        SELECT Disciplina, Weekly_Progress_Status, COUNT_BIG(*) FROM Source
        GROUP BY Disciplina, Weekly_Progress_Status ORDER BY Disciplina, Weekly_Progress_Status;
        """;

    public async Task<ScheduleControlReport> ReadAsync(string projectId, CancellationToken cancellationToken)
    {
        await using var connection = await ProductionSql.AbrirAsync(configuration, cancellationToken);
        using var command = ProductionSql.Comando(Query, connection, projectId);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);
        long atividades = r.GetInt64(0), comBaseline = r.GetInt64(1), comRepro = r.GetInt64(2), reproApos = r.GetInt64(3);
        var lastSync = ProductionSql.Data(r, 4);
        await r.NextResultAsync(cancellationToken);
        var porStatus = await ProductionSql.ContagensAsync(r, cancellationToken);
        var porFase = await ProductionSql.ContagensAsync(r, cancellationToken);
        var matriz = new List<StatusPorDisciplina>();
        while (await r.ReadAsync(cancellationToken))
            matriz.Add(new(ProductionSql.Str(r, 0), ProductionSql.Str(r, 1), r.GetInt64(2)));
        return new(atividades, comBaseline, comRepro, reproApos, lastSync, porStatus, porFase, matriz);
    }
}
