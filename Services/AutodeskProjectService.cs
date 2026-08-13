using System;
using System.Threading.Tasks;
using Autodesk.Forge; // Referência à SDK da Autodesk
using Microsoft.Extensions.Configuration;

namespace LDP_System.Modules.Autodesk.Services
{
    /// <summary>
    /// Módulo responsável exclusivamente pela comunicação técnica com os servidores da Autodesk.
    /// </summary>
    public class AutodeskProjectService
    {
        private readonly IConfiguration _config;

        public AutodeskProjectService(IConfiguration config)
        {
            _config = config;
        }

        /// <summary>
        /// Verifica se uma pasta específica realmente existe dentro de um projeto no BIM 360/ACC.
        /// </summary>
        /// <param name="projectId">ID do Projeto na Autodesk (ex: b.123...)</param>
        /// <param name="folderId">ID da Pasta na Autodesk (ex: urn:adsk...)</param>
        /// <param name="token">Token de acesso OAuth2 válido</param>
        public async Task<bool> ValidarPastaNoAutodesk(string projectId, string folderId, string token)
        {
            // Instancia a API de Pastas da SDK oficial
            var foldersApi = new FoldersApi();
            foldersApi.Configuration.AccessToken = token;

            try 
            {
                // Tenta buscar os detalhes da pasta
                var folder = await foldersApi.GetFolderAsync(projectId, folderId);
                
                // Se a API retornar algo, a pasta é válida
                return folder != null;
            }
            catch (Exception ex)
            {
                // Se der erro (404 Not Found, 401 Unauthorized, etc), retornamos falso
                // O log do erro pode ser adicionado aqui se desejar
                return false;
            }
        }
    }
}