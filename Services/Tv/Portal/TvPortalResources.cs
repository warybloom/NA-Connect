using System.Globalization;

namespace APS.WebApp.Services.Tv.Portal;

/// <summary>Explicit read-only resources. Unknown screens/resources fail closed, never a generic URL proxy.</summary>
public static class TvPortalResources
{
    private static readonly string[] Bi = ["/api/bi-chs001/ldp", "/api/bi-chs001/programacao", "/api/bi-chs001/cronograma",
        "/api/bi-chs001/cronograma-diario", "/api/bi-chs001/schedule/tarefas", "/api/bi-chs001/schedule/trabalho-diario"];
    private static readonly HashSet<string> Dashboards = new(StringComparer.Ordinal) {
        "ldp", "programacao", "cap-externo", "cap-interno", "desvio-escopo", "change-order", "hold", "desvio-qualidade", "rnc",
        "nivelamento-programacao", "controle-programacao", "controle-documental" };
    public static IReadOnlyList<string> ForScreen(string screen) => TvTelas.Obter(screen) is null ? [] :
        screen.StartsWith("gestao-vista-tipo2-", StringComparison.Ordinal) ? Bi :
        Dashboards.Contains(screen) ? [$"/api/reports/production/dashboard/{screen}"] :
        screen is "resumo" or "resumo-tv-teste" ? ["/api/reports/gestao-vista", "/api/reports/production/dashboard/ldp"] :
        screen.StartsWith("gestao-a-vista-", StringComparison.Ordinal) ? ["/api/reports/gestao-vista", "/api/reports/production/dashboard/ldp", "/api/reports/gestao-vista-jamile/disciplinas", "/api/reports/gestao-vista-jamile/programacao"] :
        screen is "analise-financeira" or "analise-financeira-projeto" or "analise-financeira-documento" ? ["/api/reports/gestao-vista"] :
        screen == "curva-s" ? ["/api/reports/curva-s"] : [];
    public const string ProgramacaoJamile = "/api/reports/gestao-vista-jamile/programacao";
    /// <summary>
    /// Tela com data por item (Programação Diária): a programação só pode ser lida para a semana (domingo–sábado) da data
    /// SALVA no slide — a TV não abre outras semanas trocando inicio/fim. Slide AUTOMÁTICO ("AUTO"): somente a janela
    /// [ontem, hoje] calculada no servidor (data civil da Bahia, TvDiaAutomatico). Demais telas/recursos: sem restrição extra.
    /// </summary>
    public static bool PeriodoPermitido(TvSlide slide, string resource, IQueryCollection query, DateTimeOffset? agora = null)
    {
        if (TvTelas.Obter(slide.ScreenId) is not { DataReferenciaObrigatoria: true }) return true;
        if (!string.Equals(resource, ProgramacaoJamile, StringComparison.Ordinal)) return true;
        if (slide.DataReferencia == TvDiaAutomatico.Auto)
            return TvDiaAutomatico.JanelaPermitida(query["inicio"].ToString(), query["fim"].ToString(), agora ?? DateTimeOffset.UtcNow);
        if (!DateOnly.TryParseExact(slide.DataReferencia, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)) return false;
        var inicio = dia.AddDays(-(int)dia.DayOfWeek);
        return query["inicio"].ToString() == inicio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            && query["fim"].ToString() == inicio.AddDays(6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
    public static bool Supported(TvPresentation p) => p.Slides.Count > 0 && p.Slides.All(s => ForScreen(s.ScreenId).Count > 0);
    public static IReadOnlyList<string> ReadKeys(TvSlide slide) => TvTelas.Obter(slide.ScreenId)!.PermissionKeys
        .Concat(new[] { "production.view", "reports.gestao.view", "production.managerial.ldp.view", "production.operational.ldp.view" }).ToArray();
    public static bool ValidQuery(IQueryCollection query) => query.All(q => (q.Value.Count == 1 || q.Key == "projetos" || q.Key.StartsWith("f.", StringComparison.Ordinal))
        && (q.Key is "resource" or "projectId" or "hubId" or "versao" or "inicio" or "fim" or "dataReferencia" or "page" or "pageSize" or "projetos" || q.Key.StartsWith("f.", StringComparison.Ordinal)))
        && (!query.TryGetValue("versao", out var v) || int.TryParse(v, out var n) && n > 0);
}
