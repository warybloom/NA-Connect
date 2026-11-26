using System.Data;
using APS.WebApp.Services.Planning;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace NA_Connect.Services;

public partial class PlanningControlSyncService
{
    public async Task<(bool Sucesso, string Mensagem)> SincronizarWeeklyScheduleAsync(
        string hubId, string projectId, string robotToken, string? projectLanguage)
    {
        var temporaryFiles = new List<string>();
        try
        {
            if (projectLanguage == null)
            {
                await using var languageConnection = new SqlConnection(_connectionString);
                await languageConnection.OpenAsync();
                using var languageCommand = new SqlCommand("SELECT Project_Language FROM dbo.Projetos WHERE Project_ID_ACC=@pid OR Project_ID_ACC=@cleanPid", languageConnection);
                languageCommand.Parameters.AddWithValue("@pid", projectId);
                languageCommand.Parameters.AddWithValue("@cleanPid", projectId.StartsWith("b.") ? projectId.Substring(2) : projectId);
                projectLanguage = (await languageCommand.ExecuteScalarAsync())?.ToString();
            }
            var roots = new[] { "01 - PLANNING & CONTROL", "01 - PLANEJAMENTO E CONTROLE" };
            var paths = WeeklyScheduleContract.FolderAliases(projectLanguage)
                .SelectMany(alias => roots.Select(root => new[] { root, alias })).ToArray();
            var folder = await _scheduleService.FindFolderByPaths(hubId, projectId, paths, robotToken);
            // O helper legado captura falhas de transporte. Null não prova ausência da pasta.
            if (string.IsNullOrWhiteSpace(folder))
                return (false, "PASTA_WEEKLY_SCHEDULE_NAO_CONFIRMADA: snapshot preservado; descoberta não distingue ausência de falha ACC.");
            var candidates = await _autodeskApi.ListarArquivosPlanejamentoAsync(projectId, folder, robotToken, includeLegacyXls: true);
            if (candidates.Count == 0)
            {
                PlanningValidationEvent.EmptyFolder(_logger, "PROGRAMAÇÃO", string.Join(" | ", paths.Select(p => string.Join("/", p))), 0);
                using var sentinel = WeeklyScheduleContract.Sentinel(projectId, "SEM_EXCEL", "NÃO EXISTE EXCEL PARA O PROJETO NA PASTA OFICIAL DE PROGRAMAÇÃO SEMANAL.");
                _logger.LogWarning("[PASTA_SEM_ARQUIVO] Projeto={Projeto} Ação=registro sentinela SEM_EXCEL criado", projectId);
                return await PersistWeeklyAsync(projectId, sentinel);
            }
            var valid = new List<string>();
            var originalNames = new Dictionary<string, string>();
            using var diagnostics = WeeklyScheduleContract.CreateTable();
            foreach (var legacy in candidates.Where(c => c.EhXls))
            {
                using var diagnostic = WeeklyScheduleContract.Sentinel(projectId, "PADRAO_INCORRETO",
                    "PADRÃO EXCEL INCORRETO, IMPOSSÍVEL OBTER INFORMAÇÕES. XLS_REQUER_CONVERSAO_MANUAL: converter para XLSX/XLSM.", legacy.FileName);
                diagnostics.Merge(diagnostic);
            }
            foreach (var candidate in candidates.Where(c => c.EhXlsxOuXlsm))
            {
                var path = Path.Combine(Path.GetTempPath(), $"WEEKLY_{Guid.NewGuid():N}{Path.GetExtension(candidate.FileName)}");
                temporaryFiles.Add(path);
                // Falhas de transporte bloqueiam a seleção: não podemos afirmar unicidade sem ler todos.
                using (var download = await _autodeskApi.DownloadFileStreamAsync(projectId, candidate.FileId, robotToken))
                using (var output = File.Create(path)) await download.CopyToAsync(output);
                try
                {
                    RemoverFormatacoesCondicionaisOrfas(path);
                    ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                    using var package = new ExcelPackage(new FileInfo(path));
                    var inspection = WeeklyScheduleContract.InspectWorkbook(package, _logger, candidate.FileName);
                    if (inspection.Error != null)
                    {
                        using var diagnostic = WeeklyScheduleContract.Read(package, projectId, _logger, candidate.FileName);
                        diagnostics.Merge(diagnostic);
                    }
                    if (inspection.Error == null) { valid.Add(path); originalNames[path] = candidate.FileName; }
                }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException or System.Xml.XmlException)
                {
                    _logger.LogWarning(ex, "[WEEKLY] Excel ilegível: {Arquivo}", candidate.FileName);
                    using var diagnostic = WeeklyScheduleContract.Sentinel(projectId, "PADRAO_INCORRETO", "PADRÃO EXCEL INCORRETO, IMPOSSÍVEL OBTER INFORMAÇÕES. " + ex.Message, candidate.FileName);
                    diagnostics.Merge(diagnostic);
                }
            }
            var error = WeeklyScheduleContract.SelectionError(valid.Count);
            if (valid.Count == 0) return await PersistWeeklyAsync(projectId, diagnostics);
            // Mais de um arquivo válido não tem regra de precedência autorizada: preservar snapshot.
            if (error != null) return (false, error);
            using var selected = new ExcelPackage(new FileInfo(valid.Single()));
            using var data = WeeklyScheduleContract.Read(selected, projectId, _logger, originalNames[valid.Single()]);
            if (data.Rows.Count == 0) return (false, "TEMPLATE_SEM_DADOS: snapshot preservado; necessário definir política para arquivo reconhecido vazio.");
            data.Merge(diagnostics);
            return await PersistWeeklyAsync(projectId, data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WEEKLY] Importação bloqueada");
            return (false, ex.Message);
        }
        finally
        {
            foreach (var path in temporaryFiles)
                try { File.Delete(path); }
                catch (IOException ex) { _logger.LogWarning(ex, "[WEEKLY] Falha ao limpar temporário {Path}", path); }
        }
    }

    private async Task<(bool Sucesso, string Mensagem)> PersistWeeklyAsync(string projectId, DataTable data)
    {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await ValidateWeeklySqlAsync(connection, data);
            // Template vazio válido preserva os registros existentes, como no operacional anterior.
            if (data.Rows.Count == 0) return (false, "TEMPLATE_SEM_DADOS: arquivo reconhecido sem dados; snapshot preservado para evitar exclusão silenciosa.");
            using var transaction = connection.BeginTransaction();
            try
            {
                using var delete = new SqlCommand("DELETE FROM dbo.tb_WeeklyEngineeringSchedule WHERE Project_ID_ACC = @pid", connection, transaction);
                delete.Parameters.Add("@pid", SqlDbType.NVarChar, 100).Value = projectId;
                await delete.ExecuteNonQueryAsync();
                using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.KeepNulls, transaction)
                { DestinationTableName = "dbo.tb_WeeklyEngineeringSchedule", BulkCopyTimeout = 300 };
                foreach (DataColumn column in data.Columns) bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                await bulk.WriteToServerAsync(data);
                transaction.Commit();
                return (true, $"{data.Rows.Count} linha(s) inserida(s) em dbo.tb_WeeklyEngineeringSchedule.");
            }
            catch { transaction.Rollback(); throw; }
    }

    private static async Task ValidateWeeklySqlAsync(SqlConnection connection, DataTable data)
    {
        using var command = new SqlCommand("SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='tb_WeeklyEngineeringSchedule'", connection);
        using var reader = await command.ExecuteReaderAsync();
        var schema = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) schema.Add(reader.GetString(0), reader.GetString(1));
        WeeklyScheduleContract.PrepareForSql(data, schema);
    }
}
