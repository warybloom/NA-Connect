using APS.WebApp.Services.Planning;

namespace APS.WebApp.Services.Reports;

public sealed record CurvaSPonto(DateTime Data, decimal PrevistoHh, decimal RealizadoHh, decimal PrevistoCusto, decimal RealizadoCusto,
    int PrevistoDocumentos, int EmitidosDocumentos);

public sealed record CurvaSCobertura(int TotalDocumentos, int Cancelados, int Ambiguos, int SemBaseline, int SemHhPrevisto,
    int SemCustoPrevisto, decimal? HhPrevistoTotalProjeto, decimal HhCurvaPrevisto, decimal HhCurvaRealizado, decimal HhCancelados,
    decimal HhSemBaseline, decimal HhAtividades, decimal? HhSemDocumento, decimal CustoCurvaPrevisto, decimal CustoCurvaRealizado);

public sealed record CurvaSReport(string ProjectId, DateTime DataReferencia, IReadOnlyList<CurvaSPonto> Pontos, CurvaSCobertura Cobertura,
    string FontePrevisto, string FonteRealizado);

/// <summary>
/// Curva S documental sobre a MESMA fonte canônica da Gestão à Vista (documentos, Planned_HH e Planned_Cost da última carga da
/// LDP, status canônico). O Timesheet NÃO possui data de trabalho em nenhuma fonte do sistema (DataLancamento é nula
/// em 100% do ledger e do snapshot), então HH/Custo realizados NÃO podem ser posicionados no tempo. Por isso a curva "Realizada"
/// é o valor agregado por emissão: HH/custo previsto dos documentos EMITIDOS, na data de emissão (Emission_Date). O "Previsto"
/// posiciona o HH/custo previsto de cada documento não cancelado na sua data baseline (LDP Planned_Date_Baseline). Nunca há
/// distribuição arbitrária entre datas: cada documento entra uma única vez, na sua própria data.
/// </summary>
public static class PmoCurvaSRules
{
    private static DateTime? Valida(DateTime? v) => v.HasValue && v.Value.Year >= 2000 ? v.Value.Date : null;

    public static CurvaSReport Build(string projectId, DateTime referencia, IReadOnlyList<PmoDocumento> docs, decimal? plannedHhTotal)
    {
        // Curva S é documental: AT permanece no universo econômico/HH do projeto, mas não vira entrega, emissão ou atraso.
        var documentos = docs.Where(d => d.IsDocument).ToArray();
        var multiplicity = documentos.Where(d => !string.IsNullOrWhiteSpace(d.Codigo)).GroupBy(d => PmoExecutiveRules.Chave(d.Codigo))
            .ToDictionary(g => g.Key, g => g.Count());
        var events = new SortedDictionary<DateTime, (decimal PrevHh, decimal RealHh, decimal PrevCusto, decimal RealCusto, int PrevDocs, int EmDocs)>();
        void Add(DateTime date, decimal prevHh = 0, decimal realHh = 0, decimal prevCusto = 0, decimal realCusto = 0, int prevDocs = 0, int emDocs = 0)
        {
            events.TryGetValue(date, out var e);
            events[date] = (e.PrevHh + prevHh, e.RealHh + realHh, e.PrevCusto + prevCusto, e.RealCusto + realCusto, e.PrevDocs + prevDocs, e.EmDocs + emDocs);
        }
        int cancelados = 0, ambiguos = 0, semBaseline = 0, semHh = 0, semCusto = 0;
        decimal hhCancelados = 0, hhSemBaseline = 0;
        // Reconciliação de HH continua sobre TODAS as linhas (inclusive AT), pois atividade tem esforço/custo real.
        decimal hhAtividades = docs.Where(d => d.IsActivity && d.HHPrevisto.HasValue).Sum(d => d.HHPrevisto!.Value);
        decimal hhTodosUnicos = docs.Where(d => d.HHPrevisto.HasValue).Sum(d => d.HHPrevisto!.Value);
        foreach (var d in documentos)
        {
            var key = PmoExecutiveRules.Chave(d.Codigo);
            if (!string.IsNullOrWhiteSpace(d.Codigo) && multiplicity[key] > 1) ambiguos++;
            // HH/custo previsto OFICIAIS: Planned_HH / Planned_Cost da própria linha da última carga da LDP (null = ausente, nunca zero).
            var hh = d.HHPrevisto; var custo = d.CustoPrevisto;
            if (d.StatusDocumento == "CANCELADO") { cancelados++; if (hh.HasValue) hhCancelados += hh.Value; continue; }
            if (!hh.HasValue) semHh++;
            if (!custo.HasValue) semCusto++;
            var baseline = Valida(d.DataBaseline);
            if (baseline.HasValue) Add(baseline.Value, hh ?? 0, 0, custo ?? 0, 0, 1, 0);
            else { semBaseline++; if (hh.HasValue) hhSemBaseline += hh.Value; }
            if (d.StatusDocumento == "EMITIDO" && Valida(d.DataEmissao) is { } emissao)
                Add(emissao, 0, hh ?? 0, 0, custo ?? 0, 0, 1);
        }
        var pontos = new List<CurvaSPonto>(); decimal ph = 0, rh = 0, pc = 0, rc = 0; int pd = 0, ed = 0;
        foreach (var (date, e) in events)
        {
            ph += e.PrevHh; rh += e.RealHh; pc += e.PrevCusto; rc += e.RealCusto; pd += e.PrevDocs; ed += e.EmDocs;
            pontos.Add(new(date, ph, rh, pc, rc, pd, ed));
        }
        decimal? semDocumento = plannedHhTotal.HasValue ? plannedHhTotal.Value - hhTodosUnicos : null;
        return new(projectId, referencia.Date, pontos,
            new(documentos.Length, cancelados, ambiguos, semBaseline, semHh, semCusto, plannedHhTotal, ph, rh, hhCancelados, hhSemBaseline, hhAtividades, semDocumento, pc, rc),
            "HH/custo previsto dos documentos não cancelados na data baseline (LDP Planned_Date_Baseline); HH (Planned_HH) e custo (Planned_Cost) da LDP",
            "Valor agregado por emissão: HH/custo previsto dos documentos EMITIDOS na Emission_Date. HH/Custo realizados do Timesheet não têm data de trabalho na fonte");
    }
}
