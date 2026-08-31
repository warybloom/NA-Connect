#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using APS.WebApp.Models;
using NA_Connect.Services; 

namespace APS.WebApp.Services
{
    public class Plant3DAuditoriaService
    {
        private readonly ILogger<Plant3DAuditoriaService> _logger;
        private readonly AutodeskApiService _autodeskApi;
        private readonly DatabaseService _dbService;
        private readonly Plant3DDataFetcher _fetcher;

        public Plant3DAuditoriaService(
            ILogger<Plant3DAuditoriaService> logger,
            AutodeskApiService autodeskApi,
            DatabaseService dbService,
            Plant3DDataFetcher fetcher)
        {
            _logger = logger;
            _autodeskApi = autodeskApi;
            _dbService = dbService;
            _fetcher = fetcher;
        }

        public async Task<Plant3DAuditoria> RodarAuditoriaCompletaAsync(string projectId)
        {
            string tempProcessPowerPath = null;
            string tempPipingPath = null;

            try
            {
                _logger.LogInformation($"[AUDITORIA] Iniciando auditoria P&ID vs 3D para o projeto {projectId}");

                // 1. Obter Configuração do Banco de Dados (ForgeSync)
                // Usando o método que você já tem para buscar a pasta configurada
                var config = _dbService.ObterConfiguracaoLdp(projectId); 
                if (config == null || string.IsNullOrEmpty(config.Folder_ID_LDP))
                {
                    throw new Exception("Configuração não encontrada. Verifique se o projeto e a pasta DCF estão cadastrados na tabela Plant3D_Configuracao.");
                }

                string folderUrn = config.Folder_ID_LDP;
                string token = await _autodeskApi.GetInternalTokenAsync();

                // 2. Busca e Download do ProcessPower.dcf (P&ID)
                _logger.LogInformation($"[AUDITORIA] Procurando ProcessPower.dcf na pasta {folderUrn}...");
                
                // Tentativa 1: Nome exato com extensão e recursividade ligada
                string processUrn = await _autodeskApi.FindFileByNameRecursiveAsync(projectId, folderUrn, "ProcessPower.dcf", token, true);
                
                // Tentativa 2: Fallback sem extensão caso a indexação do ACC esteja diferente
                if (string.IsNullOrEmpty(processUrn))
                {
                    _logger.LogWarning("[AUDITORIA] ProcessPower.dcf não achado com extensão, tentando sem extensão...");
                    processUrn = await _autodeskApi.FindFileByNameRecursiveAsync(projectId, folderUrn, "ProcessPower", token, true);
                }

                if (string.IsNullOrEmpty(processUrn)) 
                    throw new Exception($"ProcessPower.dcf não encontrado no ACC dentro da pasta configurada.");

                tempProcessPowerPath = await _autodeskApi.DownloadFileToTempPathAsync(projectId, processUrn, token);

                // 3. Busca e Download do Piping.dcf (3D)
                _logger.LogInformation("[AUDITORIA] Procurando Piping.dcf...");
                string pipingUrn = await _autodeskApi.FindFileByNameRecursiveAsync(projectId, folderUrn, "Piping.dcf", token, true);
                
                if (string.IsNullOrEmpty(pipingUrn))
                {
                    pipingUrn = await _autodeskApi.FindFileByNameRecursiveAsync(projectId, folderUrn, "Piping", token, true);
                }

                if (string.IsNullOrEmpty(pipingUrn)) 
                    throw new Exception($"Piping.dcf não encontrado no ACC dentro da pasta configurada.");

                tempPipingPath = await _autodeskApi.DownloadFileToTempPathAsync(projectId, pipingUrn, token);

                // 4. Extrair as Listas do SQLite local (Dapper)
                _logger.LogInformation("[AUDITORIA] Extraindo dados dos arquivos SQLite baixados...");
                var instPID = await _fetcher.ExtrairInstrumentosPIDAsync(tempProcessPowerPath);
                var valvPID = await _fetcher.ExtrairValvulasPIDAsync(tempProcessPowerPath);
                
                var inst3D = await _fetcher.ExtrairInstrumentos3DAsync(tempPipingPath);
                var valv3D = await _fetcher.ExtrairValvulas3DAsync(tempPipingPath);

                // 5. Inicializar Objeto de Auditoria para o Dashboard
                var auditoria = new Plant3DAuditoria
                {
                    Project_ID_ACC = projectId,
                    Data_Auditoria = DateTime.Now,
                    Total_PID = instPID.Count + valvPID.Count,
                    Total_3D = inst3D.Count + valv3D.Count,
                    Faltam_No_3D = 0,
                    Faltam_No_PID = 0,
                    Divergencias_Spec = 0
                };

                var listaAtivosCruzados = new List<Plant3DAtivo>();

                // ==========================================================
                // 6. CRUZAMENTO DE INSTRUMENTOS (P&ID vs 3D)
                // ==========================================================
                foreach (var p in instPID)
                {
                    var match3D = inst3D.FirstOrDefault(t => !string.IsNullOrEmpty(t.Tag) && t.Tag.Equals(p.Tag, StringComparison.OrdinalIgnoreCase));
                    
                    var ativo = new Plant3DAtivo
                    {
                        Tag = p.Tag,
                        Categoria = "Instrumento",
                        Desenho_PID = p.DwgName,
                        Desenho_3D = match3D?.DwgName,
                        Spec_PID = p.Type,
                        Spec_3D = match3D?.Type
                    };

                    if (match3D == null)
                    {
                        ativo.Status_Cruzamento = "Falta Modelar no 3D";
                        auditoria.Faltam_No_3D++;
                    }
                    else if (p.Type != match3D.Type)
                    {
                        ativo.Status_Cruzamento = "Divergência de Spec";
                        auditoria.Divergencias_Spec++;
                    }
                    else
                    {
                        ativo.Status_Cruzamento = "Match Perfeito";
                    }
                    listaAtivosCruzados.Add(ativo);
                }

                // ==========================================================
                // 7. CRUZAMENTO DE VÁLVULAS (P&ID vs 3D)
                // ==========================================================
                foreach (var p in valvPID)
                {
                    var match3D = valv3D.FirstOrDefault(t => !string.IsNullOrEmpty(t.Tag) && t.Tag.Equals(p.Tag, StringComparison.OrdinalIgnoreCase));
                    
                    var ativo = new Plant3DAtivo
                    {
                        Tag = p.Tag,
                        Categoria = "Válvula",
                        Desenho_PID = p.DwgName,
                        Desenho_3D = match3D?.DwgName,
                        Spec_PID = p.ClassName,
                        Spec_3D = match3D?.ClassName
                    };

                    if (match3D == null)
                    {
                        ativo.Status_Cruzamento = "Falta Modelar no 3D";
                        auditoria.Faltam_No_3D++;
                    }
                    else
                    {
                        ativo.Status_Cruzamento = "Match Perfeito";
                    }
                    listaAtivosCruzados.Add(ativo);
                }

                // 8. TODO: Salvar listaAtivosCruzados no Banco de Dados
                // Aqui você chamaria o DatabaseService para persistir os detalhes na tabela Plant3D_Ativos
                
                _logger.LogInformation($"[AUDITORIA] Sucesso! Resumo: {auditoria.Total_PID} no PID, {auditoria.Total_3D} no 3D.");
                
                return auditoria;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[AUDITORIA] Erro crítico no processamento do projeto {projectId}");
                throw;
            }
            finally
            {
                // Limpeza rigorosa de arquivos temporários
                if (tempProcessPowerPath != null && File.Exists(tempProcessPowerPath)) File.Delete(tempProcessPowerPath);
                if (tempPipingPath != null && File.Exists(tempPipingPath)) File.Delete(tempPipingPath);
            }
        }
    }
}