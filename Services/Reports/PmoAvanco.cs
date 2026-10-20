namespace APS.WebApp.Services.Reports;

/// <summary>
/// Avanço previsto/físico de UM projeto (calculado por projeto; a consolidação multiprojeto soma numeradores e
/// denominadores — nunca mistura linhas de projetos diferentes antes de resolver cada um).
/// Percentuais = Numerador / Denominador × 100 (null quando o denominador é 0 — nunca 0% inventado).
/// </summary>
public sealed record PmoAvancoProjeto(
    int TipoLdp, DateTime DataReferencia, DateTime InicioSemanaVigente, DateTime FimSemanaVigente,
    decimal? Previsto, decimal PrevistoNumerador, decimal PrevistoDenominador,
    decimal? Fisico, decimal FisicoNumerador, decimal FisicoDenominador,
    int Documentos, int DocumentosSemDadoFisico, int DocumentosSemDadoPrevisto,
    string RegraPrevisto, string RegraFisico, string Ponderacao)
{
    /// <summary>Tipo 2: avanço previsto/físico de cada fase (30/60/90), ponderado pelo esforço. Vazio no Tipo 1.</summary>
    public IReadOnlyList<PmoAvancoFase> PorFase { get; init; } = Array.Empty<PmoAvancoFase>();
    /// <summary>Tipo 2: fases ativas sem esforço válido (fora das contas, nunca peso inventado).</summary>
    public int FasesSemEsforco { get; init; }
}

/// <summary>Avanço de UMA fase (gate) do projeto Tipo 2, com numerador/denominador para consolidar vários projetos.</summary>
public sealed record PmoAvancoFase(int Gate, decimal? Previsto, decimal PrevistoNumerador, decimal PrevistoDenominador,
    decimal? Fisico, decimal FisicoNumerador, decimal FisicoDenominador);

/// <summary>
/// HH REALIZADO do projeto = soma, sobre os documentos da LDP atual, da coluna PRONTA dbo.VW_Ldp_Unificada.Actual_HH_Total_Corrigido
/// (por documento e por disciplina do documento — mesma chave de disciplina do HH previsto). Nenhuma reconstrução da regra.
/// </summary>
public sealed record PmoHhDisciplina(string? Disciplina, decimal? Hh)
{
    /// <summary>Σ Actual_Cost_Total_Corrigido dos documentos da disciplina (null quando o custo oficial não está disponível).</summary>
    public decimal? Custo { get; init; }
}
public sealed record PmoHhRealizado(decimal? Total, IReadOnlyList<PmoHhDisciplina> PorDisciplina, string Fonte)
{
    /// <summary>CUSTO REALIZADO oficial = Σ Actual_Cost_Total_Corrigido (coluna pronta). Só vale quando CustoOficial = true.</summary>
    public decimal? CustoTotal { get; init; }
    public bool CustoOficial { get; init; }
}

public static class PmoAvancoRules
{
    /// <summary>
    /// Semana vigente = REGRA OFICIAL existente (ProductionDashboards.InicioSemana: domingo a sábado, mesma convenção de
    /// PmoLdpSnapshot.Inicio_da_Semana). O avanço previsto "de hoje" usa como corte o FIM desta semana (sábado) — nunca
    /// semanas posteriores.
    /// </summary>
    public static (DateTime Inicio, DateTime Fim) SemanaVigente(DateTime referencia)
    {
        var inicio = Dashboards.ProductionDashboards.InicioSemana(referencia);
        return (inicio, inicio.AddDays(6));
    }

    private static decimal? Pct(decimal num, decimal den) => den > 0 ? Math.Round(num / den * 100m, 2) : null;

    public static PmoAvancoProjeto Calcular(int tipoLdp, DateTime referencia, IReadOnlyList<PmoDocumento> docs, decimal? plannedHhTotal)
    {
        var (inicio, fim) = SemanaVigente(referencia);
        return tipoLdp == 2 ? Tipo2(referencia, inicio, fim, docs) : Tipo1(referencia, inicio, fim, docs, plannedHhTotal);
    }

