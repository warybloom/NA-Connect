namespace APS.WebApp.Services.Reports.Dashboards;

// =============================================================================================
// MOTOR DOS DASHBOARDS DO RELATÓRIO DE PRODUÇÃO (servidor).
// Cada dashboard carrega as linhas do PROJETO com UM SELECT parametrizado (só as colunas necessárias) e
// este motor calcula, sobre esse conjunto, KPIs, gráficos e tabela — todos com os MESMOS filtros.
//
// CROSS-FILTER (regra única, documentada no RELATORIO_PRODUCAO_PROGRESSO.md):
//   • filtros = dimensão → conjunto de valores (OR dentro da dimensão, AND entre dimensões);
//   • KPIs e tabela usam TODOS os filtros ativos;
//   • cada gráfico usa todos os filtros EXCETO os da sua própria dimensão — o visual de origem continua
//     mostrando as demais categorias (com a selecionada destacada) e permite trocar a seleção, como o
//     cross-filter do Power BI.
// Nada aqui acessa banco: é puro e testado em Tests/NA_Connect.Planning.Tests/ProductionDashboardEngineTests.cs.
// =============================================================================================

public sealed class DashboardRow(IReadOnlyDictionary<string, object?> values)
{
    public object? this[string column] => values.TryGetValue(column, out var v) ? v : null;
    public decimal? Decimal(string column) => this[column] is { } v && v is not DBNull ? Convert.ToDecimal(v) : null;
    public DateTime? Date(string column) => this[column] is DateTime d ? d : null;
}

public sealed record DashboardDimension(string Key, string Label, Func<DashboardRow, (string Key, string Label)> Value);

public sealed record DashboardKpi(string Key, string Label, string Detail, Func<IReadOnlyList<DashboardRow>, decimal?> Compute,
    string Format = DashboardFormats.Numero, string? FilterDimension = null, string? FilterValue = null);

/// <param name="OrdenarPorChave">true = eixo em ordem da chave (ex.: semanas cronológicas); false = maior valor primeiro.</param>
public sealed record DashboardChart(string Key, string Title, string Kind, string Dimension, string MeasureLabel = "Quantidade",
    Func<IEnumerable<DashboardRow>, decimal?>? Measure = null, string MeasureFormat = DashboardFormats.Numero, bool OrdenarPorChave = false);

public sealed record DashboardColumn(string Key, string Label, string Type, Func<DashboardRow, object?> Value);

/// <summary>
/// Célula de uma MATRIZ compacta: conta as linhas cujo valor na dimensão é Value (clique = filtro Dimension=Value).
/// Cada célula aponta para UMA dimensão/valor — a contagem e o filtro são sempre o mesmo critério.
/// </summary>
public sealed record DashboardMatrixCell(string Row, string Column, string Dimension, string Value);

/// <summary>Matriz compacta opcional (ex.: status por gate 30/60/90 da LDP Tipo 2). Linhas × colunas de células.</summary>
public sealed record DashboardMatrix(string Key, string Title, IReadOnlyList<DashboardOption> Rows, IReadOnlyList<DashboardOption> Columns,
    IReadOnlyList<DashboardMatrixCell> Cells);

public static class DashboardFormats
{
    public const string Numero = "numero", Percentual = "percentual", Horas = "horas", Texto = "texto", Data = "data";
    /// <summary>Célula de gate da LDP Tipo 2 (objeto { rotulo, ativo, classe, documentStatus, situation }).</summary>
    public const string Gate = "gate";
}

public static class DashboardKinds
{
    public const string Barra = "bar", Coluna = "column", Rosca = "donut";
}

