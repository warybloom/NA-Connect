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
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;

namespace NA_Connect.Services
{
    public class PlanningControlSyncService
    {
        private readonly ILogger<PlanningControlSyncService> _logger;
        private readonly AutodeskApiService _autodeskApi;
        private readonly AccScheduleSyncService _scheduleService;
        private readonly string _connectionString;

        public PlanningControlSyncService(
            ILogger<PlanningControlSyncService> logger,
            AutodeskApiService autodeskApi,
            AccScheduleSyncService scheduleService,
            IConfiguration configuration)
        {
            _logger = logger;
            _autodeskApi = autodeskApi;
            _scheduleService = scheduleService;
            _connectionString = configuration.GetConnectionString("LocalConnection");
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
            string tempPath = null;

            try
            {
                _logger.LogInformation(
                    "[PLANNING SYNC] Buscando pasta: {SubFolder}...",
                    subFolder);

                var folderId = await _scheduleService.FindFolderByPaths(
                    hubId,
                    projectId,
                    new[]
                    {
                        new[] { "01 - PLANNING & CONTROL", subFolder },
                        new[] { "01 - planning & control", subFolder }
                    },
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
                    "LDP",
                    robotToken
                    ); // Passa null para usar o padrão de nomenclatura do projeto

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
                if (package.Workbook.Worksheets.Count == 0)
                {
                    throw new InvalidOperationException(
                        "A planilha não possui nenhuma aba.");
                }

                // Primeira aba da planilha.
                var ws = package.Workbook.Worksheets[0];

                _logger.LogInformation(
                    "[PLANNING SYNC] Aba selecionada: {WorksheetName}.",
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