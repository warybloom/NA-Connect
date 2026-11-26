using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.IO.Compression;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using OfficeOpenXml;
using APS.WebApp.Services;
using APS.WebApp.Services.Planning;
using APS.WebApp.Models;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;

namespace NA_Connect.Services
{
    public partial class PlanningControlSyncService
    {
        private readonly ILogger<PlanningControlSyncService> _logger;
        private readonly AutodeskApiService _autodeskApi;
        private readonly AccScheduleSyncService _scheduleService;
        private readonly IPlanningPreflightService _preflightService;
        private readonly IPlanningRepository _repository;
        private readonly string _connectionString;

        public PlanningControlSyncService(
            ILogger<PlanningControlSyncService> logger,
            AutodeskApiService autodeskApi,
            AccScheduleSyncService scheduleService,
            IPlanningPreflightService preflightService,
            IPlanningRepository repository,
            IConfiguration configuration)
        {
            _logger = logger;
            _autodeskApi = autodeskApi;
            _scheduleService = scheduleService;
            _preflightService = preflightService;
            _repository = repository;
            _connectionString = configuration.GetConnectionString("LocalConnection");
        }

        /// <summary>
        /// Sincroniza uma etapa de Planning Control com colunas descobertas dinamicamente pelo nome do
        /// cabeçalho (Etapas 12/13/14/16 — seções 6 a 14 do pedido). Método NOVO e independente de
        /// <see cref="SincronizarPlanilhaEspecificaAsync"/>. A Etapa 15 usa seu contrato oficial próprio
        /// em <see cref="SincronizarWeeklyScheduleAsync"/>.
        /// </summary>
        public async Task<(bool Sucesso, string Mensagem)> SincronizarTabelaDinamicaAsync(
            string hubId,
            string projectId,
            string robotToken,
            PlanningSheetDefinition definicao)
        {
            var tempPathsParaLimpar = new List<string>();

            try
            {
                _logger.LogInformation(
                    "[PLANNING_SYNC][INICIO] Processo={Processo} | Project_ID_ACC={ProjectIdAcc} | Buscando pasta: {Pasta}...",
                    definicao.ProcessName, projectId, definicao.FolderName);

                var caminhosDeBusca = GerarCaminhosAlternativos(definicao.FolderName);
                var folderId = await _scheduleService.FindFolderByPaths(hubId, projectId, caminhosDeBusca, robotToken);

                if (string.IsNullOrEmpty(folderId))
                {
                    string msgPastaNaoEncontrada = $"Pasta não encontrada: {definicao.FolderName}";
                    _logger.LogWarning("[PLANNING_SYNC][INICIO][ERRO] Processo={Processo} | {Mensagem}", definicao.ProcessName, msgPastaNaoEncontrada);
                    return (false, msgPastaNaoEncontrada);
                }

                var candidatos = await _autodeskApi.ListarArquivosPlanejamentoAsync(projectId, folderId, robotToken, includeLegacyXls: true);

                foreach (var candidatoBruto in candidatos)
                {
                    _logger.LogInformation(
                        "[PLANNING_SYNC][ARQUIVO_CANDIDATO] Processo={Processo} | Arquivo={Arquivo} | Extensao={Extensao} | UltimaModificacao={UltimaModificacao}",
                        definicao.ProcessName, candidatoBruto.FileName, candidatoBruto.EhXls ? ".xls" : ".xlsx/.xlsm", candidatoBruto.LastModifiedUtc);
                }

                var candidatosXlsx = candidatos.Where(c => c.EhXlsxOuXlsm).ToList();

                if (candidatosXlsx.Count == 0)
                {
                    PlanningValidationEvent.EmptyFolder(_logger, definicao.ProcessName, definicao.FolderName, candidatos.Count);
                    if (candidatos.Any(c => c.EhXls))
                    {
                        string msgXls =
                            $"XLS_REQUER_CONVERSAO_MANUAL: o arquivo de '{definicao.ProcessName}' está no formato .xls. Converta manualmente para .xlsx antes da sincronização.";
                        _logger.LogError("[PLANNING_SYNC][INICIO][ERRO] Processo={Processo} | {Mensagem}", definicao.ProcessName, msgXls);
                        return (false, msgXls);
                    }

                    var sentinel = ResultadoPreflightPlanejamento.SemExcel(definicao.FolderName);
                    string msgSemArquivo = sentinel.Mensagem;
                    _logger.LogWarning("[PLANNING_SYNC][INICIO][ERRO] Processo={Processo} | {Mensagem}", definicao.ProcessName, msgSemArquivo);
                    return (false, msgSemArquivo);
                }

                // Baixa e valida os headers de cada candidato .xlsx/.xlsm antes de escolher
                // — nunca "primeiro Excel encontrado" (seção 12).
                var validos = new List<(AutodeskApiService.ArquivoCandidato Candidato, string TempPath)>();

                foreach (var candidato in candidatosXlsx)
                {
                    string tempPathCandidato = Path.Combine(Path.GetTempPath(), $"SYNC_PLAN_{Guid.NewGuid():N}{Path.GetExtension(candidato.FileName)}");
                    tempPathsParaLimpar.Add(tempPathCandidato);

                    using (var downloadStream = await _autodeskApi.DownloadFileStreamAsync(projectId, candidato.FileId, robotToken))
                    using (var fileStream = new FileStream(tempPathCandidato, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await downloadStream.CopyToAsync(fileStream);
                    }

                    try
                    {
                    RemoverFormatacoesCondicionaisOrfas(tempPathCandidato);

                    bool assinaturaOk;
                    ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                    using (var package = new ExcelPackage(new FileInfo(tempPathCandidato)))
                    {
                        try { PlanningHeaderScan.Select(package.Workbook.Worksheets, definicao, _logger, candidato.FileName); assinaturaOk = true; }
                        catch (PlanningLayoutException) { assinaturaOk = false; }
                    }

                    _logger.LogInformation(
                        "[PLANNING_SYNC][ARQUIVO_CANDIDATO] Processo={Processo} | Arquivo={Arquivo} | AssinaturaValida={Valida}",
                        definicao.ProcessName, candidato.FileName, assinaturaOk);

                    if (assinaturaOk)
                    {
                        validos.Add((candidato, tempPathCandidato));
                    }
                    }
                    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException or System.Xml.XmlException)
                    {
                        new PlanningValidationEvent(LogLevel.Error, "ABA_INCORRETA", definicao.ProcessName, candidato.FileName,
                            Message: "Workbook ilegível: " + ex.Message).Write(_logger);
                    }
                }

                if (validos.Count == 0)
                {
                    string msgSemAssinatura =
                        $"ARQUIVO_COM_ASSINATURA_ESPERADA_NAO_ENCONTRADO: nenhum arquivo na pasta '{definicao.FolderName}' contém a assinatura de headers esperada nas primeiras 10 linhas. " +
                        $"Candidatos avaliados: [{string.Join(", ", candidatosXlsx.Select(c => c.FileName))}].";
                    _logger.LogError("[PLANNING_SYNC][INICIO][ERRO] Processo={Processo} | {Mensagem}", definicao.ProcessName, msgSemAssinatura);
                    return (false, msgSemAssinatura);
                }

                if (validos.Count > 1) return (false, "ARQUIVO_AMBIGUO: mais de um arquivo com assinatura válida.");
                var escolhido = validos.Single();

                ResultadoPreflightPlanejamento preflight;
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using (var package = new ExcelPackage(new FileInfo(escolhido.TempPath)))
                {
                    preflight = _preflightService.ExecutarPreflight(package, definicao, projectId, definicao.FolderName, escolhido.Candidato.FileName);
                }

                if (!preflight.Sucesso)
                {
                    return (false, $"{preflight.CodigoResultado}: {preflight.Mensagem}");
                }

                var persistencia = await _repository.PersistirAsync(projectId, definicao, preflight);

                if (!persistencia.Sucesso)
                {
                    return (false, $"Rollback: {(persistencia.RollbackExecutado ? "executado" : "NÃO executado — verificar manualmente")} | {persistencia.Erro}");
                }

                string mensagemSucesso =
                    $"{persistencia.LinhasInseridas} linha(s) inserida(s) na tabela {definicao.DestinationTable} (registros anteriores removidos: {persistencia.RegistrosAnteriores}).";

                return (true, mensagemSucesso);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PLANNING_SYNC][ERRO] Processo={Processo} | Erro crítico.", definicao.ProcessName);
                return (false, $"ERRO: {ex.Message}");
            }
            finally
            {
                foreach (var tempPath in tempPathsParaLimpar)
                {
                    if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                    {
                        try
                        {
                            File.Delete(tempPath);
                        }
                        catch (Exception cleanupException)
                        {
                            _logger.LogWarning(cleanupException, "[PLANNING_SYNC] Não foi possível excluir o arquivo temporário {TempPath}.", tempPath);
                        }
                    }
                }
            }
        }