    /// <summary>
    /// TIPO 1:
    ///  • previsto = HH previsto dos documentos com baseline até o FIM da semana vigente ÷ HH previsto total;
    ///  • físico = Σ(HH previsto × % físico gerencial) ÷ Σ(HH previsto) dos documentos não cancelados.
    /// O % físico gerencial já contém a precedência: valor original da LDP quando > 0; caso contrário,
    /// override confirmado em qualidade.AuditoriaAvancoDocumento.
    /// </summary>
    private static PmoAvancoProjeto Tipo1(DateTime referencia, DateTime inicio, DateTime fim, IReadOnlyList<PmoDocumento> docs, decimal? plannedHhTotal)
    {
        var curva = PmoCurvaSRules.Build("", referencia, docs, plannedHhTotal);
        decimal Acumulado(DateTime corte, Func<CurvaSPonto, decimal> campo) =>
            curva.Pontos.LastOrDefault(p => p.Data.Date <= corte.Date) is { } p ? campo(p) : 0m;

        var denPrev = curva.Cobertura.HhCurvaPrevisto;
        var prev = Acumulado(fim, p => p.PrevistoHh);

        var documentosFisicos = docs
            .Where(d => d.IsDocument && d.StatusDocumento != "CANCELADO" && d.HHPrevisto is > 0)
            .ToArray();
        var denFis = documentosFisicos.Sum(d => d.HHPrevisto!.Value);
        var fis = documentosFisicos.Sum(d =>
            d.HHPrevisto!.Value * Math.Clamp(d.AvancoFisico ?? 0m, 0m, 100m) / 100m);
        var semFisico = documentosFisicos.Count(d => d.AvancoFisico is null);

        return new(1, referencia.Date, inicio, fim, Pct(prev, denPrev), prev, denPrev, Pct(fis, denFis), fis, denFis,
            curva.Cobertura.TotalDocumentos, semFisico, curva.Cobertura.SemBaseline,
            "Previsto: HH previsto dos documentos não cancelados com data prevista até o fim da semana vigente ÷ HH previsto total.",
            "Físico: Σ(HH previsto × % físico gerencial) ÷ Σ(HH previsto) dos documentos não cancelados; % físico gerencial = LDP quando informado, senão validação por Document_Status.",
            "HH previsto de cada documento");
    }

    /// <summary>
    /// TIPO 2 (regra de ESFORÇO, 30/09/2026 — substitui a média dos gates): cada FASE (gate 30/60/90) de cada documento entra
    /// INDIVIDUALMENTE, ponderada pelo seu esforço (Work planejado em horas da tarefa do gate no Cronograma Tipo 2):
    ///   físico   = Σ (esforço × %físico da fase) ÷ Σ esforço das fases com %físico;
    ///   previsto = Σ (esforço × %previsto da fase no corte) ÷ Σ esforço das fases com data prevista, onde %previsto da fase no
    ///              corte = 100% se a baseline do gate ≤ fim da semana vigente, senão 0% (mesma leitura da Curva S, por fase).
    /// Mesmas somas por gate (30/60/90) para os cards por fase. Fases consideradas: documento não cancelado, gate ativo e não
    /// cancelado. Fase sem esforço válido (sem Work ou ≤ 0) fica FORA do numerador e do denominador (nunca peso 0/1/nº do gate)
    /// e é contada em FasesSemEsforco.
    /// </summary>
    private static PmoAvancoProjeto Tipo2(DateTime referencia, DateTime inicio, DateTime fim, IReadOnlyList<PmoDocumento> docs)
    {
        var documentos = docs.Where(d => d.IsDocument).ToArray();
        var fases = documentos.Where(d => d.StatusDocumento != "CANCELADO")
            .SelectMany(d => d.Gates.Where(g => g.Ativo && !g.Cancelado)).ToArray();
        var comEsforco = fases.Where(g => g.EsforcoHoras is > 0).ToArray();
        (decimal Num, decimal Den) Fisico(IEnumerable<PmoGate> gs)
        {
            var v = gs.Where(g => g.FisicoConcluido is >= 0 and <= 100).ToArray();
            return (v.Sum(g => g.EsforcoHoras!.Value * g.FisicoConcluido!.Value / 100m), v.Sum(g => g.EsforcoHoras!.Value));
        }
        (decimal Num, decimal Den) Previsto(IEnumerable<PmoGate> gs)
        {
            var v = gs.Where(g => g.Baseline.HasValue).ToArray();
            return (v.Sum(g => g.Baseline!.Value.Date <= fim.Date ? g.EsforcoHoras!.Value : 0m), v.Sum(g => g.EsforcoHoras!.Value));
        }
        var (fisNum, fisDen) = Fisico(comEsforco);
        var (prevNum, prevDen) = Previsto(comEsforco);
        var porFase = new[] { 30, 60, 90 }.Select(p =>
        {
            var gs = comEsforco.Where(g => g.Percentual == p).ToArray();
            var (fn, fd) = Fisico(gs); var (pn, pd) = Previsto(gs);
            return new PmoAvancoFase(p, Pct(pn, pd), pn, pd, Pct(fn, fd), fn, fd);
        }).ToArray();
        return new(2, referencia.Date, inicio, fim, Pct(prevNum, prevDen), prevNum, prevDen, Pct(fisNum, fisDen), fisNum, fisDen,
            documentos.Length, comEsforco.Count(g => g.FisicoConcluido is not (>= 0 and <= 100)), comEsforco.Count(g => !g.Baseline.HasValue),
            "Previsto: Σ (esforço da fase × previsto da fase na semana vigente) ÷ Σ esforço das fases (gates 30/60/90 de cada documento).",
            "Físico: Σ (esforço da fase × avanço físico da fase) ÷ Σ esforço das fases (gates 30/60/90 de cada documento).",
            "Esforço planejado (Work, horas) de cada fase")
        { PorFase = porFase, FasesSemEsforco = fases.Length - comEsforco.Length };
    }