public sealed class DashboardDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Source { get; init; }
    /// <summary>SELECT único, filtrado por Project_ID_ACC = @projectId.</summary>
    public required string Sql { get; init; }
    public string? LastSyncColumn { get; init; } = "Data_Sincronizacao";
    public required IReadOnlyList<DashboardDimension> Dimensions { get; init; }
    public required IReadOnlyList<DashboardKpi> Kpis { get; init; }
    public required IReadOnlyList<DashboardChart> Charts { get; init; }
    public required IReadOnlyList<DashboardColumn> Table { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
    /// <summary>
    /// SELECT complementar opcional (também filtrado por Project_ID_ACC = @projectId) e a função que o incorpora às
    /// linhas principais ANTES da avaliação (ex.: gates da LDP Tipo 2). Continua sendo leitura única por consulta.
    /// </summary>
    public string? ComplementSql { get; init; }
    public Action<IReadOnlyList<Dictionary<string, object?>>, IReadOnlyList<Dictionary<string, object?>>>? Merge { get; init; }
    /// <summary>Dimensão aplicada ao clicar numa linha da tabela (ex.: documento). Null = linhas não clicáveis.</summary>
    public string? TableRowFilter { get; init; }
    /// <summary>Matrizes compactas opcionais (a maioria dos dashboards não tem nenhuma).</summary>
    public IReadOnlyList<DashboardMatrix> Matrices { get; init; } = Array.Empty<DashboardMatrix>();
    /// <summary>
    /// Especialização pelo PRÓPRIO dataset (ex.: LDP Tipo 1 × Tipo 2): devolve a definição efetiva a avaliar. Deve
    /// manter um subconjunto das dimensões da definição base (filtros de dimensões ausentes são descartados).
    /// </summary>
    public Func<IReadOnlyList<DashboardRow>, DashboardDefinition>? Especializar { get; init; }
    /// <summary>Metadados mínimos para o frontend (ex.: tipoLdp) — calculados do dataset carregado.</summary>
    public Func<IReadOnlyList<DashboardRow>, IReadOnlyDictionary<string, object?>>? Metadata { get; init; }
    /// <summary>Filtro inicial sugerido (ex.: semana atual); o frontend aplica só na primeira carga da tela.</summary>
    public Func<IReadOnlyList<DashboardRow>, DateTime, IReadOnlyDictionary<string, string[]>>? DefaultFilters { get; init; }
    public DashboardDimension Dimensao(string key) => Dimensions.First(d => d.Key == key);
}

// ------------------------------------------------------------------------------- resposta ----
public sealed record DashboardOption(string Key, string Label);
public sealed record DashboardActiveFilter(string Dimension, string DimensionLabel, IReadOnlyList<DashboardOption> Values);
public sealed record DashboardKpiResult(string Key, string Label, string Detail, decimal? Value, string Format,
    string? FilterDimension, string? FilterValue, bool Selected);
public sealed record DashboardPoint(string Key, string Label, decimal Value, bool Selected);
public sealed record DashboardChartResult(string Key, string Title, string Kind, string Dimension, string MeasureLabel, string MeasureFormat,
    IReadOnlyList<DashboardPoint> Points);
public sealed record DashboardTableColumnInfo(string Key, string Label, string Type);
/// <param name="RowFilterDimension">Dimensão aplicada ao clicar numa linha; cada linha traz "__rowKey" e "__selected".</param>
public sealed record DashboardTableResult(IReadOnlyList<DashboardTableColumnInfo> Columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    long Total, int Page, int PageSize, string? RowFilterDimension = null);
public sealed record DashboardMatrixCellResult(string Row, string Column, decimal Value, string FilterDimension, string FilterValue, bool Selected);
public sealed record DashboardMatrixResult(string Key, string Title, IReadOnlyList<DashboardOption> Rows, IReadOnlyList<DashboardOption> Columns,
    IReadOnlyList<DashboardMatrixCellResult> Cells);
public sealed record DashboardResult(string Id, string Title, string Source, long TotalRows, long FilteredRows, DateTime? LastSync,
    IReadOnlyList<DashboardOption> Dimensions, IReadOnlyList<DashboardActiveFilter> ActiveFilters,
    IReadOnlyList<DashboardKpiResult> Kpis, IReadOnlyList<DashboardChartResult> Charts, DashboardTableResult Table, IReadOnlyList<string> Notes,
    IReadOnlyDictionary<string, string[]>? DefaultFilters = null,
    IReadOnlyList<DashboardMatrixResult>? Matrices = null, IReadOnlyDictionary<string, object?>? Meta = null);

public static class DashboardEngine
{
    public const int MaxValoresPorDimensao = 50;

