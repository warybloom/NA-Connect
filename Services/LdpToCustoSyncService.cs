using AccIntegration.WebApp.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace APS.WebApp.Services
{
    public class LdpToCustoSyncService
    {
        private readonly AutodeskApiService _autodeskApi;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LdpToCustoSyncService> _logger;

        // Configurações de Estrutura baseadas na Linha 7 (Headers) e 8 (Dados)
        private const int LINHA_CABECALHO = 7;
        private const int LINHA_INICIO_DADOS = 8;
        
        // LIMITADOR ESTREITO: O robô só pode mexer até a coluna G (7) de forma obrigatória.
        // As colunas de 8 em diante contêm fórmulas estruturais complexas do Excel e serão preservadas.
        private const int COLUNA_LIMITE_TECNICO = 7; 

        public LdpToCustoSyncService(
            AutodeskApiService autodeskApi, 
            IConfiguration configuration, 
            ILogger<LdpToCustoSyncService> logger)
        {
            _autodeskApi = autodeskApi;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Sincroniza os dados da aba LDP para a aba de Custo localizando as colunas dinamicamente,
        /// sem risco de quebra posicional ou estouro de range por abas ocultas.
        /// </summary>
        public async Task<string> ExecuteFullSyncProcessAsync(string hubId, string projectId, string accessToken, string ldpFileId)
        {
            _logger.LogInformation("📊 [ETAPA 6/11] Sincronizando LDP -> CUSTO (Preservando Estilos e Fórmulas O-AH)...");
            string tempPath = Path.Combine(Path.GetTempPath(), $"SYNC_CUSTO_{Guid.NewGuid()}.xlsx");

            try
            {
                var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
                if (itemDetails == null) throw new Exception("Detalhes do arquivo não encontrados (Item/Folder).");

                string versionUrn = itemDetails.TipVersionUrn;
                string parentFolderId = itemDetails.ParentFolderId; 
                string realFileName = itemDetails.DisplayName;

                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, versionUrn, accessToken))
                using (var fs = new FileStream(tempPath, FileMode.Create)) 
                {
                    await dl.CopyToAsync(fs);
                }

                var fileInfo = new FileInfo(tempPath);
                if (fileInfo.Length == 0) throw new Exception("O download retornou um arquivo de 0 bytes!");

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using (var package = new ExcelPackage(fileInfo))
                {
                    var wsLdp = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals("LDP", StringComparison.OrdinalIgnoreCase));
                    var wsCusto = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals("CUSTO", StringComparison.OrdinalIgnoreCase));

                    if (wsLdp == null || wsCusto == null) 
                    {
                        var abasReais = string.Join(", ", package.Workbook.Worksheets.Select(w => $"'{w.Name}'"));
                        throw new Exception($"Abas 'LDP' ou 'CUSTO' não encontradas! No arquivo continha apenas: {abasReais}");
                    }

                    // Mapeamento Dinâmico dos Cabeçalhos em Runtime
                    var validacao = LdpToCustoSyncMapper.Sincronizar(wsLdp, wsCusto);
                    _logger.LogInformation(
                        "[LDP_CUSTO][VALIDADO] Documentos={Documentos} Colunas={Colunas} "
                        + "CelulasCopiadas={Celulas} CelulasNaoVazias={NaoVazias} "
                        + "Destino=A:G PrimeiraLinha=8; formulas H:AH fora do escopo",
                        validacao.Documentos, validacao.Colunas,
                        validacao.Celulas, validacao.CelulasNaoVazias);
                    package.Save();
                }

                using (var ul = new FileStream(tempPath, FileMode.Open))
                {
                    await _autodeskApi.CreateNewVersionAsync(projectId, ldpFileId, parentFolderId, realFileName, ul, accessToken);
                }

                return "Sincronização concluída. Formatação e Fórmulas preservadas.";
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Erro na Etapa 6 (Custo): {ex.Message}");
                throw;
            }
            finally 
            { 
                if (File.Exists(tempPath)) File.Delete(tempPath); 
            }
        }

        /// <summary>
        /// Mapeia os cabeçalhos varrendo estritamente o tamanho físico real da planilha,
        /// neutralizando erros de range em abas com estruturas reduzidas ou ocultas.
        /// </summary>
        private Dictionary<string, int> MapearColunas(ExcelWorksheet ws)
        {
            var dic = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            
            // 🎯 CAPTURA DINÂMICA DE INTERVALO: Evita varrer colunas inexistentes na memória
            int limiteFisicoColunas = ws.Dimension?.End.Column ?? 100;
            int limiteMaximoVarredura = Math.Min(100, limiteFisicoColunas);

            for (int c = 1; c <= limiteMaximoVarredura; c++)
            {
                // Substituído .Text por .Value?.ToString() para evitar chamadas ao motor de cálculos do Excel
                string header = ws.Cells[LINHA_CABECALHO, c].Value?.ToString()?.Trim() ?? "";
                
                if (!string.IsNullOrEmpty(header) && !dic.ContainsKey(header))
                    dic.Add(header, c);
            }
            return dic;
        }

        private string GetLdpFileNameFromConfig(string projectId)
        {
            return _configuration[$"ProjectSettings:{projectId}:LdpFileName"] ?? _configuration["ProjectSettings:Default:LdpFileName"];
        }

        private async Task<string> FindFolderIdByAlternativePathsAsync(string hubId, string projectId, string accessToken)
        {
            var paths = new[] { 
                new[] { "09 - DESENVOLVIMENTO", "09.14 - GESTÃO" }, 
                new[] { "09 - DEVELOPING", "09.14 - MANAGEMENT" } 
            };

            foreach (var path in paths)
            {
                var id = await _autodeskApi.FindFolderIdByPathAsync(hubId, projectId, path, accessToken);
                if (!string.IsNullOrEmpty(id)) return id;
            }
            return null;
        }
    }
}