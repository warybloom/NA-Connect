using System.Data;
using System.Globalization;
using System.Text;
using APS.WebApp.Services.Costs;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

/// <summary>
/// HH realizado de uma pessoa num documento num dia. Fonte (30/10/2026): gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial — a MESMA
/// dos lançamentos do PMO e do HH oficial por documento (Actual_HH_Corrigido da VW_Ldp_Unificada), com o DOCUMENTO CORRIGIDO (De/Para).
/// DocumentoCodigoOriginal/Correcao preservam o código lançado quando o De/Para o trocou (rastreabilidade, nunca outro documento).
/// Disciplina = disciplina do LANÇAMENTO (atributo da pessoa); nunca vira disciplina do documento.
/// </summary>
public sealed record HhCustoResponsavelLinha(string Pessoa, string? DocumentoCodigo, string? Disciplina, DateTime? Data, decimal Hh, int Lancamentos)
{
    public string? DocumentoCodigoOriginal { get; init; }
    public string? Correcao { get; init; }
}

/// <summary>
/// Identificação do responsável. PACOTE = o nome casa com pacote ativo do projeto pela MESMA regra oficial do custeio
/// (<see cref="CostTimesheetMatchingRules.Matches"/>: AliasTimesheet/NomeReferencia + costs.TimesheetMatchAlias); a empresa vem de
/// costs.Package.Empresa. PACOTE_SEM_EMPRESA / AMBIGUO = pendência explícita (nunca empresa inventada). PESSOA = sem pacote.
/// </summary>
public sealed record HhCustoResponsavel(string Pessoa, string Tipo, string? Empresa, IReadOnlyList<string> Pacotes);

/// <summary>Rótulo de disciplina (qualquer grafia conhecida) → nome PT de exibição/agrupamento do projeto. Fonte = NORMA | MAPA_PT.</summary>
public sealed record HhCustoDisciplinaNome(string Origem, string Nome, string? Canonico, string Fonte);

public sealed record HhCustoResponsaveisReport(string ProjectId, string CrOs, string Fonte, IReadOnlyList<HhCustoResponsavelLinha> Linhas,
    IReadOnlyList<HhCustoResponsavel> Responsaveis)
{
    /// <summary>Resolvedor de disciplinas do projeto. Rótulo ausente aqui = não reconhecido: o frontend mantém o original e o lista.</summary>
    public IReadOnlyList<HhCustoDisciplinaNome> Disciplinas { get; init; } = [];
    /// <summary>Rótulos com mais de um nome possível (nunca unidos em silêncio).</summary>
    public IReadOnlyList<string> DiagnosticosDisciplina { get; init; } = [];
}

public sealed record HhCustoPacoteAlias(int PackageId, string? Empresa, string NomeReferencia, string? Fallback, IReadOnlyList<(string Text, string Type)> Aliases);

public interface IHhCustoResponsaveisReader
{
    Task<HhCustoResponsaveisReport> ReadHhCustoResponsaveisAsync(string projectId, CancellationToken ct);
}

public static class HhCustoResponsavelRules
{
    public const string Pessoa = "PESSOA", Pacote = "PACOTE", PacoteSemEmpresa = "PACOTE_SEM_EMPRESA", Ambiguo = "AMBIGUO";

