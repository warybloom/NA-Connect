using System.Text.RegularExpressions;
using APS.WebApp.Models;
using APS.WebApp.Services.CronogramaTipo2;
using NA_Connect.Services.Ldp;
using OfficeOpenXml;

namespace APS.WebApp.Services.LdpTipo2;

/// <summary>Espelho Tipo 2: LDP atual em memória, uma linha por documento, no modelo existente.</summary>
public sealed class LdpTipo2EspelhoService(
    AutodeskApiService api, ILdpTipo2Parser parser,
    LdpConfigDatabaseService templates, LdpArquivoStorageService storage,
    ILogger<LdpTipo2EspelhoService> logger)
{
    public async Task<string> GerarAsync(string hubId, string projectId, string token, string ldpFileId,
        CancellationToken ct = default)
    {
        var source = await api.GetItemDetailsAsync(projectId, ldpFileId, token)
            ?? throw new InvalidOperationException("LDP Tipo 2 não encontrada.");
        string folder = null;
        foreach (var path in new[] {
            new[] { "01 - PLANEJAMENTO E CONTROLE", "01.06 - LISTA DE DOCUMENTOS PROJETO" },
            new[] { "01 - PLANNING & CONTROL", "01.06 - MASTER DOCUMENT LIST" } })
        {
            folder = await api.FindFolderIdByPathAsync(hubId, projectId, path, token);
            if (!string.IsNullOrEmpty(folder)) break;
        }
        if (string.IsNullOrEmpty(folder)) throw new InvalidOperationException("Pasta 01.06 não encontrada.");
        var contents = await api.GetFolderContentsAllPagesAsync(projectId, folder, token, ct);
        var destination = ResolverDestinoEspelho(contents, source.DisplayName);
        if (destination.Status == Tipo2EspelhoDestinoStatus.Ambiguo)
        {
            var candidatos = string.Join(" | ", destination.Candidatos);
            logger.LogError("[ETAPA 10][TIPO2][ESPELHO][ERRO] Motivo=DESTINO_ESPELHO_AMBIGUO " +
                "Candidatos=[{Candidatos}] Publicacao=ABORTADA", candidatos);
            throw new InvalidOperationException(
                $"Destino do Espelho Tipo 2 ambíguo na pasta 01.06: {candidatos}. Publicação cancelada.");
        }
        if (destination.Status == Tipo2EspelhoDestinoStatus.NaoEncontrado)
        {
            var error = "Nenhum destino compatível de Espelho Tipo 2 encontrado na pasta 01.06. Publicação cancelada.";
            logger.LogError("[ETAPA 10][TIPO2][ESPELHO][ERRO] Motivo=DESTINO_ESPELHO_NAO_ENCONTRADO Publicacao=ABORTADA");
            throw new InvalidOperationException(error);
        }

        var mirror = destination.Item!;
        var mirrorId = mirror.Id;
        var mirrorName = mirror.Name;
        if (destination.Status == Tipo2EspelhoDestinoStatus.FallbackLdpExistente)
        {
            logger.LogInformation("[ETAPA 10][TIPO2][ESPELHO][FALLBACK] " +
                "PadraoSufixoXX=NAO_ENCONTRADO Pasta=01.06 ArquivoSelecionado={ArquivoSelecionado} " +
                "OrigemSelecao=FALLBACK_LDP_EXISTENTE Acao=PUBLICAR_NOVA_VERSAO_MESMO_ITEM", mirrorName);
        }
        logger.LogInformation("[ETAPA 10][TIPO2] Espelho localizado | Nome={Nome} | ItemId={ItemId}",
            mirrorName, mirrorId);
        byte[] template;
        var config = templates.GetProjetoConfiguracao(projectId);
        var model = config?.TemplateId is int templateId && config.ExcelEspelhoId is int espelhoId
            ? templates.GetEspelhos(templateId).SingleOrDefault(x => x.ExcelEspelhoId == espelhoId && x.Ativo)
            : null;
        if (!string.IsNullOrWhiteSpace(model?.ArquivoReferencia))
            template = storage.Ler(model.ArquivoReferencia);
        else
        {
            var target = await api.GetItemDetailsAsync(projectId, mirrorId, token)
                ?? throw new InvalidOperationException("Espelho Tipo 2 não encontrado.");
            using var download = await api.DownloadFileStreamAsync(projectId, target.TipVersionUrn, token);
            using var buffer = new MemoryStream();
            await download.CopyToAsync(buffer, ct);
            template = buffer.ToArray();
        }

        using var package = new ExcelPackage(new MemoryStream(template));
        var ws = package.Workbook.Worksheets["LDP"]
            ?? throw new InvalidOperationException("Modelo de Espelho Tipo 2 sem aba LDP.");
        if (ws.Dimension == null || ws.Dimension.End.Column < 23 || ws.Dimension.End.Row < 8)
            throw new InvalidOperationException("Modelo de Espelho Tipo 2 deve conter A:W, cabeçalho na linha 7 e linha de dados formatada.");

        // Usa a versão após GRD/Workflow/Cronograma, sem antecipar a persistência da Etapa 11.
        LdpTipo2Resultado final;
        using (var download = await api.DownloadFileStreamAsync(projectId, source.TipVersionUrn, token))
            final = await parser.ProcessarAsync(download, projectId, source.DisplayName, DateTime.UtcNow, ct);
        if (final.Erros.Count > 0) throw new InvalidOperationException(string.Join(" | ", final.Erros));
        var ordered = final.Documentos.OrderBy(d => d.LinhaExcel).ToList();
        if (ordered.GroupBy(d => CronogramaTipo2DocumentNumberNormalizer.Normalizar(d.NumeroDocumentoNa))
            .Any(g => g.Count() > 1))
            throw new InvalidOperationException("LDP Tipo 2 com documentos duplicados: espelho não publicado.");
        Preencher(ws, ordered);
        RemoverReferenciasExternas(package);
        using var upload = new MemoryStream(package.GetAsByteArray());
        logger.LogInformation("[ETAPA 10][TIPO2] Atualizando versão existente | Nome={Nome} | ItemId={ItemId}",
            mirrorName, mirrorId);
        await api.CreateNewVersionAsync(projectId, mirrorId, folder, mirrorName, upload, token);
        logger.LogInformation("[ETAPA 10][TIPO2] Espelho publicado: {Documentos} documentos, {Gates} gates",
            ordered.Count, ordered.Sum(d => d.Gates.Count));
        return $"Espelho Tipo 2 publicado: {ordered.Count} documentos.";
    }

    internal static Tipo2EspelhoDestinoResultado ResolverDestinoEspelho(
        IEnumerable<AccFolderItem> contents, string? sourceDisplayName)
    {
        var items = contents.Where(item =>
            string.Equals(item.Type, "items", StringComparison.OrdinalIgnoreCase)).ToList();
        var preferenciais = items.Where(item =>
            Regex.IsMatch(item.Name ?? "", @"-\d{2}\.xlsx$", RegexOptions.IgnoreCase)).ToList();

        // A convenção histórica continua sendo a preferência e mantém exatamente o comportamento anterior.
        if (preferenciais.Count == 1)
            return new(Tipo2EspelhoDestinoStatus.Preferencial, preferenciais[0], []);
        if (preferenciais.Count > 1)
            return new(Tipo2EspelhoDestinoStatus.Ambiguo, null, preferenciais.Select(item => item.Name).ToList());

        // Sem o sufixo histórico, só aceita uma LDP identificável pelo mesmo código de projeto da LDP fonte.
        // Não há busca fora da pasta 01.06 e nunca se escolhe o primeiro Excel disponível.
        var codigoProjeto = ExtrairCodigoProjetoDaLdp(sourceDisplayName);
        if (string.IsNullOrWhiteSpace(codigoProjeto))
            return new(Tipo2EspelhoDestinoStatus.NaoEncontrado, null, []);

        var padraoLdp = new Regex(
            $@"^{Regex.Escape(codigoProjeto)}-\d{{3}}-PL\d+-LD-\d{{3}}\.xlsx$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var fallback = items.Where(item => padraoLdp.IsMatch(item.Name ?? string.Empty)).ToList();
        return fallback.Count switch
        {
            1 => new(Tipo2EspelhoDestinoStatus.FallbackLdpExistente, fallback[0], []),
            > 1 => new(Tipo2EspelhoDestinoStatus.Ambiguo, null, fallback.Select(item => item.Name).ToList()),
            _ => new(Tipo2EspelhoDestinoStatus.NaoEncontrado, null, [])
        };
    }

    private static string? ExtrairCodigoProjetoDaLdp(string? fileName)
    {
        var match = Regex.Match(fileName ?? string.Empty,
            @"^(?<codigo>[A-Za-z0-9.]+)-\d{3}-PL\d+-LD-\d{2,3}\.xlsx$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["codigo"].Value : null;
    }

    internal static LdpTipo2Gate EscolherGate(IEnumerable<LdpTipo2Gate> gates)
    {
        var active = gates.Where(g => g.GateAtivo).OrderBy(g => g.GatePercentual).ToList();
        return active.FirstOrDefault(g => !Finalizado(g.DocumentStatus)) ?? active.LastOrDefault();
    }

    private static bool Finalizado(string status) => GrdLdpWriteback.Normalizar(status) is
        "APPROVED" or "APROVADO" or "COMPLETED" or "CONCLUIDO" or "FINALIZADO" or "FINALIZED" or "CLOSED" or "ENCERRADO";

    internal static void Preencher(ExcelWorksheet ws, IReadOnlyList<LdpTipo2Documento> documents)
    {
        // O parser já associa os gates a cada documento; não há matching por ID/data de SQL.
        int previousLast = ws.Dimension?.End.Row ?? 8;
        int last = Math.Max(previousLast, documents.Count + 7);
        for (int row = previousLast + 1; row <= last; row++)
        {
            ws.Cells[8, 1, 8, 23].Copy(ws.Cells[row, 1, row, 23]);
            ws.Row(row).Height = ws.Row(8).Height;
        }
        // Limpa conteúdo, inclusive fórmulas antigas, sem Clear/DeleteRow que destruiriam o modelo.
        ws.Cells[8, 1, last, 23].Formula = null;
        ws.Cells[8, 1, last, 23].Value = null;
        for (int index = 0; index < documents.Count; index++)
        {
            var doc = documents[index];
            int row = index + 8;
            ws.Cells[row, 1].Value = index + 1;
            object[] fields = { doc.ScheduleId, doc.Disciplina, doc.NumeroDocumentoNa, doc.NumeroDocumentoCliente,
                doc.TituloDocumento, doc.Formato, doc.NumeroPaginas };
            for (int col = 0; col < fields.Length; col++) ws.Cells[row, col + 2].Value = fields[col];
            var docGates = doc.Gates;
            var current = EscolherGate(docGates);
            ws.Cells[row, 9].Value = current?.PlannedBaseline;
            ws.Cells[row, 10].Value = current?.ReplannedDate;
            object[] general = { doc.EmissionDate, doc.TransmittalNumber, doc.IssuedPurpose, doc.Revision, doc.DocumentStatus };
            for (int col = 0; col < general.Length; col++) ws.Cells[row, col + 11].Value = general[col];
            ws.Cells[row, 16].Value = doc.IssuedPurpose;
            ws.Cells[row, 17].Value = doc.Revision;
            for (int indexGate = 0; indexGate < 3; indexGate++)
            {
                var gate = docGates.SingleOrDefault(g => g.GatePercentual == (indexGate + 1) * 30);
                ws.Cells[row, 18 + indexGate].Value = gate?.DocumentStatus;
                ws.Cells[row, 21 + indexGate].Value = gate?.GateAtivo == true ? "X" : null;
            }
        }
        for (int col = 1; col <= 23; col++) ws.Column(col).Hidden = col >= 10 && col <= 17;
    }

    internal static void RemoverReferenciasExternas(ExcelPackage package)
    {
        foreach (var sheet in package.Workbook.Worksheets)
        {
            if (sheet.Dimension == null) continue;
            foreach (var cell in sheet.Cells[sheet.Dimension.Address])
            {
                if (string.IsNullOrEmpty(cell.Formula) || !Regex.IsMatch(cell.Formula, @"\[\d+\]")) continue;
                // Localiza CAPA quando existir; outros vínculos ficam com seu valor já armazenado.
                if (package.Workbook.Worksheets["CAPA"] != null &&
                    Regex.IsMatch(cell.Formula, @"^=?'?\[\d+\]CAPA'?!", RegexOptions.IgnoreCase))
                    cell.Formula = Regex.Replace(cell.Formula, @"\[\d+\]", "");
                else
                {
                    var value = cell.Value;
                    cell.Formula = null;
                    cell.Value = value;
                }
            }
        }
        package.Workbook.ExternalLinks.Clear();
    }
}

internal enum Tipo2EspelhoDestinoStatus
{
    Preferencial,
    FallbackLdpExistente,
    Ambiguo,
    NaoEncontrado
}

internal sealed record Tipo2EspelhoDestinoResultado(
    Tipo2EspelhoDestinoStatus Status,
    AccFolderItem? Item,
    IReadOnlyList<string> Candidatos);
