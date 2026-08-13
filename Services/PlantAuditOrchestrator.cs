using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using APS.WebApp.Models;
using ForgeSync.Modules.Piping.Services;
// ✅ Referência correta para o seu namespace:
using ForgeSync.Modules.Piping.Repositories; 

namespace ForgeSync.Modules.Plant3D.Services
{
    public class PlantAuditOrchestrator
    {
        private readonly PlantAuditEngine _auditEngine;
        private readonly PipingRepository _repository;
        private readonly PipingDataProcessor _dataProcessor;

        public PlantAuditOrchestrator(string connectionString)
        {
            _auditEngine = new PlantAuditEngine();
            _repository = new PipingRepository(connectionString);
            _dataProcessor = new PipingDataProcessor();
        }

        public async Task RunFullProjectAudit(int projectId, string jsonPathPID, string jsonPath3D)
        {
            // 1. Busca a configuração de mapeamento dinâmica
            string mappingJson = await _repository.GetProjectMappingConfigAsync(projectId);

            // 2. Processa os arquivos JSON do Plant 3D
            var pidData = _dataProcessor.ProcessPipingData(jsonPathPID);
            var p3dData = _dataProcessor.ProcessPipingData(jsonPath3D);

            // 3. Cria ID de Auditoria
            int novaAuditoriaId = await _repository.CreateAuditoriaHeaderAsync(projectId);

            // 4. Executa o Cruzamento
            var resultadosAuditados = _auditEngine.ExecuteAudit(
                pidData, 
                p3dData, 
                mappingJson, 
                novaAuditoriaId
            );

            // 5. Salva os resultados na tabela Plant3D_Ativos
            _repository.BulkInsertPipingItems(resultadosAuditados, "Plant3D_Ativos");
        }
    }
}