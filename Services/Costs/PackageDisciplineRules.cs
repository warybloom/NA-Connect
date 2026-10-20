namespace APS.WebApp.Services.Costs;

/// <summary>Modelos de cadastro de pacote. DOCUMENTO = comportamento original (costs.PackageDocument); DISCIPLINA = totais por disciplina.</summary>
public static class PackageModel
{
    public const string Documento = "DOCUMENTO", Disciplina = "DISCIPLINA";
    public const string Todos = "TODOS", Selecionados = "SELECIONADOS";
    public static bool IsDiscipline(string? model) => string.Equals(model, Disciplina, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Documento da LDP Unificada já classificado pela Norma de Coordenação (mesma regra do modelo Por Documento).</summary>
public sealed record LdpPackageDocument(string Codigo, string Disciplina, string? Titulo, string? Status, bool Cancelado,
    decimal? PlannedHH, decimal? PlannedCost, decimal? StatusPercentual = null, string? Doc3 = null);

public sealed record PackageDisciplineInput(string Disciplina, decimal HHPrevisto, decimal CustoPrevisto, string? Abrangencia,
    IReadOnlyList<string>? Documentos, string? Justificativa);
public sealed record DisciplinePackageInput(string? Empresa, string NomeReferencia, string? AliasTimesheet, string? Descricao,
    IReadOnlyList<PackageDisciplineInput> Disciplinas);

public sealed record DisciplineDocumentShare(string DocumentoCodigo, string? Titulo, string? DocumentStatus, decimal HHPrevisto, decimal CustoPrevisto,
    decimal? HHLdp, decimal? CustoLdp);
public sealed record DisciplineExcludedDocument(string DocumentoCodigo, string Motivo);
/// <summary>Rateio simulado/vigente de UMA disciplina. HH/custo do pacote são a referência; LDP é só conferência.</summary>
public sealed record DisciplinePlan(string Disciplina, string Abrangencia, decimal HHPrevisto, decimal CustoPrevisto, decimal HHLdp, decimal CustoLdp,
    decimal DivergenciaHH, decimal DivergenciaCusto, int DocumentosDisciplina, int DocumentosElegiveis,
    IReadOnlyList<DisciplineDocumentShare> Documentos, IReadOnlyList<DisciplineExcludedDocument> Excluidos, string? Justificativa,
    IReadOnlyList<string> Erros, IReadOnlyList<string> Avisos)
{
    public bool Valido => Erros.Count == 0;
}
public sealed record DisciplinePackagePreview(IReadOnlyList<DisciplinePlan> Disciplinas, decimal HHTotal, decimal CustoTotal, decimal HHLdpTotal,
    decimal CustoLdpTotal, bool Valido, IReadOnlyList<string> Erros);

public enum DisciplineRowKind { Direto, SemDocumento, ForaDaAbrangencia, DisciplinaIndefinida, Ambiguo }
public sealed record DisciplineRowAssignment(int RowIndex, int? PackageId, string? Disciplina, string? DocumentoCodigo, DisciplineRowKind Kind);
/// <summary>Pacote por disciplina para casar Timesheet: alias do profissional + documentos elegíveis de cada disciplina (códigos normalizados).</summary>
public sealed record DisciplinePackageMatcher(int PackageId, string? FallbackAlias, IReadOnlyList<(string Text, string Type)> Aliases,
    IReadOnlyDictionary<string, IReadOnlySet<string>> EligibleByDiscipline);

/// <summary>HH bruto (Timesheet original) separado do apropriado (até o previsto); o excedente nunca some.</summary>
public sealed record DocumentAppropriation(string DocumentoCodigo, decimal HHPrevisto, decimal HHDireto, decimal HHRateado, decimal HHBruto,
    decimal HHApropriado, decimal HHExcedente, decimal HHSaldo);

public static class PackageDisciplineRules
{
    public const int HoursDecimals = 4, MoneyDecimals = 2;

    /// <summary>Rateio igualitário que fecha EXATAMENTE o total: unidades de arredondamento vão aos primeiros itens.</summary>
    public static IReadOnlyList<decimal> EqualSplit(decimal total, int count, int decimals)
    {
        if (total < 0) throw new ArgumentException("Total de rateio não pode ser negativo.");
        if (count <= 0) throw new InvalidOperationException("Sem documentos elegíveis para o rateio.");
        var unit = 1m;
        for (var i = 0; i < decimals; i++) unit /= 10m;
        var baseValue = decimal.Floor(total / count / unit) * unit;
        var remainder = total - baseValue * count;
        var result = new decimal[count];
        for (var i = 0; i < count; i++)
        {
            var extra = remainder >= unit ? unit : 0m;
            result[i] = baseValue + extra;
            remainder -= extra;
        }
        result[count - 1] += remainder; // resíduo abaixo da precisão (total com mais casas que a precisão)
        return result;
    }

    public static string Key(string? value) => CostTimesheetMatchingRules.NormalizeName(value);

    public static string NormalizeScope(string? scope) =>
        string.Equals(scope?.Trim(), PackageModel.Selecionados, StringComparison.OrdinalIgnoreCase) ? PackageModel.Selecionados : PackageModel.Todos;

    /// <summary>
    /// Elegibilidade + rateio igualitário de uma disciplina contra a LDP ATUAL. TODOS = todos os documentos ativos (não cancelados) da
    /// disciplina, então documentos novos entram e cancelados saem automaticamente; SELECIONADOS = só os escolhidos, nunca de outra disciplina.
    /// </summary>
    public static DisciplinePlan Plan(PackageDisciplineInput input, IReadOnlyList<LdpPackageDocument> ldp, string semDisciplinaLabel)
    {
        var errors = new List<string>(); var warnings = new List<string>();
        var scope = NormalizeScope(input.Abrangencia);
        var key = Key(input.Disciplina);
        var ofDiscipline = ldp.Where(d => Key(d.Disciplina) == key).OrderBy(d => d.Codigo, StringComparer.Ordinal).ToList();
        var name = ofDiscipline.FirstOrDefault()?.Disciplina ?? (input.Disciplina ?? "").Trim();
        if (key.Length == 0) errors.Add("Informe a disciplina.");
        else if (key == Key(semDisciplinaLabel)) errors.Add("Documentos SEM DISCIPLINA não podem formar pacote: corrija a Norma de Coordenação.");
        else if (ofDiscipline.Count == 0) errors.Add($"Disciplina \"{name}\" sem documentos na LDP Unificada deste projeto.");
        if (input.HHPrevisto < 0 || input.CustoPrevisto < 0) errors.Add("HH e custo previstos não podem ser negativos.");
        if (input.HHPrevisto == 0) warnings.Add("HH previsto zerado: nenhuma hora do Timesheet será apropriada.");

        var excluded = new List<DisciplineExcludedDocument>();
        List<LdpPackageDocument> eligible;
        if (scope == PackageModel.Todos)
        {
            eligible = ofDiscipline.Where(d => !d.Cancelado).ToList();
            excluded.AddRange(ofDiscipline.Where(d => d.Cancelado).Select(d => new DisciplineExcludedDocument(d.Codigo, "CANCELADO na LDP: fora do rateio ativo.")));
        }
        else
        {
            var byCode = ldp.GroupBy(d => CostAllocationRules.NormalizeDocument(d.Codigo)).ToDictionary(g => g.Key, g => g.First());
            var requested = (input.Documentos ?? Array.Empty<string>()).Select(CostAllocationRules.NormalizeDocument).Where(x => x.Length > 0).Distinct().ToList();
            if (requested.Count == 0) errors.Add("Abrangência SELECIONADOS exige ao menos um documento.");
            eligible = new();
            foreach (var code in requested)
            {
                if (!byCode.TryGetValue(code, out var doc)) errors.Add($"{code}: não encontrado na LDP Unificada do projeto.");
                else if (Key(doc.Disciplina) != key) errors.Add($"{code}: pertence à disciplina \"{doc.Disciplina}\", não a \"{name}\".");
                else if (doc.Cancelado) excluded.Add(new(doc.Codigo, "CANCELADO na LDP: fora do rateio ativo."));
                else eligible.Add(doc);
            }
            eligible = eligible.OrderBy(d => d.Codigo, StringComparer.Ordinal).ToList();
        }
        if (errors.Count == 0 && eligible.Count == 0) errors.Add($"Disciplina \"{name}\" sem documentos elegíveis (todos cancelados ou nenhum selecionado): rateio impossível.");

        var hhLdp = eligible.Sum(d => d.PlannedHH ?? 0m); var costLdp = eligible.Sum(d => d.PlannedCost ?? 0m);
        var shares = new List<DisciplineDocumentShare>();
        if (errors.Count == 0)
        {
            var hh = EqualSplit(input.HHPrevisto, eligible.Count, HoursDecimals);
            var cost = EqualSplit(input.CustoPrevisto, eligible.Count, MoneyDecimals);
            shares = eligible.Select((d, i) => new DisciplineDocumentShare(d.Codigo, d.Titulo, d.Status, hh[i], cost[i], d.PlannedHH, d.PlannedCost)).ToList();
        }
        var divergenceH = input.HHPrevisto - hhLdp; var divergenceC = input.CustoPrevisto - costLdp;
        if (errors.Count == 0 && divergenceH != 0)
            warnings.Add($"Divergência de {divergenceH:+#,##0.##;-#,##0.##} HH entre o pacote ({input.HHPrevisto:#,##0.##}) e a LDP ({hhLdp:#,##0.##}). O pacote é a referência do rateio; a LDP não é alterada."
                + (string.IsNullOrWhiteSpace(input.Justificativa) ? " Registre uma justificativa." : ""));
        if (eligible.Any(d => d.PlannedHH is null)) warnings.Add("Há documentos sem Planned_HH na LDP: a conferência considera 0 para eles.");
        return new(name, scope, input.HHPrevisto, input.CustoPrevisto, hhLdp, costLdp, divergenceH, divergenceC, ofDiscipline.Count, eligible.Count,
            shares, excluded, string.IsNullOrWhiteSpace(input.Justificativa) ? null : input.Justificativa.Trim(), errors, warnings);
    }

    public static DisciplinePackagePreview Preview(DisciplinePackageInput input, IReadOnlyList<LdpPackageDocument> ldp, string semDisciplinaLabel)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.NomeReferencia)) errors.Add("Informe o nome do pacote/profissional.");
        var disciplines = input.Disciplinas ?? Array.Empty<PackageDisciplineInput>();
        if (disciplines.Count == 0) errors.Add("Informe ao menos uma disciplina.");
        foreach (var dup in disciplines.GroupBy(d => Key(d.Disciplina)).Where(g => g.Key.Length > 0 && g.Count() > 1))
            errors.Add($"Disciplina repetida no pacote: {dup.First().Disciplina}.");
        var plans = disciplines.Select(d => Plan(d, ldp, semDisciplinaLabel)).ToList();
        return new(plans, plans.Sum(x => x.HHPrevisto), plans.Sum(x => x.CustoPrevisto), plans.Sum(x => x.HHLdp), plans.Sum(x => x.CustoLdp),
            errors.Count == 0 && plans.All(x => x.Valido), errors);
    }

    /// <summary>
    /// Classifica linhas do Timesheet AINDA NÃO atribuídas a pacotes Por Documento. Com documento elegível do pacote = Direto (vínculo preservado);
    /// documento da LDP fora da abrangência = ForaDaAbrangencia; sem documento identificado = SemDocumento (rateio) quando a disciplina é única
    /// ou pode ser deduzida; caso contrário DisciplinaIndefinida. Nome que casa com dois pacotes por disciplina = Ambiguo.
    /// </summary>
    public static IReadOnlyList<DisciplineRowAssignment> AssignRows(IReadOnlyList<TimesheetWork> rows, IEnumerable<int> candidateRows,
        IReadOnlyList<DisciplinePackageMatcher> packages, IReadOnlySet<string> ldpDocuments, Func<TimesheetWork, string?> disciplineHint)
    {
        var output = new List<DisciplineRowAssignment>();
        if (packages.Count == 0) return output;
        foreach (var index in candidateRows)
        {
            var row = rows[index];
            var matched = packages.Where(p => CostTimesheetMatchingRules.Matches(p.FallbackAlias, p.Aliases, row.Name)).ToList();
            if (matched.Count == 0) continue;
            if (matched.Count > 1) { output.Add(new(index, null, null, null, DisciplineRowKind.Ambiguo)); continue; }
            var package = matched[0];
            var code = CostAllocationRules.NormalizeDocument(row.DocumentoCodigo);
            var direct = package.EligibleByDiscipline.FirstOrDefault(d => d.Value.Contains(code));
            if (code.Length > 0 && direct.Key is not null) { output.Add(new(index, package.PackageId, direct.Key, code, DisciplineRowKind.Direto)); continue; }
            if (code.Length > 0 && ldpDocuments.Contains(code)) { output.Add(new(index, package.PackageId, null, code, DisciplineRowKind.ForaDaAbrangencia)); continue; }
            string? discipline = null;
            if (package.EligibleByDiscipline.Count == 1) discipline = package.EligibleByDiscipline.Keys.Single();
            else if (disciplineHint(row) is { } hint) discipline = package.EligibleByDiscipline.Keys.FirstOrDefault(k => Key(k) == Key(hint));
            output.Add(new(index, package.PackageId, discipline, null, discipline is null ? DisciplineRowKind.DisciplinaIndefinida : DisciplineRowKind.SemDocumento));
        }
        return output;
    }

    /// <summary>
    /// Bruto por documento = vínculo direto + rateio das horas sem documento. O teto é a soma contratada da disciplina;
    /// concentração acima da parcela documental é alerta, não motivo para descartar horas legítimas.
    /// Horas com documento NÃO entram no rateio sem documento (sem dupla contagem).
    /// </summary>
    public static IReadOnlyList<DocumentAppropriation> Appropriate(IReadOnlyList<(string Codigo, decimal Previsto)> documents,
        IReadOnlyDictionary<string, decimal> directHours, decimal undocumentedHours)
    {
        if (documents.Count == 0) return Array.Empty<DocumentAppropriation>();
        var shares = EqualSplit(Math.Max(0m, undocumentedHours), documents.Count, HoursDecimals);
        var grossHours = documents.Select((d,i) => Math.Max(0m, directHours.GetValueOrDefault(CostAllocationRules.NormalizeDocument(d.Codigo))) + shares[i]).ToArray();
        var totalGross = grossHours.Sum();
        var limit = Math.Min(totalGross, documents.Sum(d=>Math.Max(0m,d.Previsto)));
        // O teto é disciplinar. Rateio proporcional cumulativo conserva total e vínculo, inclusive documentos com zero horas.
        var result = new List<DocumentAppropriation>();
        decimal cumulativeGross=0, previous=0;
        for(var i=0;i<documents.Count;i++)
        {
            var d=documents[i]; var gross=grossHours[i];
            cumulativeGross+=gross;
            var cumulative=totalGross==0?0:cumulativeGross==totalGross?limit:decimal.Round(limit*cumulativeGross/totalGross,6,MidpointRounding.ToZero);
            var appropriated=cumulative-previous; previous=cumulative;
            result.Add(new(d.Codigo,d.Previsto,gross-shares[i],shares[i],gross,appropriated,gross-appropriated,Math.Max(0m,d.Previsto-appropriated)));
        }
        return result;
    }
}
