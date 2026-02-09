using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Data;
using NA_Connect.Services;
using APS.WebApp.Services;

namespace NA_Connect.Services
{
    public class AccProjectSyncService
    {
        private readonly AutodeskApiService _accApi;
        private readonly DatabaseService _db;

        public AccProjectSyncService(AutodeskApiService accApi, DatabaseService db)
        {
            _accApi = accApi;
            _db = db;
        }

        /// <summary>
        /// Orquestra a sincronização: lê os hubs do banco local e busca os projetos na Autodesk.
        /// </summary>
        public async Task SincronizarProjetosDosHubs()
        {
            // 1. Busca os Hubs que você cadastrou manualmente no seu SQL local
            DataTable hubsNoBanco = _db.GetHubs();

            if (hubsNoBanco.Rows.Count == 0)
            {
                Console.WriteLine(">>> [AVISO] Nenhum Hub encontrado no banco local para sincronizar.");
                return;
            }

            foreach (DataRow hubRow in hubsNoBanco.Rows)
            {
                string hubId = hubRow["Account_ID_ACC"].ToString();
                string hubNome = hubRow["Nome_Hub"].ToString();
                int hubLocalId = Convert.ToInt32(hubRow["HubContaID"]);

                Console.WriteLine($"\n>>> [SYNC] Acessando Hub: {hubNome} ({hubId})");

                try 
                {
                    // 2. Chama a API da Autodesk. 
                    // IMPORTANTE: Ajustei para 'GetProjectsAsync' que é o padrão modular.
                    var projetosAcc = await _accApi.GetProjectsAsync(hubId); 

                    if (projetosAcc == null) continue;

                    foreach (var proj in projetosAcc)
                    {
                        // 3. Extração dos dados dinâmicos da API
                        // A API da Autodesk geralmente retorna 'id' e 'name' dentro de attributes
                        string projectId = proj.id;
                        string projectName = proj.name;

                        // 4. Salva ou atualiza no seu SQL Server local
                        _db.SalvarProjeto(hubLocalId, projectName, projectId);
                        
                        Console.WriteLine($">>> [SQL] Projeto sincronizado: {projectName}");
                    }
                }
                catch (Exception ex)
                {
                    // Log detalhado conforme sua necessidade de gestor de padrões
                    Console.WriteLine($">>> [ERRO CRÍTICO] Falha ao sincronizar projetos do Hub {hubNome}: {ex.Message}");
                }
            }
        }
    }
}