    /// <summary>Valida filtros contra as dimensões do dashboard (dimensão desconhecida = entrada inválida).</summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> ValidarFiltros(DashboardDefinition def, IReadOnlyDictionary<string, IReadOnlyList<string>> brutos)
    {
        var validos = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var (dim, valores) in brutos)
        {
            if (def.Dimensions.All(d => d.Key != dim)) throw new ArgumentException($"Dimensão de filtro desconhecida: {dim}");
            var set = valores.Where(v => !string.IsNullOrEmpty(v)).Select(v => v.Length > 200 ? throw new ArgumentException("Valor de filtro longo demais.") : v)
                .ToHashSet(StringComparer.Ordinal);
            if (set.Count > MaxValoresPorDimensao) throw new ArgumentException("Valores de filtro demais.");
            if (set.Count > 0) validos[dim] = set;
        }
        return validos;
    }

    /// <summary>Data de referência "hoje" no fuso de operação (mesma regra de PmoExecutiveData).</summary>
    public static DateTime HojeOperacao() => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "E. South America Standard Time").Date;

    public static DashboardResult Avaliar(DashboardDefinition def, IReadOnlyList<DashboardRow> linhas,
        IReadOnlyDictionary<string, IReadOnlySet<string>> filtros, int page, int pageSize, DateTime? hoje = null)
    {
        // Definição efetiva para ESTE dataset (ex.: LDP Tipo 1 não tem nada de gates). Filtros de dimensões que a
        // definição efetiva não possui não se aplicam a ela.
        var meta = def.Metadata?.Invoke(linhas);
        if (def.Especializar is not null)
        {
            def = def.Especializar(linhas);
            var efetivas = def.Dimensions.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
            filtros = filtros.Where(f => efetivas.Contains(f.Key)).ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        }

        // Valor normalizado de cada dimensão, calculado uma vez por linha.
        var valores = linhas.Select(r => def.Dimensions.ToDictionary(d => d.Key, d => d.Value(r), StringComparer.Ordinal)).ToList();

        bool Passa(int i, string? excetoDimensao) => filtros.All(f => f.Key == excetoDimensao || f.Value.Contains(valores[i][f.Key].Key));

        var indicesFiltrados = Enumerable.Range(0, linhas.Count).Where(i => Passa(i, null)).ToList();
        var filtradas = indicesFiltrados.Select(i => linhas[i]).ToList();

        var kpis = def.Kpis.Select(k => new DashboardKpiResult(k.Key, k.Label, k.Detail, k.Compute(filtradas), k.Format,
            k.FilterDimension, k.FilterValue,
            k.FilterDimension != null && filtros.TryGetValue(k.FilterDimension, out var sel) && sel.Contains(k.FilterValue!))).ToList();

        var graficos = def.Charts.Select(c =>
        {
            filtros.TryGetValue(c.Dimension, out var selecionados);
            var pontos = Enumerable.Range(0, linhas.Count).Where(i => Passa(i, c.Dimension))
                .GroupBy(i => valores[i][c.Dimension].Key)
                .Select(g =>
                {
                    var grupo = g.Select(i => linhas[i]).ToList();
                    // Medida de soma sem nenhum valor conhecido no grupo → o grupo NÃO entra (vazio nunca vira zero).
                    decimal? valor = c.Measure is null ? grupo.Count : c.Measure(grupo);
                    return valor is null ? null : new DashboardPoint(g.Key, valores[g.First()][c.Dimension].Label, valor.Value, selecionados?.Contains(g.Key) == true);
                })
                .OfType<DashboardPoint>();
            pontos = c.OrdenarPorChave
                ? pontos.OrderBy(p => p.Key, StringComparer.Ordinal)
                : pontos.OrderByDescending(p => p.Value).ThenBy(p => p.Label, StringComparer.CurrentCulture);
            return new DashboardChartResult(c.Key, c.Title, c.Kind, c.Dimension, c.MeasureLabel, c.MeasureFormat, pontos.ToList());
        }).ToList();

        // Rótulos dos filtros ativos (a partir de TODAS as linhas do projeto).
        var ativos = filtros.Select(f =>
        {
            var dim = def.Dimensao(f.Key);
            var labels = valores.Select(v => v[f.Key]).Where(v => f.Value.Contains(v.Key)).GroupBy(v => v.Key).ToDictionary(g => g.Key, g => g.First().Label);
            return new DashboardActiveFilter(dim.Key, dim.Label,
                f.Value.Select(v => new DashboardOption(v, labels.TryGetValue(v, out var l) ? l : v)).ToList());
        }).ToList();

        IReadOnlySet<string>? linhasSelecionadas = null;
        if (def.TableRowFilter != null) filtros.TryGetValue(def.TableRowFilter, out linhasSelecionadas);
        var tabela = new DashboardTableResult(
            def.Table.Select(c => new DashboardTableColumnInfo(c.Key, c.Label, c.Type)).ToList(),
            indicesFiltrados.Skip((page - 1) * pageSize).Take(pageSize).Select(i =>
            {
                var linha = def.Table.ToDictionary(c => c.Key, c => Limpar(c.Value(linhas[i])));
                if (def.TableRowFilter != null)
                {
                    var chave = valores[i][def.TableRowFilter].Key;
                    linha["__rowKey"] = chave;
                    linha["__selected"] = linhasSelecionadas?.Contains(chave) == true;
                }
                return (IReadOnlyDictionary<string, object?>)linha;
            }).ToList(),
            filtradas.Count, page, pageSize, def.TableRowFilter);

        DateTime? lastSync = def.LastSyncColumn is null ? null : linhas.Select(r => r.Date(def.LastSyncColumn)).Where(d => d.HasValue).DefaultIfEmpty().Max();

        // Matrizes: cada célula segue a regra dos gráficos — todos os filtros EXCETO o da sua própria dimensão
        // (a célula selecionada continua mostrando as vizinhas da mesma dimensão, como uma barra de gráfico).
        var matrizes = def.Matrices.Select(m => new DashboardMatrixResult(m.Key, m.Title, m.Rows, m.Columns,
            m.Cells.Select(c =>
            {
                filtros.TryGetValue(c.Dimension, out var selecionados);
                var n = Enumerable.Range(0, linhas.Count).Count(i => Passa(i, c.Dimension) && valores[i][c.Dimension].Key == c.Value);
                return new DashboardMatrixCellResult(c.Row, c.Column, n, c.Dimension, c.Value, selecionados?.Contains(c.Value) == true);
            }).ToList())).ToList();

        return new DashboardResult(def.Id, def.Title, def.Source, linhas.Count, filtradas.Count, lastSync,
            def.Dimensions.Select(d => new DashboardOption(d.Key, d.Label)).ToList(), ativos, kpis, graficos, tabela, def.Notes,
            def.DefaultFilters?.Invoke(linhas, hoje ?? HojeOperacao()), matrizes.Count > 0 ? matrizes : null, meta);
    }

    private static object? Limpar(object? v) => v is DBNull ? null : v is string s ? s.Trim() : v;

    // ------------------------------------------------------------------- blocos de medida ----
    public static Func<IReadOnlyList<DashboardRow>, decimal?> Contagem() => rs => rs.Count;

    public static Func<IReadOnlyList<DashboardRow>, decimal?> ContagemOnde(DashboardDimension d, string chave) =>
        rs => rs.Count(r => d.Value(r).Key == chave);

    /// <summary>Soma só dos valores conhecidos; null quando nenhum valor é conhecido (vazio nunca vira zero).</summary>
    public static Func<IReadOnlyList<DashboardRow>, decimal?> SomaConhecida(string coluna) => rs =>
    {
        decimal? soma = null;
        foreach (var r in rs)
            if (r.Decimal(coluna) is decimal v) soma = (soma ?? 0m) + v;
        return soma;
    };

    /// <summary>Soma dos valores conhecidos do grupo; null quando o grupo não tem nenhum valor conhecido.</summary>
    public static Func<IEnumerable<DashboardRow>, decimal?> SomaMedida(string coluna) =>
        rs => SomaConhecida(coluna)(rs as IReadOnlyList<DashboardRow> ?? rs.ToList());

    public static Func<IReadOnlyList<DashboardRow>, decimal?> DistintosNaoVazios(DashboardDimension d) =>
        rs => rs.Select(r => d.Value(r).Key).Where(k => k != ReportStatusNormalizer.NullKey).Distinct().Count();

    /// <summary>Percentual (0–100) de linhas com a chave, sobre o total do conjunto; null se o conjunto é vazio.</summary>
    public static Func<IReadOnlyList<DashboardRow>, decimal?> PercentualOnde(DashboardDimension d, string chave) =>
        rs => rs.Count == 0 ? null : Math.Round(rs.Count(r => d.Value(r).Key == chave) * 100m / rs.Count, 1);
}