    public static HhCustoResponsavel Resolver(string pessoa, IReadOnlyList<HhCustoPacoteAlias> pacotes)
    {
        var casados = pacotes.Where(p => CostTimesheetMatchingRules.Matches(p.Fallback, p.Aliases, pessoa)).ToList();
        if (casados.Count == 0) return new(pessoa, Pessoa, null, []);
        var nomes = casados.Select(p => p.NomeReferencia).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var empresas = casados.Select(p => string.IsNullOrWhiteSpace(p.Empresa) ? null : p.Empresa.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (empresas.Count == 1 && empresas[0] is { } empresa) return new(pessoa, Pacote, empresa, nomes);
        if (empresas.All(e => e is null)) return new(pessoa, PacoteSemEmpresa, null, nomes);
        return new(pessoa, Ambiguo, null, nomes);
    }
}

/// <summary>
/// Unificação de disciplinas do Controle de HH / Custo, só com cadastros existentes (nenhum alias novo):
///  1. canônico (EN) do rótulo: coordenacao.DisciplinaAlias → DisciplinaPadrao; senão ref.tbMapaDisciplina (PT ou EN);
///  2. nome: a disciplina da NORMA do projeto com o mesmo canônico (prioridade); se a Norma tiver mais de um nome para ele, o que
///     coincide com o PT de ref.tbMapaDisciplina; sem Norma, o PT único de ref.tbMapaDisciplina; senão o rótulo fica como está.
/// Comparação sem acento/caixa/espaços extras (mesma semântica CI_AI do SQL atual). Ambiguidade nunca une: vira diagnóstico.
/// </summary>
public static class HhCustoDisciplinaRules
{
    public const string FonteNorma = "NORMA", FonteMapaPt = "MAPA_PT";

