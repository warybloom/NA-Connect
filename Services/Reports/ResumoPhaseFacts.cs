namespace APS.WebApp.Services.Reports;

/// <summary>Read-only phase projection. Null means no traceable source, never an allocation of a consolidated value.</summary>
public sealed record ResumoPhaseMetric(int Gate, int Total, int? Emitidos, int? Cancelados, int? AEmitir,
    int? Atrasados, int? NoPrazo, int? SemPrazo, decimal? HhPrevisto, int SemHhPrevisto)
{
    public int Conflitos { get; init; }
    public decimal? HhRealizado => null;
    public decimal? ConsumoHh => null;
    public decimal? SaldoHh => null;
    public decimal? CustoPrevisto => null;
    public decimal? CustoRealizado => null;
    public decimal? ConsumoCusto => null;
}

public sealed record ResumoPhaseDocument(string RowId, string? Codigo, string? Disciplina,
    string? DisciplinaNorma, string? Doc3, string? FonteDisciplina, IReadOnlyList<ResumoPhaseMetric> Fases);

public sealed record ResumoPhaseFacts(IReadOnlyList<ResumoPhaseMetric> Fases,
    IReadOnlyList<ResumoPhaseDocument> Documentos, IReadOnlyList<string> Pendencias);

public static class ResumoPhaseRules
{
    public static ResumoPhaseFacts Read(int tipo, DateTime referencia, IReadOnlyList<PmoDocumento> docs)
    {
        if (tipo != 2) return new([], [], []);
        // A document participates once per gate. Conflicting duplicate source rows are exposed as unavailable.
        var documentos = docs.GroupBy(d => string.IsNullOrWhiteSpace(d.Codigo)
            ? d.RowId : PmoExecutiveRules.Chave(d.Codigo), StringComparer.Ordinal).Select(group =>
        {
            var doc = group.First();
            var fases = new[] { 30, 60, 90 }.Select(gate =>
            {
                var gs = group.SelectMany(d => d.Gates).Where(g => g.Ativo && g.Percentual == gate).Distinct().ToArray();
                if (gs.Length == 0) return new ResumoPhaseMetric(gate, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                var unique = gs.Length == 1;
                if (!unique) return new ResumoPhaseMetric(gate, doc.IsDocument ? 1 : 0,
                    doc.IsDocument ? null : 0, doc.IsDocument ? null : 0, doc.IsDocument ? null : 0,
                    doc.IsDocument ? null : 0, doc.IsDocument ? null : 0, doc.IsDocument ? null : 0, null, 1)
                    { Conflitos = 1 };
                var g = gs[0];
                var cancelado = unique && g.Cancelado;
                var emitido = unique && !cancelado && g.Emitido;
                static DateTime? Valid(DateTime? date) => date?.Year >= 2000 ? date.Value.Date : null;
                var date = unique ? Valid(g.Reprogramada) ?? Valid(g.Baseline) : null;
                var documental = doc.IsDocument;
                var aberto = !cancelado && !emitido;
                var atraso = aberto && date.HasValue && date.Value < referencia.Date;
                var noPrazo = aberto && date.HasValue && !atraso;
                // Work is the official effort of this gate. No percentage or document budget is used as a substitute.
                var hh = unique && g.EsforcoHoras is >= 0 ? g.EsforcoHoras : null;
                return new ResumoPhaseMetric(gate, documental ? 1 : 0, documental && emitido ? 1 : 0,
                    documental && cancelado ? 1 : 0, documental && aberto ? 1 : 0,
                    documental && atraso ? 1 : 0, documental && noPrazo ? 1 : 0,
                    documental && aberto && !date.HasValue ? 1 : 0, hh, hh.HasValue ? 0 : 1);
            }).ToArray();
            return new ResumoPhaseDocument(doc.RowId, doc.Codigo, doc.DisciplinaCanonica,
                doc.DisciplinaNorma, doc.SiglaDoc3, doc.FonteDisciplinaNorma, fases);
        }).ToArray();
        var totals = new[] { 30, 60, 90 }.Select(gate => Sum(gate,
            documentos.SelectMany(d => d.Fases).Where(f => f.Gate == gate))).ToArray();
        return new(totals, documentos,
            ["HH realizado, consumo e saldo por fase indisponíveis: Timesheet corrigido e complementos históricos não possuem vínculo explícito ao gate.",
             "Custo previsto/realizado e consumo por fase indisponíveis: não há orçamento e lançamento financeiro identificados por gate.",
             "HH previsto por fase: Work do Cronograma Tipo 2; cobertura incompleta retorna null. Totais consolidados permanecem nas fontes oficiais."]);
    }

    private static ResumoPhaseMetric Sum(int gate, IEnumerable<ResumoPhaseMetric> values)
    {
        var fs = values.ToArray();
        var missing = fs.Sum(f => f.SemHhPrevisto);
        int? StatusSum(Func<ResumoPhaseMetric, int?> field) => fs.Any(f => !field(f).HasValue) ? null : fs.Sum(f => field(f) ?? 0);
        return new ResumoPhaseMetric(gate, fs.Sum(f => f.Total), StatusSum(f => f.Emitidos), StatusSum(f => f.Cancelados),
            StatusSum(f => f.AEmitir), StatusSum(f => f.Atrasados), StatusSum(f => f.NoPrazo), StatusSum(f => f.SemPrazo),
            missing > 0 ? null : fs.Sum(f => f.HhPrevisto ?? 0), missing) { Conflitos = fs.Sum(f => f.Conflitos) };
    }
}
