namespace APS.WebApp.Services.Costs;

public sealed record ProfessionalCostPeriodRule(int Id, DateOnly Start, DateOnly? End, decimal MonthlyHours, decimal Remuneration, decimal? ManualRate, bool Active = true);
public sealed record ProfessionalCostComponentRule(string CalculationType, decimal Amount, decimal? Percentage, string Base);
public sealed record ProjectTaxPeriodRule(DateOnly Start, DateOnly? End, string CalculationType, decimal? Percentage, decimal? FixedValue, string Base);
public sealed record CostRemainingRule(decimal Planned, decimal Actual, decimal Remaining, decimal Excess);

public static class ProjectCostHistoryRules
{
    public static bool Covers(ProfessionalCostPeriodRule period, DateOnly date) => period.Active && date >= period.Start && (period.End is null || date <= period.End.Value);

    public static ProfessionalCostPeriodRule? Resolve(IEnumerable<ProfessionalCostPeriodRule> periods, DateOnly date)
    {
        var matches = periods.Where(x => Covers(x, date)).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("Vigências sobrepostas para o profissional.");
        return matches.SingleOrDefault();
    }

    public static void EnsureNoOverlap(DateOnly start, DateOnly? end, IEnumerable<ProfessionalCostPeriodRule> existing)
    {
        if (end is not null && end.Value < start) throw new ArgumentException("A data final deve ser igual ou posterior à inicial.");
        if (existing.Any(p => start <= (p.End ?? DateOnly.MaxValue) && p.Start <= (end ?? DateOnly.MaxValue)))
            throw new InvalidOperationException("A vigência sobrepõe outra configuração ativa.");
    }

    /// <summary>Only a strictly later period may close the current open period. Historical backfill never rewrites it.</summary>
    public static bool RequiresClosingOpenPeriod(DateOnly proposedStart, DateOnly openPeriodStart) => proposedStart > openPeriodStart;

    public static decimal MonthlyCost(decimal remuneration, IEnumerable<ProfessionalCostComponentRule> items)
    {
        if (remuneration < 0) throw new ArgumentOutOfRangeException(nameof(remuneration));
        decimal total = remuneration;
        foreach (var item in items)
        {
            if (item.CalculationType.Equals("FIXO", StringComparison.OrdinalIgnoreCase))
            {
                if (item.Amount < 0) throw new ArgumentOutOfRangeException(nameof(items));
                total += item.Amount;
            }
            else if (item.CalculationType.Equals("PERCENTUAL", StringComparison.OrdinalIgnoreCase))
            {
                if (item.Percentage is null or < 0) throw new ArgumentException("Percentual inválido.");
                var basis = item.Base.Equals("REMUNERACAO", StringComparison.OrdinalIgnoreCase) ? remuneration :
                    throw new ArgumentException($"Base percentual ainda não suportada: {item.Base}.");
                total += basis * item.Percentage.Value / 100m;
            }
            else throw new ArgumentException("Tipo de cálculo inválido.");
        }
        return total;
    }

    public static decimal HourlyRate(ProfessionalCostPeriodRule period, IEnumerable<ProfessionalCostComponentRule> items)
    {
        if (period.ManualRate is >= 0m) return period.ManualRate.Value;
        if (period.MonthlyHours <= 0) throw new ArgumentException("HH base mensal deve ser positiva.");
        return MonthlyCost(period.Remuneration, items) / period.MonthlyHours;
    }

    public static decimal Tax(decimal? percentage, decimal? fixedValue, string calculationType, string basis, decimal revenue, decimal cost, decimal otherBasis = 0m)
    {
        if (calculationType.Equals("FIXO", StringComparison.OrdinalIgnoreCase)) return fixedValue is >= 0m ? fixedValue.Value : throw new ArgumentException("Valor fixo inválido.");
        if (!calculationType.Equals("PERCENTUAL", StringComparison.OrdinalIgnoreCase) || percentage is null or < 0) throw new ArgumentException("Cálculo de imposto inválido.");
        var baseValue = basis.ToUpperInvariant() switch { "RECEITA" => revenue, "CUSTO" => cost, "OUTRO" => otherBasis, "VALOR_FIXO" => fixedValue ?? throw new ArgumentException("Informe a base fixa."), _ => throw new ArgumentException("Base de cálculo inválida.") };
        if (baseValue < 0) throw new ArgumentOutOfRangeException(nameof(baseValue));
        return baseValue * percentage.Value / 100m;
    }

    public static CostRemainingRule Remaining(decimal planned, decimal actual)
    {
        var remaining = planned - actual;
        return new(planned, actual, remaining, Math.Max(0m, -remaining));
    }
}
