using System.Globalization;

namespace APS.WebApp.Services.Tv;

/// <summary>
/// Programação Diária AUTOMÁTICA (20/10/2026): slide com TvSlide.DataReferencia = "AUTO". HOJE = data civil de
/// America/Bahia no instante da consulta (nunca uma data congelada na configuração); ONTEM = hoje − 1. A tela lê a
/// programação semanal na janela [ontem, hoje] — cruza a semana no domingo sem duplicar (a consulta é uma só).
/// </summary>
public static class TvDiaAutomatico
{
    public const string Auto = "AUTO";

    /// <summary>
    /// Exibição do slide AUTOMÁTICO (TvSlide.LayoutDiario, 20/10/2026): padrão (ausente) = layout da 02 dividido em Hoje/Ontem;
    /// cards = blocos por disciplina num slide; disciplina = uma disciplina por slide (TvSlide.Disciplina obrigatória).
    /// </summary>
    public const string LayoutPadrao = "padrao", LayoutCards = "cards", LayoutDisciplina = "disciplina";
    public static readonly IReadOnlySet<string> Layouts = new HashSet<string>(StringComparer.Ordinal) { LayoutPadrao, LayoutCards, LayoutDisciplina };

    /// <summary>Tolerância de relógio entre a TV e o servidor perto da meia-noite (a janela continua calculada aqui).</summary>
    public static readonly TimeSpan ToleranciaRelogio = TimeSpan.FromMinutes(5);

    private static readonly TimeZoneInfo Bahia = ResolverFuso();

    // IANA (Linux/ICU) ou id Windows; sem nenhum dos dois, UTC−3 fixo (a Bahia não tem horário de verão desde 2012).
    private static TimeZoneInfo ResolverFuso()
    {
        foreach (var id in new[] { "America/Bahia", "Bahia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Bahia-UTC-3", TimeSpan.FromHours(-3), "Bahia (UTC-3)", "Bahia (UTC-3)");
    }

    public static DateOnly Hoje(DateTimeOffset agora) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(agora, Bahia).DateTime);

    public static (DateOnly Inicio, DateOnly Fim) Janela(DateOnly hoje) => (hoje.AddDays(-1), hoje);

    /// <summary>
    /// Portal TV: inicio/fim precisam ser EXATAMENTE a janela [ontem, hoje] calculada no servidor para o instante atual
    /// (± ToleranciaRelogio, só para a virada do dia). Qualquer outro período é recusado.
    /// </summary>
    public static bool JanelaPermitida(string? inicio, string? fim, DateTimeOffset agora) =>
        new[] { agora - ToleranciaRelogio, agora, agora + ToleranciaRelogio }.Select(Hoje).Distinct().Any(hoje =>
        {
            var (i, f) = Janela(hoje);
            return inicio == Iso(i) && fim == Iso(f);
        });

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