    /// <summary>Work planejado (horas) do snapshot do Cronograma Tipo 2 (mesma chave/estrutura de CronogramaTipo2Work).</summary>
    public static decimal? LerEsforcoHoras(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(CronogramaTipo2.CronogramaTipo2Work.ChaveSnapshot, out var v) || v.ValueKind != System.Text.Json.JsonValueKind.String) return null;
            return System.Text.Json.JsonSerializer.Deserialize<CronogramaTipo2.CronogramaTipo2Work.Snapshot>(v.GetString()!)?.ItemHoras;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>Esforço de cada gate do documento = Σ Work das tarefas válidas (não resumo) do MESMO documento e MESMO gate.</summary>
    public static PmoDocumento ComEsforcoDosGates(PmoDocumento d, IEnumerable<PmoTarefa> tarefasDoDocumento)
    {
        var validas = tarefasDoDocumento.Where(t => t.ElegivelDocumento && !t.Resumo).ToArray();
        return d with
        {
            Gates = d.Gates.Select(g =>
            {
                var hs = validas.Where(t => t.Gate == g.Percentual && t.EsforcoHoras.HasValue).Select(t => t.EsforcoHoras!.Value).ToArray();
                return g with { EsforcoHoras = hs.Length == 0 ? null : hs.Sum() };
            }).ToArray(),
        };
    }

    /// <summary>HH realizado = Σ Actual_HH_Total_Corrigido dos documentos da LDP atual (coluna pronta), total e por disciplina.</summary>
    public static PmoHhRealizado HhRealizado(IReadOnlyList<PmoDocumento> docs, bool custoOficial = false)
    {
        static decimal? Soma(IEnumerable<decimal?> v) { var a = v.Where(x => x.HasValue).ToArray(); return a.Length == 0 ? null : a.Sum(x => x!.Value); }
        return new(Soma(docs.Select(d => d.HHRealizado)),
            docs.GroupBy(d => Planning.ProjectPlanningRules.Key(d.Disciplina)).Select(g => new PmoHhDisciplina(g.First().Disciplina?.Trim(), Soma(g.Select(d => d.HHRealizado)))
                { Custo = custoOficial ? Soma(g.Select(d => d.CustoRealizado)) : null }).ToArray(),
            custoOficial ? "dbo.VW_Ldp_Unificada.Actual_HH_Total_Corrigido / Actual_Cost_Total_Corrigido" : "dbo.VW_Ldp_Unificada.Actual_HH_Total_Corrigido")
        { CustoTotal = custoOficial ? Soma(docs.Select(d => d.CustoRealizado)) : null, CustoOficial = custoOficial };
    }
}
