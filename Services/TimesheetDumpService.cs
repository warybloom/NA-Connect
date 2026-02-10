using AccIntegration.WebApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace APS.WebApp.Services
{
    public class TimesheetDumpService
    {
        private readonly ApplicationDbContext _context;
        private readonly AutodeskApiService _autodeskApi;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TimesheetDumpService> _logger;

        public TimesheetDumpService(ApplicationDbContext context, AutodeskApiService autodeskApi, IConfiguration configuration, ILogger<TimesheetDumpService> logger)
        {
            _context = context;
            _autodeskApi = autodeskApi;
            _configuration = configuration;
            _logger = logger;
        }

        // Módulo: Services/TimesheetDumpService.cs

// 🎯 AGORA RECEBE O ldpFileId DO ROBÔ
public async Task<string> ResetAndDumpTimesheetAsync(string hubId, string projectId, string token, string ldpFileId)
{
    _logger.LogInformation("🚀 [TIMESHEET DUMP] Iniciando reconstrução...");

    try 
    {
        // ❌ REMOVA a lógica de procurar pasta (FindFolderByPaths) daqui!
        // ✅ USE DIRETO O ID QUE VEIO DO ROBÔ
        using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, ldpFileId, token))
        {
            // Lógica de processamento...
        }
        return "Sucesso";
    }
    catch (Exception ex)
    {
        return $"Erro: {ex.Message}";
    }
}

        // --- LÓGICA DE BUSCA INTELIGENTE (PT -> EN) ---
        private async Task<string> FindFolderIdByAlternativePathsAsync(string hubId, string projectId, string accessToken)
        {
            var paths = new[] {
                new[] { "09 - DESENVOLVIMENTO", "09.10 - GESTÃO" },      // Tenta Português
                new[] { "09 - MODEL DEVELOPING", "09.10 - MANAGEMENT" }  // Tenta Inglês
            };

            foreach (var p in paths)
            {
                try
                {
                    var id = await _autodeskApi.FindFolderIdByPathAsync(hubId, projectId, p, accessToken);
                    if (!string.IsNullOrEmpty(id))
                    {
                        // Achou! Retorna o ID
                        return id;
                    }
                }
                catch
                {
                    // Se der erro (ex: pasta não existe), silencia e tenta a próxima
                }
            }
            // Se tentou tudo e não achou nada, retorna null (mas o método principal agora trata isso!)
            return null;
        }

        private string GetLdpFileNameFromConfig(string projectId)
        {
            var name = _configuration[$"ProjectSettings:{projectId}:LdpFileName"];
            return string.IsNullOrEmpty(name) ? _configuration["ProjectSettings:Default:LdpFileName"] : name;
        }
    }
}