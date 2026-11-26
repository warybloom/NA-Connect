using System.Data;
using APS.WebApp.Services.Planning;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.DocumentQuality;

public sealed record InformationQualityDetail(string Label, string Value);

public sealed record InformationQualityFinding(
    string Id,
    string Severity,
    string Category,
    string Rule,
    string Title,
    string Description,
    string? DocumentCode,
    string? Discipline,
    decimal? LdpHours,
    decimal? TimesheetHours,
    bool DecisionRequired,
    IReadOnlyList<InformationQualityDetail> Details);

public sealed record InformationQualitySummary(
    int Critical,
    int Warning,
    int Info,
    int Total,
    int LdpRows,
    int LdpDocuments,
    int TimesheetDocuments,
    decimal LdpActualHours,
    decimal TimesheetActualHours,
    decimal DifferenceHours);

public sealed record InformationQualityAuditResult(
    string ProjectId,
    DateTime GeneratedAtUtc,
    InformationQualitySummary Summary,
    IReadOnlyList<InformationQualityFinding> Findings);

internal sealed record InformationQualityLdpRow(
    string Code,
    string? ClientCode,
    string? Title,
    string? Discipline,
    string? Area,
    string? Activity,
    string? DocType,
    string? Seq,
    decimal PlannedHours,
    decimal ActualHours,
    decimal? PlannedPhysical,
    decimal? ActualPhysical,
    string? DocumentStatus,
    string? Situation);

internal sealed record InformationQualityTimesheetRow(string Code, string? Discipline, decimal ActualHours);

internal sealed record InformationQualityScheduleRow(
    string? Code,
    string? TaskName,
    decimal? ActualPhysical,
    DateTime SyncedAt);

