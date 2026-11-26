using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Reports;

public sealed record Avanco4DTarefaFonte(string? Wbs, string? Tarefa, string? Documento, string? Disciplina, decimal? Previsto,
    decimal? Realizado, DateTime? Inicio, DateTime? Fim, DateTime? Tendencia, bool Resumo);
public sealed record Avanco4DLeitura(DateTime Leitura, decimal? Previsto, decimal? Realizado);

public sealed record Avanco4DTarefa(string? Wbs, string? Tarefa, string? Documento, string? Disciplina, decimal? Previsto, decimal? Realizado,
    decimal? DesvioPp, string SituacaoRealizado, string Situacao, int? DiasAtraso, DateTime? Inicio, DateTime? Fim, DateTime? Tendencia);
public sealed record Avanco4DPonto(DateTime Data, decimal? Previsto, decimal? Realizado);
public sealed record Avanco4DDisciplina(string Disciplina, decimal Previsto, decimal Realizado, decimal DesvioPp, int Tarefas);
public sealed record Avanco4DKpis(int TotalTarefas, int NoPrazo, int Atrasadas, int SemDado);
public sealed record Avanco4DDiagnostico(int TarefasFolha, int Consideradas, int SemRealizado, int SemPrevisto, int SemData, int ZeroConfirmado,
    int Invalidos, decimal CoberturaPct, decimal CoberturaMinimaPct, bool PrevistoPreenchido, bool RealizadoPreenchido);
public sealed record Avanco4DReport(string ProjectId, int TipoLdp, bool Disponivel, string? Motivo, DateTime DataReferencia, DateTime? DataCorte,
    decimal? PrevistoGeral, decimal? RealizadoGeral, decimal? DesvioPp, string FonteGeral, string FontePrevisto, string FonteRealizado,
    IReadOnlyList<Avanco4DPonto> Timeline, IReadOnlyList<Avanco4DDisciplina> PorDisciplina, IReadOnlyList<Avanco4DTarefa> Tarefas,
    Avanco4DKpis Kpis, Avanco4DDiagnostico Diagnostico);

/// <summary>
/// Avanço 4D = % físico PREVISTO × REALIZADO do Cronograma, por tarefa/disciplina e (Tipo 2) por leitura no tempo. Somente leitura sobre
/// o que o motor homologado já gravou; nada é recalculado (gates, WBS e datas intocados). Só existe Avanço 4D quando as duas fontes
/// estão preenchidas: Realizado ZERO em todas as tarefas é indistinguível de campo não preenchido e vira SEM DADO (nunca 0% falso);
/// 0% de uma tarefa só é "confirmado" quando o projeto tem Realizado &gt; 0 em outras tarefas.
/// </summary>
public static class PmoAvanco4DRules
{
    public const decimal CoberturaMinimaPct = 50m;
    private static bool Valido(decimal? v) => v is >= 0 and <= 100;
    private static DateTime? Data(DateTime? v) => v.HasValue && v.Value.Year >= 2000 ? v.Value.Date : null;
    private static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static string Chave(string? d) => string.IsNullOrWhiteSpace(d) ? "SEM DISCIPLINA" : d.Trim().ToUpperInvariant();