    public static string Chave(string? v)
    {
        var s = (v ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return string.Join(' ', sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static (IReadOnlyList<HhCustoDisciplinaNome> Nomes, IReadOnlyList<string> Diagnosticos) Construir(
        IEnumerable<string> normaNomes, IEnumerable<(string Alias, string Canonico)> aliases, IEnumerable<(string Pt, string En)> mapa)
    {
        var aliasCanon = aliases.Where(a => Chave(a.Alias) != "" && Chave(a.Canonico) != "").GroupBy(a => Chave(a.Alias))
            .ToDictionary(g => g.Key, g => g.Select(a => Chave(a.Canonico)).Distinct().ToList());
        var pares = mapa.Where(m => Chave(m.Pt) != "" && Chave(m.En) != "").ToList();
        var ptPorEn = pares.GroupBy(m => Chave(m.En)).ToDictionary(g => g.Key, g => g.GroupBy(m => Chave(m.Pt)).Select(x => x.First().Pt.Trim()).ToList());
        var enPorPt = pares.GroupBy(m => Chave(m.Pt)).ToDictionary(g => g.Key, g => g.Select(m => Chave(m.En)).Distinct().ToList());
        var diagnosticos = new List<string>();

        string? Canonico(string rotulo)
        {
            var k = Chave(rotulo);
            if (aliasCanon.TryGetValue(k, out var c)) return c.Count == 1 ? c[0] : null;
            if (enPorPt.TryGetValue(k, out var e)) return e.Count == 1 ? e[0] : null;
            return ptPorEn.ContainsKey(k) ? k : null;
        }

        var norma = normaNomes.Where(n => Chave(n) != "").GroupBy(Chave).Select(g => g.First().Trim()).ToList();
        var normaPorCanon = norma.Select(n => (Nome: n, Canon: Canonico(n))).Where(x => x.Canon is not null)
            .GroupBy(x => x.Canon!).ToDictionary(g => g.Key, g => g.Select(x => x.Nome).ToList());

        (string Nome, string Fonte)? NomePara(string canon)
        {
            var pts = ptPorEn.GetValueOrDefault(canon) ?? [];
            if (normaPorCanon.TryGetValue(canon, out var ns))
            {
                if (ns.Count == 1) return (ns[0], FonteNorma);
                var comum = ns.Where(n => pts.Any(p => Chave(p) == Chave(n))).ToList();
                if (comum.Count == 1) return (comum[0], FonteNorma);
                diagnosticos.Add($"Norma com mais de um nome para {canon}: {string.Join(" / ", ns)}; mantido o rótulo original.");
                return null;
            }
            if (pts.Count == 1) return (pts[0], FonteMapaPt);
            if (pts.Count > 1) diagnosticos.Add($"{canon} tem mais de um nome PT no mapa de disciplinas ({string.Join(" / ", pts)}) e nenhum na Norma; mantido o rótulo original.");
            return null;
        }

        var rotulos = norma.Concat(aliasCanon.Keys).Concat(aliasCanon.Values.SelectMany(v => v)).Concat(pares.SelectMany(p => new[] { p.Pt, p.En }));
        var saida = new Dictionary<string, HhCustoDisciplinaNome>(StringComparer.Ordinal);
        var nomesCache = new Dictionary<string, (string Nome, string Fonte)?>(StringComparer.Ordinal);
        foreach (var rotulo in rotulos)
        {
            var k = Chave(rotulo);
            if (k == "" || saida.ContainsKey(k)) continue;
            var canon = Canonico(rotulo);
            (string Nome, string Fonte)? nome = null;
            if (canon is not null)
            {
                if (!nomesCache.TryGetValue(canon, out nome)) nomesCache[canon] = nome = NomePara(canon);
            }
            else if (norma.Any(n => Chave(n) == k)) nome = (norma.First(n => Chave(n) == k), FonteNorma);
            if (nome is { } n) saida[k] = new(k, n.Nome, canon, n.Fonte);
        }
        return (saida.Values.OrderBy(x => x.Origem, StringComparer.Ordinal).ToList(), diagnosticos.Distinct().ToList());
    }
}

public sealed partial class ProductionAuditRepository : IHhCustoResponsaveisReader
{
    /// <summary>Lançamentos por pessoa × documento CORRIGIDO × dia — mesma view dos lançamentos do PMO (PmoExecutiveData).</summary>
    public const string HhCustoResponsaveisSql = """
        SELECT COALESCE(NULLIF(LTRIM(RTRIM(NomeProfissional)),''),N'SEM NOME') Nome,
               NULLIF(UPPER(LTRIM(RTRIM(DocumentoCodigo_Corrigido))),'') Codigo,
               MAX(NULLIF(LTRIM(RTRIM(Disciplina)),'')) Disc, CAST(DataLancamento AS date) Dia,
               SUM(ISNULL(HorasTrabalhadas,0)) Trab, COUNT(1) Lanc,
               MAX(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(DocumentoCodigo_Original,'')))) <> UPPER(LTRIM(RTRIM(ISNULL(DocumentoCodigo_Corrigido,''))))
                        THEN UPPER(LTRIM(RTRIM(DocumentoCodigo_Original))) END) CodigoOriginal,
               MAX(NULLIF(LTRIM(RTRIM(Tipo_Correcao)),'')) Correcao
        FROM gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial
        WHERE Project_ID_ACC=@p
        GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(NomeProfissional)),''),N'SEM NOME'), NULLIF(UPPER(LTRIM(RTRIM(DocumentoCodigo_Corrigido))),''), CAST(DataLancamento AS date)
        ORDER BY Nome, Codigo, Dia;
        """;

    public async Task<HhCustoResponsaveisReport> ReadHhCustoResponsaveisAsync(string projectId, CancellationToken ct)
    {
        // CR/OS só informativo: a fonte é por Project_ID_ACC (mesma do HH oficial), então não depende do mapeamento CR/OS.
        var scope = await ResolveScopeAsync(projectId, ct);
        var linhas = new List<HhCustoResponsavelLinha>();
        await using var c = await OpenAsync(ct);
        await using (var cmd = new SqlCommand(HhCustoResponsaveisSql, c) { CommandTimeout = 90 })
        {
            cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                linhas.Add(new(Text(r, 0) ?? ProductionAuditStatus.SemNome, Text(r, 1), Text(r, 2), Date(r, 3), Num(r, 4) ?? 0m, r.GetInt32(5))
                    { DocumentoCodigoOriginal = Text(r, 6), Correcao = Text(r, 7) });
        }
        var pacotes = await PacotesAsync(c, projectId, ct);
        var responsaveis = linhas.Select(l => l.Pessoa).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => HhCustoResponsavelRules.Resolver(p, pacotes)).ToList();
        var (nomes, diagnosticos) = await DisciplinasAsync(c, projectId, ct);
        return new(projectId, scope.Status == ProductionAuditScopeStatus.Ok ? scope.CrOs ?? "" : "",
            "gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial (NomeProfissional, DocumentoCodigo_Corrigido, HorasTrabalhadas) + costs.Package.Empresa",
            linhas, responsaveis) { Disciplinas = nomes, DiagnosticosDisciplina = diagnosticos };
    }

    private static async Task<(IReadOnlyList<HhCustoDisciplinaNome>, IReadOnlyList<string>)> DisciplinasAsync(SqlConnection c, string projectId, CancellationToken ct)
    {
        const string sql = """
            SELECT DISTINCT m.Disciplina FROM coordenacao.ProjetoNorma pn
              JOIN coordenacao.Norma n ON n.NormaId=pn.NormaId AND n.Ativa=1
              JOIN coordenacao.NormaMapeamento m ON m.NormaId=n.NormaId
             WHERE pn.Project_ID_ACC=@p AND NULLIF(LTRIM(RTRIM(m.Disciplina)),'') IS NOT NULL;
            IF OBJECT_ID(N'coordenacao.DisciplinaAlias', N'U') IS NOT NULL
              SELECT DA.Alias, DP.Nome_Padrao FROM coordenacao.DisciplinaAlias DA
                JOIN coordenacao.DisciplinaPadrao DP ON DP.DisciplinaPadraoId=DA.DisciplinaPadraoId AND DP.Ativa=1;
            ELSE SELECT CAST(NULL AS nvarchar(200)), CAST(NULL AS nvarchar(200)) WHERE 1=0;
            IF OBJECT_ID(N'ref.tbMapaDisciplina', N'U') IS NOT NULL
              SELECT DISTINCT Disciplina_PT, Disciplina_EN FROM ref.tbMapaDisciplina WHERE Ativo=1;
            ELSE SELECT CAST(NULL AS nvarchar(200)), CAST(NULL AS nvarchar(200)) WHERE 1=0;
            """;
        var norma = new List<string>(); var aliases = new List<(string, string)>(); var mapa = new List<(string, string)>();
        await using var cmd = new SqlCommand(sql, c);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) norma.Add(Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture)!);
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct)) if (!r.IsDBNull(0) && !r.IsDBNull(1)) aliases.Add((Convert.ToString(r.GetValue(0))!, Convert.ToString(r.GetValue(1))!));
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct)) if (!r.IsDBNull(0) && !r.IsDBNull(1)) mapa.Add((Convert.ToString(r.GetValue(0))!, Convert.ToString(r.GetValue(1))!));
        return HhCustoDisciplinaRules.Construir(norma, aliases, mapa);
    }

    private static async Task<IReadOnlyList<HhCustoPacoteAlias>> PacotesAsync(SqlConnection c, string projectId, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'costs.Package', N'U') IS NOT NULL AND OBJECT_ID(N'costs.TimesheetMatchAlias', N'U') IS NOT NULL
              SELECT p.Id, p.Empresa, p.NomeReferencia, COALESCE(NULLIF(LTRIM(RTRIM(p.AliasTimesheet)),''),p.NomeReferencia), a.TextoMatch, a.TipoMatch
              FROM costs.Package p LEFT JOIN costs.TimesheetMatchAlias a ON a.PackageId=p.Id AND a.Ativo=1
              WHERE p.Project_ID_ACC=@p AND p.Ativo=1;
            """;
        var rows = new Dictionary<int, (string? Empresa, string Nome, string? Fallback, List<(string, string)> Aliases)>();
        await using var cmd = new SqlCommand(sql, c);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var id = r.GetInt32(0);
            if (!rows.TryGetValue(id, out var v)) rows[id] = v = (Text(r, 1), r.GetString(2), Text(r, 3), new());
            if (!r.IsDBNull(4)) v.Aliases.Add((r.GetString(4), r.IsDBNull(5) ? "CONTEM" : r.GetString(5)));
        }
        return rows.Select(kv => new HhCustoPacoteAlias(kv.Key, kv.Value.Empresa, kv.Value.Nome, kv.Value.Fallback, kv.Value.Aliases)).ToList();
    }
}