public sealed class InformationQualityAuditService(
    IConfiguration configuration,
    InformationQualityRedistributionService redistributionService,
    InformationQualityProgressService progressService)
{
    private const decimal HourTolerance = 0.005m;
    private const decimal ScheduleTolerance = 0.005m;

    public async Task<InformationQualityAuditResult> ReadAsync(string projectId, CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão LocalConnection indisponível.");

        var ldp = new List<InformationQualityLdpRow>();
        var timesheet = new List<InformationQualityTimesheetRow>();
        var schedule = new List<InformationQualityScheduleRow>();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            SELECT
                UPPER(LTRIM(RTRIM(NA_Document_Number))) AS Codigo,
                NULLIF(LTRIM(RTRIM(Client_Document_Number)), '') AS ClientCode,
                NULLIF(LTRIM(RTRIM(Document_Title)), '') AS Titulo,
                NULLIF(LTRIM(RTRIM(Discipline)), '') AS Disciplina,
                NULLIF(LTRIM(RTRIM(Area)), '') AS Area,
                NULLIF(LTRIM(RTRIM(Discipline_Activity)), '') AS Activity,
                NULLIF(LTRIM(RTRIM(Doc_Type_Code)), '') AS DocType,
                NULLIF(LTRIM(RTRIM(Seq)), '') AS Seq,
                ISNULL(Planned_HH, 0) AS PlannedHH,
                ISNULL(Actual_HH, 0) AS ActualHH,
                Percent_Planned_Physical,
                Percent_Physical_Completed,
                NULLIF(LTRIM(RTRIM(Document_Status)), '') AS DocumentStatus,
                NULLIF(LTRIM(RTRIM(Situation)), '') AS Situation
            FROM {PmoLdpSnapshot.Tabela}
            WHERE NULLIF(LTRIM(RTRIM(NA_Document_Number)), '') IS NOT NULL
            {PmoLdpSnapshot.OpcaoPlano};

            SELECT
                UPPER(LTRIM(RTRIM(DocumentoCodigo))) AS Codigo,
                MAX(COALESCE(NULLIF(LTRIM(RTRIM(Disciplina_PT_Padrao)), ''), NULLIF(LTRIM(RTRIM(Disciplina)), ''))) AS Disciplina,
                SUM(ISNULL(Trabalhadas, 0)) AS ActualHH
            FROM dbo.VW_TimeSheet_NA_Corrigida_Projeto
            WHERE Project_ID_ACC_Canonico = @projectId
              AND Status_Mapeamento_CR_OS IN ('UNICO', 'ATIVO_UNICO')
              AND NULLIF(LTRIM(RTRIM(DocumentoCodigo)), '') IS NOT NULL
            GROUP BY UPPER(LTRIM(RTRIM(DocumentoCodigo)));

            ;WITH UltimaCargaCronograma AS (
                SELECT MAX(Data_Sincronizacao) AS MaxSync
                FROM dbo.Cronograma_Projetos
                WHERE Project_ID_ACC = @projectId
            )
            SELECT
                UPPER(LTRIM(RTRIM(COALESCE(
                    NULLIF(NA_Document_Number_Normalizado, ''),
                    NULLIF(NA_DOCUMENT_NUMBER, '')
                )))) AS Codigo,
                NULLIF(LTRIM(RTRIM(TASK_NAME)), '') AS TaskName,
                COALESCE(Percent_Realizado, PERCENT_PHYSICAL_COMPLETED) AS ActualPhysical,
                Data_Sincronizacao
            FROM dbo.Cronograma_Projetos c
            CROSS JOIN UltimaCargaCronograma u
            WHERE c.Project_ID_ACC = @projectId
              AND u.MaxSync IS NOT NULL
              AND c.Data_Sincronizacao = u.MaxSync;
            """;

        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 45 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var code = Text(reader, 0);
            if (string.IsNullOrWhiteSpace(code) || code == "----") continue;
            ldp.Add(new(
                code,
                Text(reader, 1),
                Text(reader, 2),
                Text(reader, 3),
                Text(reader, 4),
                Text(reader, 5),
                Text(reader, 6),
                Text(reader, 7),
                Decimal(reader, 8) ?? 0,
                Decimal(reader, 9) ?? 0,
                Decimal(reader, 10),
                Decimal(reader, 11),
                Text(reader, 12),
                Text(reader, 13)));
        }

        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var code = Text(reader, 0);
            if (string.IsNullOrWhiteSpace(code) || code == "----") continue;
            timesheet.Add(new(code, Text(reader, 1), Decimal(reader, 2) ?? 0));
        }

        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            schedule.Add(new(
                Text(reader, 0),
                Text(reader, 1),
                Decimal(reader, 2),
                reader.GetDateTime(3)));
        }

        var findings = BuildFindings(ldp, timesheet, schedule);
        var redistributions = await redistributionService.ListAsync(projectId, ct);
        ApplyRedistributions(findings, redistributions);
        await progressService.ApplyAuditFindingsAsync(projectId, ldp, findings, ct);
        var ldpByCode = ldp.GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToList();

        var critical = findings.Count(x => x.Severity == "CRITICO");
        var warning = findings.Count(x => x.Severity == "ATENCAO");
        var info = findings.Count(x => x.Severity == "INFORMATIVO");
        var ldpHours = ldp.Sum(x => x.ActualHours);
        var tsHours = timesheet.Sum(x => x.ActualHours);

        return new(
            projectId,
            DateTime.UtcNow,
            new(
                critical,
                warning,
                info,
                findings.Count,
                ldp.Count,
                ldpByCode.Count,
                timesheet.Count,
                ldpHours,
                tsHours,
                ldpHours - tsHours),
            findings);
    }

    internal static void ApplyRedistributions(
        List<InformationQualityFinding> findings,
        IReadOnlyList<RedistributionDecision> redistributions)
    {
        if (redistributions.Count == 0) return;

        // Mesma chave do frontend (confirmedRedistributionCodes: trim + maiúsculas): backend e tela sempre concordam sobre o que já foi tratado.
        static string Key(string? code) => (code ?? "").Trim().ToUpperInvariant();
        var active = redistributions
            .Where(x => string.Equals(x.Status?.Trim(), "CONFIRMADO", StringComparison.OrdinalIgnoreCase) && Key(x.SourceDocumentCode).Length > 0)
            .GroupBy(x => Key(x.SourceDocumentCode), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.ConfirmedAtUtc).First(), StringComparer.Ordinal);

        if (active.Count == 0) return;

        findings.RemoveAll(f =>
            active.ContainsKey(Key(f.DocumentCode))
            && f.Rule is "TIMESHEET_SEM_LDP" or "DOCUMENTO_RETIRADO_COM_HH" or "HH_LDP_TIMESHEET_DIVERGENTE");

        foreach (var decision in active.Values.OrderBy(x => x.SourceDocumentCode, StringComparer.OrdinalIgnoreCase))
        {
            var details = new List<InformationQualityDetail>
            {
                new("HH origem", $"{decision.SourceHours:0.######}"),
                new("Custo origem", $"R$ {decision.SourceCost:N2}"),
                new("Confirmado por", decision.ConfirmedBy),
                new("Confirmado em", decision.ConfirmedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))
            };

            foreach (var item in decision.Items.OrderBy(x => x.DocumentCode, StringComparer.OrdinalIgnoreCase))
            {
                details.Add(new(
                    item.DocumentCode,
                    $"{item.Hours:0.######} h · R$ {item.Cost:N2} · peso {(item.Weight * 100):0.####}%"));
            }

            var atividadeAt = decision.Items.Count == 0;
            findings.Add(new(
                $"REDISTRIBUICAO:{decision.Id}",
                "INFORMATIVO",
                "TIMESHEET",
                "REDISTRIBUICAO_CONFIRMADA",
                atividadeAt ? "HH e custo classificados como atividade AT" : "HH e custo redistribuídos",
                atividadeAt
                    ? $"O lançamento {decision.SourceDocumentCode} foi tratado como atividade sem documento na camada gerencial. " +
                      $"{decision.SourceHours:0.##} h e R$ {decision.SourceCost:N2} permanecem no projeto e foram atribuídos à disciplina {decision.Discipline}."
                    : $"O documento {decision.SourceDocumentCode} foi tratado como cancelado na camada gerencial. " +
                      $"Foram redistribuídas {decision.SourceHours:0.##} h e R$ {decision.SourceCost:N2} entre {decision.Items.Count} documento(s) ativo(s) da disciplina {decision.Discipline}.",
                decision.SourceDocumentCode,
                decision.Discipline,
                null,
                decision.SourceHours,
                false,
                details));
        }
    }

    internal static List<InformationQualityFinding> BuildFindings(
        IReadOnlyList<InformationQualityLdpRow> ldp,
        IReadOnlyList<InformationQualityTimesheetRow> timesheet,
        IReadOnlyList<InformationQualityScheduleRow> schedule)
    {
        var findings = new List<InformationQualityFinding>();
        var ldpGroups = ldp.GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var tsGroups = timesheet
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => new InformationQualityTimesheetRow(
                    x.Key,
                    x.Select(y => y.Discipline).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)),
                    x.Sum(y => y.ActualHours)),
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in ldpGroups.Where(x => x.Value.Count > 1).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var differences = DescribeDuplicateDifferences(group.Value);
            var details = group.Value.Select((row, index) =>
                new InformationQualityDetail(
                    $"Ocorrência {index + 1}",
                    $"Diferenças no grupo: {differences} | Disciplina: {row.Discipline ?? "Sem disciplina"} | Cliente: {row.ClientCode ?? "—"} | Título: {row.Title ?? "Sem título"} | Status: {row.DocumentStatus ?? "—"} | Situação: {row.Situation ?? "—"} | HH: {row.ActualHours:0.##}"))
                .ToList();

            findings.Add(new(
                $"NA_DUP:{group.Key}",
                "CRITICO",
                "DOCUMENTAL",
                "NA_DOCUMENT_DUPLICADO",
                "N&A Document Number duplicado",
                $"O número do documento {group.Key} aparece {group.Value.Count} vezes na LDP atual. Há mais de um registro usando a mesma identidade documental; o sistema não escolhe qual está correto.",
                group.Key,
                group.Value.Select(x => x.Discipline).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                group.Value.Sum(x => x.ActualHours),
                tsGroups.TryGetValue(group.Key, out var ts) ? ts.ActualHours : null,
                true,
                details));
        }

        var clientGroups = ldp.Where(x => !string.IsNullOrWhiteSpace(x.ClientCode))
            .GroupBy(x => x.ClientCode!, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Select(y => y.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .OrderBy(x => x.Key, StringComparer.Ordinal);

        foreach (var group in clientGroups)
        {
            var codes = group.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            findings.Add(new(
                $"CLIENT_DUP:{group.Key}",
                "CRITICO",
                "DOCUMENTAL",
                "CLIENT_DOCUMENT_DUPLICADO",
                "Client Document Number duplicado",
                $"O número de documento do cliente {group.Key} está associado a {codes.Count} números de documento N&A diferentes.",
                codes[0],
                group.Select(x => x.Discipline).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                null,
                null,
                true,
                codes.Select((code, i) => new InformationQualityDetail($"Número do documento N&A {i + 1}", code)).ToList()));
        }

        foreach (var ts in tsGroups.Values.OrderByDescending(x => x.ActualHours).ThenBy(x => x.Code, StringComparer.Ordinal))
        {
            if (ldpGroups.ContainsKey(ts.Code)) continue;
            findings.Add(new(
                $"TS_ONLY:{ts.Code}",
                "ATENCAO",
                "TIMESHEET",
                "TIMESHEET_SEM_LDP",
                "Documento do Timesheet não existe na LDP",
                $"Existem {ts.ActualHours:0.##} h lançadas neste número de documento, mas ele não está presente na LDP atual. A decisão deve ser do responsável: reinserir, ignorar ou tratar por De/Para.",
                ts.Code,
                ts.Discipline,
                0,
                ts.ActualHours,
                true,
                []));
        }

        foreach (var pair in ldpGroups.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var ldpHours = pair.Value.Sum(x => x.ActualHours);

            if (!tsGroups.TryGetValue(pair.Key, out var ts))
            {
                if (Math.Abs(ldpHours) > HourTolerance)
                {
                    findings.Add(new(
                        $"LDP_ONLY_HH:{pair.Key}",
                        "ATENCAO",
                        "HH",
                        "LDP_SEM_TIMESHEET_COM_HH",
                        "LDP possui HH realizado sem Timesheet atual",
                        $"A LDP possui {ldpHours:0.##} h realizadas neste número de documento, mas não existe lançamento correspondente no Timesheet atual. Isso pode indicar cache antigo, fórmula residual ou mudança documental e requer validação do responsável.",
                        pair.Key,
                        pair.Value.Select(x => x.Discipline).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                        ldpHours,
                        null,
                        true,
                        []));
                }
            }
            else
            {
                var difference = ldpHours - ts.ActualHours;
                if (Math.Abs(difference) > HourTolerance)
                {
                    findings.Add(new(
                        $"HH_DIFF:{pair.Key}",
                        "ATENCAO",
                        "HH",
                        "HH_LDP_TIMESHEET_DIVERGENTE",
                        "HH LDP diferente do Timesheet",
                        $"LDP={ldpHours:0.##} h · Timesheet={ts.ActualHours:0.##} h · diferença={difference:+0.##;-0.##;0} h.",
                        pair.Key,
                        ts.Discipline ?? pair.Value.Select(x => x.Discipline).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                        ldpHours,
                        ts.ActualHours,
                        true,
                        []));
                }
            }

            var retired = pair.Value.Any(x =>
            {
                var status = x.DocumentStatus?.Trim();
                return string.Equals(status, "Documento retirado da ldp", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(status, "CANCELADO", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
            });
            var tsHours = tsGroups.TryGetValue(pair.Key, out var tsForRetired) ? tsForRetired.ActualHours : 0m;
            if (retired && Math.Abs(tsHours) > HourTolerance)
            {
                var situations = pair.Value.Select(x => x.Situation).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                findings.Add(new(
                    $"RETIRED_HH:{pair.Key}",
                    "ATENCAO",
                    "DOCUMENTAL",
                    "DOCUMENTO_RETIRADO_COM_HH",
                    "Documento cancelado/retirado possui HH no Timesheet",
                    $"O documento está cancelado ou retirado da LDP e ainda possui horas realizadas no Timesheet atual. O sistema sinaliza para redistribuição gerencial mediante confirmação do responsável.",
                    pair.Key,
                    pair.Value.Select(x => x.Discipline).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                    ldpHours,
                    tsHours,
                    true,
                    [
                        new("HH LDP", $"{ldpHours:0.##}"),
                        new("HH Timesheet", $"{tsHours:0.##}"),
                        new("Situação", situations.Count == 0 ? "—" : string.Join(" | ", situations))
                    ]));
            }
            else if (retired && Math.Abs(ldpHours) > HourTolerance)
            {
                var situations = pair.Value.Select(x => x.Situation).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                findings.Add(new(
                    $"RETIRED_LDP_RESIDUAL:{pair.Key}",
                    "ATENCAO",
                    "LDP",
                    "LDP_HH_RESIDUAL_SEM_TIMESHEET",
                    "Documento cancelado/retirado possui HH residual apenas na LDP",
                    $"A LDP ainda possui {ldpHours:0.##} h realizadas neste documento cancelado/retirado, mas o Timesheet atual possui 0 h. Não há HH realizado do Timesheet para redistribuir; trate este item como inconsistência residual da LDP.",
                    pair.Key,
                    pair.Value.Select(x => x.Discipline).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                    ldpHours,
                    0m,
                    false,
                    [
                        new("HH LDP", $"{ldpHours:0.##}"),
                        new("HH Timesheet", "0"),
                        new("Situação", situations.Count == 0 ? "—" : string.Join(" | ", situations))
                    ]));
            }
        }

        foreach (var row in ldp.OrderBy(x => x.Code, StringComparer.Ordinal))
        {
            var parts = row.Code.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length >= 5)
            {
                var expected = parts[^4..];
                var current = new[] { row.Area ?? "", row.Activity ?? "", row.DocType ?? "", row.Seq ?? "" };
                if (!expected.SequenceEqual(current, StringComparer.OrdinalIgnoreCase))
                {
                    findings.Add(new(
                        $"DERIVED:{row.Code}:{row.ClientCode}",
                        "ATENCAO",
                        "LDP",
                        "CAMPOS_DERIVADOS_DIVERGENTES",
                        "Campos derivados não correspondem ao número do documento",
                        $"O código do documento indica {string.Join(" / ", expected)}, mas a LDP contém {string.Join(" / ", current)}.",
                        row.Code,
                        row.Discipline,
                        row.ActualHours,
                        tsGroups.TryGetValue(row.Code, out var ts) ? ts.ActualHours : null,
                        true,
                        []));
                }

                var invalidPart = expected.FirstOrDefault(x => x.Any(ch => !char.IsLetterOrDigit(ch)));
                if (!string.IsNullOrWhiteSpace(invalidPart))
                {
                    findings.Add(new(
                        $"DOCUMENTO_FORMATO:{row.Code}",
                        "ATENCAO",
                        "DOCUMENTAL",
                        "DOCUMENTO_COMPONENTE_SUSPEITO",
                        "Código do documento com componente suspeito",
                        $"O código do documento contém o componente “{invalidPart}”, com caractere fora do padrão alfanumérico. Nenhuma correção automática será feita.",
                        row.Code,
                        row.Discipline,
                        row.ActualHours,
                        tsGroups.TryGetValue(row.Code, out var ts) ? ts.ActualHours : null,
                        true,
                        []));
                }
            }

            if (!string.Equals(row.DocType, "AT", StringComparison.OrdinalIgnoreCase)
                && ((row.PlannedPhysical is < 0 or > 100) || (row.ActualPhysical is < 0 or > 100)))
            {
                findings.Add(new(
                    $"PHYSICAL:{row.Code}:{row.ClientCode}",
                    "ATENCAO",
                    "PROGRESSO",
                    "PERCENTUAL_FORA_FAIXA",
                    "Percentual físico fora da faixa",
                    $"Planejado físico={FormatPercent(row.PlannedPhysical)} · realizado físico={FormatPercent(row.ActualPhysical)}. O sistema apenas sinaliza; a fonte deve ser conferida.",
                    row.Code,
                    row.Discipline,
                    row.ActualHours,
                    tsGroups.TryGetValue(row.Code, out var ts) ? ts.ActualHours : null,
                    true,
                    []));
            }
        }

        AddScheduleFindings(findings, ldpGroups, schedule);

        if (findings.Count == 0)
        {
            findings.Add(new(
                "OK",
                "INFORMATIVO",
                "GERAL",
                "SEM_INCONSISTENCIAS",
                "Nenhuma inconsistência detectada",
                "As regras atualmente cadastradas não encontraram divergências no snapshot atual. Isso não substitui auditorias específicas de ACC ou conteúdo documental.",
                null,
                null,
                ldp.Sum(x => x.ActualHours),
                timesheet.Sum(x => x.ActualHours),
                false,
                []));
        }

        return findings
            .OrderBy(x => SeverityOrder(x.Severity))
            .ThenBy(x => x.Category, StringComparer.Ordinal)
            .ThenBy(x => x.DocumentCode, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddScheduleFindings(
        ICollection<InformationQualityFinding> findings,
        IReadOnlyDictionary<string, List<InformationQualityLdpRow>> ldpGroups,
        IReadOnlyList<InformationQualityScheduleRow> schedule)
    {
        if (schedule.Count == 0)
        {
            findings.Add(new(
                "SCHEDULE:MISSING",
                "ATENCAO",
                "CRONOGRAMA",
                "CRONOGRAMA_AUSENTE",
                "Cronograma não encontrado",
                "Não há snapshot atual de cronograma para o projeto. A auditoria apenas informa a ausência; nenhuma informação de planejamento é criada automaticamente.",
                null,
                null,
                null,
                null,
                false,
                []));
            return;
        }

        var syncedAt = schedule.Max(x => x.SyncedAt);
        var withDocument = schedule.Where(x => !string.IsNullOrWhiteSpace(x.Code)).ToList();
        var uniqueDocumentCodes = withDocument
            .Select(x => x.Code!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matchedDocuments = uniqueDocumentCodes.Count(code => ldpGroups.ContainsKey(code));
        var taskCoverage = schedule.Count == 0 ? 0m : (decimal)withDocument.Count / schedule.Count;
        var matchCoverage = uniqueDocumentCodes.Count == 0 ? 0m : (decimal)matchedDocuments / uniqueDocumentCodes.Count;

        var insufficientCoverage =
            (schedule.Count >= 20 && taskCoverage < 0.05m)
            || (uniqueDocumentCodes.Count >= 5 && matchCoverage < 0.20m);

        if (insufficientCoverage)
        {
            findings.Add(new(
                $"SCHEDULE:COVERAGE:{syncedAt:O}",
                "ATENCAO",
                "CRONOGRAMA",
                "CRONOGRAMA_COBERTURA_INSUFICIENTE",
                "Cobertura documental do cronograma insuficiente",
                "O cronograma foi lido, mas a quantidade de tarefas associadas a documentos ou de documentos correspondentes à LDP é baixa demais para sustentar indicadores confiáveis.",
                null,
                null,
                null,
                null,
                false,
                [
                    new("Tarefas", schedule.Count.ToString()),
                    new("Tarefas com documento", withDocument.Count.ToString()),
                    new("Documentos únicos", uniqueDocumentCodes.Count.ToString()),
                    new("Documentos encontrados na LDP", matchedDocuments.ToString()),
                    new("Cobertura tarefas/documento", $"{taskCoverage:P1}"),
                    new("Cobertura cronograma/LDP", $"{matchCoverage:P1}"),
                    new("Sincronização", syncedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"))
                ]));
        }

        foreach (var group in withDocument
            .Where(x => x.ActualPhysical.HasValue)
            .GroupBy(x => x.Code!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var values = group
                .Select(x => x.ActualPhysical!.Value)
                .OrderBy(x => x)
                .ToList();

            if (values.Count < 2 || values[^1] - values[0] <= ScheduleTolerance) continue;

            findings.Add(new(
                $"SCHEDULE:CONFLICT:{group.Key}",
                "CRITICO",
                "CRONOGRAMA",
                "CRONOGRAMA_REALIZADO_CONFLITANTE",
                "Cronograma possui realizado conflitante para o mesmo documento",
                $"O mesmo número do documento aparece no snapshot atual do cronograma com valores de realizado diferentes ({values[0]:0.##}% a {values[^1]:0.##}%). O sistema não escolhe qual valor é correto.",
                group.Key,
                ldpGroups.TryGetValue(group.Key, out var ldpRows)
                    ? ldpRows.Select(x => x.Discipline).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                    : null,
                null,
                null,
                true,
                group
                    .OrderBy(x => x.ActualPhysical)
                    .Take(12)
                    .Select((x, index) => new InformationQualityDetail(
                        $"Tarefa {index + 1}",
                        $"{x.TaskName ?? "Sem nome"} | Realizado: {x.ActualPhysical:0.##}%"))
                    .ToList()));
        }
    }

    private static int SeverityOrder(string severity) => severity switch
    {
        "CRITICO" => 0,
        "ATENCAO" => 1,
        _ => 2,
    };

    private static string DescribeDuplicateDifferences(IReadOnlyList<InformationQualityLdpRow> rows)
    {
        var differences = new List<string>();
        AddDifference(differences, "cliente", rows.Select(x => x.ClientCode));
        AddDifference(differences, "título", rows.Select(x => x.Title));
        AddDifference(differences, "disciplina", rows.Select(x => x.Discipline));
        AddDifference(differences, "status", rows.Select(x => x.DocumentStatus));
        AddDifference(differences, "situação", rows.Select(x => x.Situation));
        AddDifference(differences, "HH", rows.Select(x => x.ActualHours.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

        return differences.Count == 0 ? "sem diferenças nos campos auditados" : string.Join(", ", differences);
    }

    private static void AddDifference(ICollection<string> differences, string label, IEnumerable<string?> values)
    {
        if (values.Select(x => x?.Trim() ?? "—").Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            differences.Add(label);
    }

    private static string FormatPercent(decimal? value) => value.HasValue ? $"{value.Value:0.##}%" : "—";
    private static string? Text(SqlDataReader reader, int index) => reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index))?.Trim();
    private static decimal? Decimal(SqlDataReader reader, int index) => reader.IsDBNull(index) ? null : Convert.ToDecimal(reader.GetValue(index));
}