    public static Avanco4DReport Build(string projectId, int tipo, DateTime referencia, DateTime? corte, IReadOnlyList<Avanco4DTarefaFonte> tarefas,
        IReadOnlyList<Avanco4DLeitura> leituras, string fontePrevisto, string fonteRealizado)
    {
        var folhas = tarefas.Where(t => !t.Resumo).ToArray();
        bool prevPreenchido = folhas.Any(t => Valido(t.Previsto) && t.Previsto > 0);
        bool realPreenchido = folhas.Any(t => Valido(t.Realizado) && t.Realizado > 0);
        var corteData = corte?.Date ?? referencia.Date;
        int invalidos = folhas.Count(t => (t.Previsto.HasValue && !Valido(t.Previsto)) || (t.Realizado.HasValue && !Valido(t.Realizado)));
        var linhas = folhas.Select(t =>
        {
            decimal? prev = Valido(t.Previsto) ? t.Previsto : null;
            decimal? real = realPreenchido && Valido(t.Realizado) ? t.Realizado : null;
            string sitReal = real is null ? "SEM_DADO" : real == 0 ? "ZERO_CONFIRMADO" : "VALOR";
            decimal? desvio = prev.HasValue && real.HasValue ? R2(real.Value - prev.Value) : null;
            string sit = desvio is null ? "SEM_DADO" : desvio < 0 ? "ATRASADA" : "NO_PRAZO";
            var fim = Data(t.Tendencia) ?? Data(t.Fim);
            int? dias = sit == "ATRASADA" && real < 100 && fim.HasValue && corteData > fim.Value ? (int)(corteData - fim.Value).TotalDays : null;
            return new Avanco4DTarefa(t.Wbs, t.Tarefa, t.Documento, t.Disciplina, prev, real, desvio, sitReal, sit, dias, Data(t.Inicio), Data(t.Fim), Data(t.Tendencia));
        }).ToArray();
        var consideradas = linhas.Where(l => l.Previsto.HasValue && l.Realizado.HasValue).ToArray();
        decimal cobertura = folhas.Length == 0 ? 0 : R2(100m * consideradas.Count(l => l.Previsto > 0) / folhas.Length);
        var diag = new Avanco4DDiagnostico(folhas.Length, consideradas.Length, linhas.Count(l => l.Realizado is null), linhas.Count(l => l.Previsto is null),
            folhas.Count(t => Data(t.Fim) is null && Data(t.Tendencia) is null), linhas.Count(l => l.SituacaoRealizado == "ZERO_CONFIRMADO"), invalidos,
            cobertura, CoberturaMinimaPct, prevPreenchido, realPreenchido);
        string? motivo = folhas.Length == 0 ? "Projeto sem tarefas-folha no cronograma."
            : !realPreenchido ? "Avanço 4D não disponível para este projeto com as fontes atuais: % Realizado do Cronograma é 0 (ou vazio) em todas as tarefas — indistinguível de campo não preenchido; tratado como SEM DADO DE AVANÇO REAL."
            : !prevPreenchido ? "Avanço 4D não disponível para este projeto com as fontes atuais: % Previsto do Cronograma não está preenchido (0 ou vazio em todas as tarefas)."
            : cobertura < CoberturaMinimaPct ? $"Avanço 4D não disponível para este projeto com as fontes atuais: apenas {cobertura:0.##}% das tarefas-folha têm Previsto > 0 e Realizado conhecido (mínimo {CoberturaMinimaPct:0}%)."
            : null;
        if (motivo is not null)
            return new(projectId, tipo, false, motivo, referencia.Date, corte, null, null, null, "—", fontePrevisto, fonteRealizado, [], [], [],
                new(folhas.Length, 0, 0, folhas.Length), diag);

        var pontos = leituras.Where(l => Valido(l.Previsto) || Valido(l.Realizado)).GroupBy(l => l.Leitura.Date)
            .Select(g => g.OrderBy(x => x.Leitura).Last())
            .OrderBy(l => l.Leitura).Select(l => new Avanco4DPonto(l.Leitura.Date, Valido(l.Previsto) ? R2(l.Previsto!.Value) : null,
                Valido(l.Realizado) ? R2(l.Realizado!.Value) : null)).ToArray();
        var ultimaRaiz = leituras.OrderBy(l => l.Leitura).LastOrDefault(l => Valido(l.Previsto) && Valido(l.Realizado));
        decimal geralPrev, geralReal; string fonteGeral;
        if (ultimaRaiz is not null)
        {
            geralPrev = R2(ultimaRaiz.Previsto!.Value); geralReal = R2(ultimaRaiz.Realizado!.Value);
            fonteGeral = $"Resumo do projeto no Cronograma (leitura {ultimaRaiz.Leitura:dd/MM/yyyy HH:mm})";
        }
        else
        {
            geralPrev = R2(consideradas.Average(l => l.Previsto!.Value)); geralReal = R2(consideradas.Average(l => l.Realizado!.Value));
            fonteGeral = "Média simples das tarefas-folha consideradas";
        }
        var porDisc = consideradas.GroupBy(l => Chave(l.Disciplina)).Select(g =>
        {
            var p = R2(g.Average(x => x.Previsto!.Value)); var r = R2(g.Average(x => x.Realizado!.Value));
            return new Avanco4DDisciplina(g.Key, p, r, R2(r - p), g.Count());
        }).OrderBy(d => d.DesvioPp).ThenBy(d => d.Disciplina).ToArray();
        var ordenadas = linhas.OrderBy(l => l.DesvioPp ?? decimal.MaxValue).ThenBy(l => l.Wbs, StringComparer.Ordinal).ToArray();
        return new(projectId, tipo, true, null, referencia.Date, corte, geralPrev, geralReal, R2(geralReal - geralPrev), fonteGeral, fontePrevisto, fonteRealizado,
            pontos, porDisc, ordenadas,
            new(linhas.Length, linhas.Count(l => l.Situacao == "NO_PRAZO"), linhas.Count(l => l.Situacao == "ATRASADA"), linhas.Count(l => l.Situacao == "SEM_DADO")), diag);
    }
}

public interface IAvanco4DReportRepository { Task<Avanco4DReport> ReadAsync(string projectId, CancellationToken ct); }