        public async Task<string> SincronizarPlanilhaEspecificaAsync(
            string hubId,
            string projectId,
            string robotToken,
            string subFolder,
            string tableName,
            int headerRow,
            int colCount)
        {
            if (string.Equals(ObterNomeTabelaSemSchema(tableName), WeeklyScheduleContract.Table, StringComparison.OrdinalIgnoreCase))
            {
                var weekly = await SincronizarWeeklyScheduleAsync(hubId, projectId, robotToken, null);
                return weekly.Mensagem;
            }

            string tempPath = null;

            try
            {
                _logger.LogInformation(
                    "[PLANNING SYNC] Buscando pasta: {SubFolder}...",
                    subFolder);

                // 🎯 AQUI ENTRA A INTELIGÊNCIA BILINGUE
                var caminhosDeBusca = GerarCaminhosAlternativos(subFolder);

                var folderId = await _scheduleService.FindFolderByPaths(
                    hubId,
                    projectId,
                    caminhosDeBusca,
                    robotToken);

                if (string.IsNullOrEmpty(folderId))
                {
                    _logger.LogWarning(
                        "⚠️ Pasta não encontrada no ACC: {SubFolder}",
                        subFolder);

                    return $"Pasta não encontrada: {subFolder}";
                }

                var fileInfo = await _autodeskApi.GetFirstExcelInFolderAsync(
                    projectId,
                    folderId,
                    robotToken);

                if (string.IsNullOrEmpty(fileInfo.FileId))
                {
                    _logger.LogWarning(
                        "⚠️ Nenhum Excel encontrado na pasta: {SubFolder}",
                        subFolder);

                    return $"Nenhum Excel encontrado: {subFolder}";
                }

                _logger.LogInformation(
                    "📥 Baixando arquivo [{FileName}]...",
                    fileInfo.FileName);

                tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"SYNC_{Guid.NewGuid():N}.xlsx");

                using (var downloadStream =
                       await _autodeskApi.DownloadFileStreamAsync(
                           projectId,
                           fileInfo.FileId,
                           robotToken))
                using (var fileStream = new FileStream(
                           tempPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    await downloadStream.CopyToAsync(fileStream);
                }

                _logger.LogInformation(
                    "[PLANNING SYNC] Arquivo baixado. Preparando planilha para leitura...");

                /*
                 * Algumas planilhas exportadas ou manipuladas no Excel
                 * podem possuir regras de formatação condicional x14
                 * sem uma regra-base correspondente.
                 *
                 * Isso faz o EPPlus gerar:
                 *
                 * KeyNotFoundException:
                 * The given key '{GUID}' was not present in the dictionary.
                 *
                 * A correção é executada somente no arquivo temporário.
                 */
                int regrasRemovidas =
                    RemoverFormatacoesCondicionaisOrfas(tempPath);

                if (regrasRemovidas > 0)
                {
                    _logger.LogWarning(
                        "[PLANNING SYNC] Foram removidas {Quantidade} " +
                        "regras de formatação condicional órfãs da cópia temporária.",
                        regrasRemovidas);
                }

                _logger.LogInformation(
                    "[PLANNING SYNC] Abrindo planilha com EPPlus...");

                int rowsInserted = ProcessarEInserirExcel(
                    tempPath,
                    projectId,
                    tableName,
                    headerRow,
                    colCount);

                _logger.LogInformation(
                    "✅ {Quantidade} linhas inseridas na tabela {TableName}.",
                    rowsInserted,
                    tableName);

                return
                    $"{rowsInserted} linhas inseridas com sucesso na tabela {tableName}.";
            }
            catch (Exception ex)
            {
                /*
                 * Usar esta sobrecarga registra também o stack trace.
                 * O código anterior registrava apenas ex.Message.
                 */
                _logger.LogError(
                    ex,
                    "❌ Erro crítico ao sincronizar {SubFolder}.",
                    subFolder);

                return $"ERRO: {ex.Message}";
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempPath) &&
                    File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (Exception cleanupException)
                    {
                        _logger.LogWarning(
                            cleanupException,
                            "[PLANNING SYNC] Não foi possível excluir " +
                            "o arquivo temporário {TempPath}.",
                            tempPath);
                    }
                }
            }
        }

        /// <summary>
        /// 🎯 METODO NOVO: Mapeia as subpastas recebidas do motor principal (Inglês)
        /// e cria as variações em Português para blindar o robô.
        /// </summary>
        private string[][] GerarCaminhosAlternativos(string subFolderEn)
        {
            var raizes = new[] 
            { 
                "01 - PLANNING & CONTROL", 
                "01 - planning & control", 
                "01 - PLANEJAMENTO E CONTROLE", 
                "01 - planejamento e controle" 
            };

            var subPastas = new List<string> { subFolderEn };

            // Cruzamento de pastas em Português
            if (subFolderEn.Contains("01.03"))
            {
                subPastas.Add("01.03 - CRONOGRAMA FÍSICO, CURVAS DE AVANÇO");
                subPastas.Add("01.03 - CRONOGRAMA FISICO, CURVAS DE AVANCO");
            }
            else if (subFolderEn.Contains("01.06"))
            {
                subPastas.Add("01.06 - LISTA DE DOCUMENTOS PROJETO");
            }
            else if (subFolderEn.Contains("01.08"))
            {
                subPastas.Add("01.08 -EXTERNO-CONTROLE DE AÇÕES E PENDÊNCIAS (CAP)");
                subPastas.Add("01.08 - EXTERNO-CONTROLE DE AÇÕES E PENDÊNCIAS (CAP)");
                subPastas.Add("01.08 - EXTERNO - CONTROLE DE AÇÕES E PENDÊNCIAS (CAP)");
            }
            else if (subFolderEn.Contains("01.09"))
            {
                subPastas.Add("01.09 -INTERNO-CONTROLE DE AÇÕES E PENDÊNCIAS (CAP)");
                subPastas.Add("01.09 - INTERNO-CONTROLE DE AÇÕES E PENDÊNCIAS (CAP)");
                subPastas.Add("01.09 - INTERNO - CONTROLE DE AÇÕES E PENDÊNCIAS (CAP)");
            }
            else if (subFolderEn.Contains("01.11"))
            {
                subPastas.Add("01.11 - F. ALTERAÇÃO DE DOCUMENTO");
                subPastas.Add("01.11 - F. ALTERACAO DE DOCUMENTO");
                subPastas.Add("01.11 -  F. ALTERAÇÃO DE DOCUMENTO"); // Previsão de espaço extra
            }
            else if (subFolderEn.Contains("01.12"))
            {
                subPastas.Add("01.12 - CONTROLE DE DESVIO");
            }
            else if (subFolderEn.Contains("01.13"))
            {
                subPastas.Add("01.13 - PROGRAMAÇÃO SEMANAL ENGENHARIA");
                subPastas.Add("01.13 - PROGRAMACAO SEMANAL ENGENHARIA");
            }
            else if (subFolderEn.Contains("01.14"))
            {
                subPastas.Add("01.14 - ORDEM DE SERVIÇO");
                subPastas.Add("01.14 - ORDEM DE SERVICO");
            }
            else if (subFolderEn.Contains("01.15"))
            {
                subPastas.Add("01.15 - CONTROLE DE HOLDS");
                subPastas.Add("01.15 - CONTORLE DE HOLDS"); // Erro de digitação frequente
            }

            var listaCaminhos = new List<string[]>();

            foreach (var raiz in raizes)
            {
                foreach (var sub in subPastas)
                {
                    listaCaminhos.Add(new[] { raiz, sub });
                }
            }

            return listaCaminhos.ToArray();
        }

        /// <summary>
        /// Remove regras de formatação condicional avançada x14 que não
        /// possuem uma regra-base correspondente.
        ///
        /// A alteração é feita somente na planilha temporária utilizada
        /// pela sincronização.
        /// </summary>
        private int RemoverFormatacoesCondicionaisOrfas(string filePath)
        {
            const string X14Namespace =
                "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

            XNamespace x14 = X14Namespace;

            int totalRemovido = 0;

            using (var archive = ZipFile.Open(
                       filePath,
                       ZipArchiveMode.Update))
            {
                /*
                 * Fazemos uma cópia das informações das entradas porque
                 * algumas delas poderão ser removidas e recriadas.
                 */
                var worksheetEntries = archive.Entries
                    .Where(entry =>
                        entry.FullName.StartsWith(
                            "xl/worksheets/sheet",
                            StringComparison.OrdinalIgnoreCase) &&
                        entry.FullName.EndsWith(
                            ".xml",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(entry => new
                    {
                        entry.FullName,
                        entry.LastWriteTime
                    })
                    .ToList();

                foreach (var worksheetInfo in worksheetEntries)
                {
                    var entry = archive.GetEntry(worksheetInfo.FullName);

                    if (entry == null)
                        continue;

                    XDocument document;

                    using (var stream = entry.Open())
                    {
                        document = XDocument.Load(
                            stream,
                            LoadOptions.PreserveWhitespace);
                    }

                    /*
                     * As regras normais de Data Bar possuem um elemento:
                     *
                     * <x14:id>{GUID}</x14:id>
                     *
                     * Esse conjunto representa os IDs válidos que podem
                     * ser utilizados pelas regras avançadas x14.
                     */
                    HashSet<string> idsValidos = document
                        .Descendants(x14 + "id")
                        .Select(element => element.Value?.Trim())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    /*
                     * Localiza regras avançadas:
                     *
                     * <x14:conditionalFormatting>
                     *     <x14:cfRule id="{GUID}">
                     *
                     * Caso o GUID não exista nos IDs válidos, a regra está
                     * órfã e provoca o KeyNotFoundException no EPPlus.
                     */
                    var regrasOrfas = document
                        .Descendants(x14 + "conditionalFormatting")
                        .Where(conditionalFormatting =>
                        {
                            var rule =
                                conditionalFormatting.Element(x14 + "cfRule");

                            string id =
                                rule?.Attribute("id")?.Value?.Trim();

                            return
                                !string.IsNullOrWhiteSpace(id) &&
                                !idsValidos.Contains(id);
                        })
                        .ToList();

                    if (regrasOrfas.Count == 0)
                        continue;

                    foreach (var regraOrfa in regrasOrfas)
                    {
                        string id =
                            regraOrfa
                                .Element(x14 + "cfRule")
                                ?.Attribute("id")
                                ?.Value;

                        _logger.LogWarning(
                            "[PLANNING SYNC] Removendo regra de " +
                            "formatação condicional órfã: {RuleId}",
                            id ?? "ID não identificado");

                        regraOrfa.Remove();
                        totalRemovido++;
                    }

                    /*
                     * Remove containers que ficaram vazios depois da
                     * exclusão da regra órfã.
                     */
                    var containersVazios = document
                        .Descendants(x14 + "conditionalFormattings")
                        .Where(container =>
                            !container
                                .Elements(x14 + "conditionalFormatting")
                                .Any())
                        .ToList();

                    foreach (var container in containersVazios)
                    {
                        var extensionElement = container.Parent;

                        if (extensionElement != null &&
                            extensionElement.Name.LocalName == "ext")
                        {
                            extensionElement.Remove();
                        }
                        else
                        {
                            container.Remove();
                        }
                    }

                    /*
                     * Remove extLst que eventualmente ficou vazio.
                     */
                    var extensionListsVazias = document
                        .Descendants()
                        .Where(element =>
                            element.Name.LocalName == "extLst" &&
                            !element.Elements().Any())
                        .ToList();

                    foreach (var extensionList in extensionListsVazias)
                    {
                        extensionList.Remove();
                    }

                    /*
                     * A entrada XML é recriada dentro do XLSX.
                     * XLSX é, internamente, um arquivo ZIP com vários XMLs.
                     */
                    entry.Delete();

                    var newEntry = archive.CreateEntry(
                        worksheetInfo.FullName,
                        System.IO.Compression.CompressionLevel.Optimal);

                    newEntry.LastWriteTime =
                        worksheetInfo.LastWriteTime;

                    using (var outputStream = newEntry.Open())
                    {
                        document.Save(
                            outputStream,
                            SaveOptions.DisableFormatting);
                    }
                }
            }

            return totalRemovido;
        }

        /// <summary>
        /// Remove schema e colchetes de um nome de tabela (ex.: "[dbo].[tb_ExternalActionLog]"
        /// ou "dbo.tb_InternalActionLog") para obter apenas "tb_ExternalActionLog"/"tb_InternalActionLog".
        /// </summary>
        private static string ObterNomeTabelaSemSchema(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                return string.Empty;
            }

            return tableName
                .Replace("[", string.Empty)
                .Replace("]", string.Empty)
                .Split('.')
                .Last()
                .Trim();
        }

        /// <summary>
        /// Normaliza um nome de aba para comparação exata: espaço não separável, quebras de linha
        /// e tabs viram espaço comum, espaços múltiplos colapsam em um só, Trim e maiúsculas.
        /// </summary>
        private static string NormalizarNomeAba(string worksheetName)
        {
            if (string.IsNullOrWhiteSpace(worksheetName))
            {
                return string.Empty;
            }

            string texto = worksheetName
                .Replace(' ', ' ')
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Trim();

            string[] partes = texto.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);

            return string
                .Join(" ", partes)
                .ToUpperInvariant();
        }

        /// <summary>
        /// Seleciona a aba correta da planilha para a tabela informada. tb_ExternalActionLog e
        /// tb_InternalActionLog exigem obrigatoriamente a aba "ACTION ITEM LIST" (correspondência
        /// exata após normalização — nunca Contains/fuzzy); qualquer outra tabela continua usando a
        /// primeira aba, exatamente como antes desta correção. Nunca cai para a primeira aba quando
        /// a aba obrigatória não é encontrada — um fallback silencioso poderia apagar registros
        /// corretos (DELETE) e inserir dados de uma aba errada.
        /// </summary>
        private ExcelWorksheet SelecionarWorksheet(
            ExcelPackage package,
            string tableName)
        {
            if (package?.Workbook?.Worksheets == null ||
                package.Workbook.Worksheets.Count == 0)
            {
                throw new InvalidOperationException(
                    "A planilha não possui nenhuma aba.");
            }

            string nomeTabela =
                ObterNomeTabelaSemSchema(tableName);

            bool exigeActionItemList =
                string.Equals(
                    nomeTabela,
                    "tb_ExternalActionLog",
                    StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(
                    nomeTabela,
                    "tb_InternalActionLog",
                    StringComparison.OrdinalIgnoreCase);

            if (!exigeActionItemList)
            {
                var primeiraAba =
                    package.Workbook.Worksheets[0];

                _logger.LogInformation(
                    "[PLANNING SYNC] Tabela {TableName}: " +
                    "utilizando a primeira aba: {WorksheetName}.",
                    tableName,
                    primeiraAba.Name);

                return primeiraAba;
            }

            const string nomeAbaObrigatoria =
                "ACTION ITEM LIST";

            string nomeNormalizado =
                NormalizarNomeAba(nomeAbaObrigatoria);

            var worksheet = package
                .Workbook
                .Worksheets
                .FirstOrDefault(ws =>
                    string.Equals(
                        NormalizarNomeAba(ws.Name),
                        nomeNormalizado,
                        StringComparison.Ordinal));

            if (worksheet == null)
            {
                string abasDisponiveis = string.Join(
                    ", ",
                    package.Workbook.Worksheets
                        .Select(ws => $"'{ws.Name}'"));

                throw new InvalidOperationException(
                    $"A tabela {nomeTabela} exige a aba " +
                    $"'{nomeAbaObrigatoria}', mas ela não foi encontrada. " +
                    $"Abas disponíveis: {abasDisponiveis}.");
            }

            _logger.LogInformation(
                "[PLANNING SYNC] Tabela {TableName}: " +
                "aba obrigatória selecionada: {WorksheetName}.",
                tableName,
                worksheet.Name);

            return worksheet;
        }

        private int ProcessarEInserirExcel(
            string filePath,
            string projectId,
            string tableName,
            int headerRow,
            int colCount)
        {
            ExcelPackage.LicenseContext =
                LicenseContext.NonCommercial;

            using (var package =
                   new ExcelPackage(new FileInfo(filePath)))
            {
                var ws = SelecionarWorksheet(
                    package,
                    tableName);

                _logger.LogInformation(
                    "[PLANNING SYNC] Aba selecionada para a tabela " +
                    "{TableName}: {WorksheetName}.",
                    tableName,
                    ws.Name);

                DataTable dt = new DataTable();

                dt.Columns.Add(
                    "Project_ID_ACC",
                    typeof(string));

                dt.Columns.Add(
                    "Data_Sincronizacao",
                    typeof(DateTime));

                for (int i = 0; i < colCount; i++)
                {
                    dt.Columns.Add(
                        $"Col{i}",
                        typeof(string));
                }

                int startRow = headerRow + 1;
                int endRow = ws.Dimension?.End.Row ?? 0;

                DateTime syncTime = DateTime.Now;

                _logger.LogInformation(
                    "[PLANNING SYNC] Lendo linhas {StartRow} até {EndRow}, " +
                    "com {ColumnCount} colunas.",
                    startRow,
                    endRow,
                    colCount);

                for (int row = startRow; row <= endRow; row++)
                {
                    var dr = dt.NewRow();

                    dr["Project_ID_ACC"] = projectId;
                    dr["Data_Sincronizacao"] = syncTime;

                    bool isRowEmpty = true;

                    for (int col = 1; col <= colCount; col++)
                    {
                        var cellValue = ws.Cells[row, col].Value;

                        string value =
                            cellValue
                                ?.ToString()
                                ?.Trim();

                        if (!string.IsNullOrEmpty(value))
                        {
                            isRowEmpty = false;
                        }

                        /*
                         * DBNull.Value evita problemas ao inserir valores
                         * nulos no DataTable e no SqlBulkCopy.
                         */
                        dr[$"Col{col - 1}"] =
                            string.IsNullOrEmpty(value)
                                ? DBNull.Value
                                : value;
                    }

                    if (!isRowEmpty)
                    {
                        dt.Rows.Add(dr);
                    }
                }

                if (dt.Rows.Count == 0)
                {
                    _logger.LogWarning(
                        "[PLANNING SYNC] Nenhuma linha preenchida encontrada.");

                    return 0;
                }

                using (SqlConnection conn =
                       new SqlConnection(_connectionString))
                {
                    conn.Open();

                    using (var cmdDelete = new SqlCommand(
                               $"DELETE FROM {tableName} " +
                               "WHERE Project_ID_ACC = @pid",
                               conn))
                    {
                        cmdDelete.Parameters.Add(
                            "@pid",
                            SqlDbType.NVarChar);

                        cmdDelete.Parameters["@pid"].Value =
                            projectId;

                        int deletedRows =
                            cmdDelete.ExecuteNonQuery();

                        _logger.LogInformation(
                            "[PLANNING SYNC] {DeletedRows} registros antigos removidos.",
                            deletedRows);
                    }

                    using (SqlBulkCopy bulkCopy =
                           new SqlBulkCopy(conn))
                    {
                        bulkCopy.DestinationTableName =
                            tableName;

                        bulkCopy.BulkCopyTimeout = 300;
                        bulkCopy.BatchSize = 1000;

                        bulkCopy.ColumnMappings.Add(
                            "Project_ID_ACC",
                            "Project_ID_ACC");

                        bulkCopy.ColumnMappings.Add(
                            "Data_Sincronizacao",
                            "Data_Sincronizacao");

                        using (var command = new SqlCommand(
                                   @"
                                   SELECT COLUMN_NAME
                                   FROM INFORMATION_SCHEMA.COLUMNS
                                   WHERE TABLE_NAME = @tableName
                                     AND COLUMN_NAME NOT IN
                                     (
                                         'Id_Registro',
                                         'Project_ID_ACC',
                                         'Data_Sincronizacao'
                                     )
                                   ORDER BY ORDINAL_POSITION",
                                   conn))
                        {
                            /*
                             * INFORMATION_SCHEMA.TABLE_NAME recebe apenas
                             * o nome da tabela, sem o schema dbo.
                             */
                            string informationSchemaTableName =
                                tableName
                                    .Replace("[", string.Empty)
                                    .Replace("]", string.Empty)
                                    .Split('.')
                                    .Last();

                            command.Parameters.Add(
                                "@tableName",
                                SqlDbType.NVarChar,
                                128);

                            command.Parameters["@tableName"].Value =
                                informationSchemaTableName;

                            using (var reader =
                                   command.ExecuteReader())
                            {
                                int columnIndex = 0;

                                while (reader.Read() &&
                                       columnIndex < colCount)
                                {
                                    string destinationColumn =
                                        reader.GetString(0);

                                    bulkCopy.ColumnMappings.Add(
                                        $"Col{columnIndex}",
                                        destinationColumn);

                                    columnIndex++;
                                }

                                if (columnIndex == 0)
                                {
                                    throw new InvalidOperationException(
                                        $"Nenhuma coluna de destino foi encontrada " +
                                        $"na tabela {tableName}.");
                                }
                            }
                        }

                        bulkCopy.WriteToServer(dt);
                    }
                }

                return dt.Rows.Count;
            }
        }
    }
}