/// <summary>Leitura pura: Tipo 2 via VW_Cronograma_Tipo2_Ultima_Versao (+ raiz por leitura em dbo.Cronograma_Projetos); Tipo 1 via dbo.Cronograma_Projetos.</summary>
public sealed class Avanco4DReportRepository(IConfiguration configuration) : IAvanco4DReportRepository
{
    public async Task<Avanco4DReport> ReadAsync(string projectId, CancellationToken ct)
    {
        using var conn = new SqlConnection(configuration.GetConnectionString("LocalConnection") ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));
        await conn.OpenAsync(ct);
        using var typeCommand = new SqlCommand("SELECT Tipo_LDP FROM dbo.Projetos WHERE Project_ID_ACC=@projectId AND Is_Ativo=1", conn);
        typeCommand.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        var tipo = Convert.ToInt32(await typeCommand.ExecuteScalarAsync(ct));
        if (tipo is not (1 or 2)) throw new InvalidOperationException("Tipo_LDP indisponível.");
        var referencia = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "E. South America Standard Time").Date;
        string sql = tipo == 2
            ? """
              SELECT EDT_WBS, TASK_NAME, NA_DOCUMENT_NUMBER, Disciplina_Cronograma, Percent_Previsto_Reprogramado, Percent_Realizado,
                     PLANNED_DATE_START, Termino_Linha_Base, Termino, CAST(ISNULL(Eh_Tarefa_Resumo,0) AS bit), Data_Leitura_Cronograma
              FROM dbo.VW_Cronograma_Tipo2_Ultima_Versao WHERE Project_ID_ACC=@projectId AND Tipo_LDP=2;
              SELECT Data_Leitura_Cronograma, Percent_Previsto_Reprogramado, Percent_Realizado FROM (
                  SELECT Data_Leitura_Cronograma, Percent_Previsto_Reprogramado, Percent_Realizado,
                         ROW_NUMBER() OVER (PARTITION BY Data_Leitura_Cronograma ORDER BY LEN(EDT_WBS), EDT_WBS) rn
                  FROM dbo.Cronograma_Projetos
                  WHERE Project_ID_ACC=@projectId AND Tipo_LDP=2 AND ISNULL(Eh_Tarefa_Resumo,0)=1 AND Data_Leitura_Cronograma IS NOT NULL) x
              WHERE rn=1 ORDER BY Data_Leitura_Cronograma;
              """
            : $"""
              SELECT EDT_WBS, TASK_NAME, NA_DOCUMENT_NUMBER, Disciplina_Cronograma, PERCENT_PREDICTED_PHYSICAL, PERCENT_PHYSICAL_COMPLETED,
                     PLANNED_DATE_START, PLANNED_DATA_BASELINE, REPLANNED_DATE, CAST(ISNULL(Eh_Tarefa_Resumo,0) AS bit), Data_Sincronizacao
              FROM dbo.Cronograma_Projetos WHERE Project_ID_ACC=@projectId AND Tipo_LDP=1 {APS.WebApp.Services.Planning.PmoPlanningFactsProvider.CurrentTipo1Predicate};
              """;
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 45 };
        cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        using var reader = await cmd.ExecuteReaderAsync(ct);
        string? T(int i) => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));
        decimal? N(int i) => reader.IsDBNull(i) ? null : Convert.ToDecimal(reader.GetValue(i));
        DateTime? D(int i) => reader.IsDBNull(i) ? null : Convert.ToDateTime(reader.GetValue(i));
        var tarefas = new List<Avanco4DTarefaFonte>(); DateTime? corte = null;
        while (await reader.ReadAsync(ct))
        {
            tarefas.Add(new(T(0), T(1), T(2), T(3), N(4), N(5), D(6), D(7), D(8), reader.GetBoolean(9)));
            if (D(10) is { } leitura && (corte is null || leitura > corte)) corte = leitura;
        }
        var leituras = new List<Avanco4DLeitura>();
        if (tipo == 2 && await reader.NextResultAsync(ct))
            while (await reader.ReadAsync(ct)) leituras.Add(new(D(0)!.Value, N(1), N(2)));
        return PmoAvanco4DRules.Build(projectId, tipo, referencia, corte, tarefas, leituras,
            tipo == 2 ? "Cronograma Tipo 2 — Percent_Previsto_Reprogramado (última versão lida pelo motor homologado)" : "Cronograma Tipo 1 — PERCENT_PREDICTED_PHYSICAL",
            tipo == 2 ? "Cronograma Tipo 2 — Percent_Realizado (última versão)" : "Cronograma Tipo 1 — PERCENT_PHYSICAL_COMPLETED");
    }
}
