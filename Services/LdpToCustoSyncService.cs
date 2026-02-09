using AccIntegration.WebApp.Data;
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
    public class LdpToCustoSyncService
    {
        private readonly AutodeskApiService _autodeskApi;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LdpToCustoSyncService> _logger;

        // Configurações de Estrutura baseadas na Linha 7 (Headers) e 8 (Dados)
        private const int LINHA_CABECALHO = 7;
        private const int LINHA_INICIO_DADOS = 8;
        
        // LIMITADOR: O robô só pode mexer até a coluna N (14). 
        // As colunas O (15) em diante contém fórmulas e serão preservadas.
        private const int COLUNA_LIMITE_TECNICO = 14; 

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
        /// Orquestra o processo de sincronização entre as abas LDP e CUSTO
        /// </summary>
        public async Task<string> ExecuteFullSyncProcessAsync(string hubId, string projectId, string accessToken)
        {
            _logger.LogInformation("📊 [ETAPA 3/7] Sincronizando LDP -> CUSTO (Preservando Estilos e Fórmulas O-AH)...");
            string tempPath = Path.Combine(Path.GetTempPath(), $"SYNC_CUSTO_{Guid.NewGuid()}.xlsx");

            try
            {
                // 1. Localizar o arquivo LDP de controle no ACC
                string ldpSearchName = GetLdpFileNameFromConfig(projectId);
                var folderId = await FindFolderIdByAlternativePathsAsync(hubId, projectId, accessToken);
                var (itemId, actualFileName) = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderId, ldpSearchName, accessToken);

                if (string.IsNullOrEmpty(itemId)) throw new Exception("Arquivo LDP não localizado no ACC.");

                // 2. Download do arquivo para processamento local
                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, itemId, accessToken))
                using (var fs = new FileStream(tempPath, FileMode.Create)) await dl.CopyToAsync(fs);

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using (var package = new ExcelPackage(new FileInfo(tempPath)))
                {
                    var wsLdp = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("LDP", StringComparison.OrdinalIgnoreCase));
                    var wsCusto = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("CUSTO", StringComparison.OrdinalIgnoreCase));

                    if (wsLdp == null || wsCusto == null) throw new Exception("Abas 'LDP' ou 'CUSTO' não encontradas no arquivo!");

                    // 3. Mapeamento Dinâmico dos Cabeçalhos (Acha a posição de cada coluna na Linha 7)
                    var mapLdp = MapearColunas(wsLdp);
                    var mapCusto = MapearColunas(wsCusto);

                    // Lista das colunas técnicas que devem ser espelhadas
                    string[] colunasParaCopiar = {
                        "ITEM", "DISCIPLINE", "N&A DOCUMENT NUMBER", "CLIENT DOCUMENT NUMBER",
                        "DOCUMENT TITLE", "FORMAT", "DOCUMENT STATUS", "CR", "CR-OS",
                        "AREA CODE", "DISCIPLINE ACTIVITY", "DOC TYPE CODE", "SEQ"
                    };

                    // 4. Limpeza Controlada da aba CUSTO (Preservando Estilos)
                    int ultimaLinhaExistenteCusto = wsCusto.Dimension?.End.Row ?? LINHA_CABECALHO;
                    if (ultimaLinhaExistenteCusto >= LINHA_INICIO_DADOS)
                    {
                        // CORREÇÃO: Definir .Value = null apaga o CONTEÚDO mas mantém BORDAS e CORES
                        // Limpamos apenas o intervalo A:N (Colunas 1 a 14)
                        wsCusto.Cells[LINHA_INICIO_DADOS, 1, ultimaLinhaExistenteCusto, COLUNA_LIMITE_TECNICO].Value = null;
                    }

                    // 5. Loop de Cópia de Dados
                    int ultimaLinhaLdp = wsLdp.Dimension?.End.Row ?? LINHA_INICIO_DADOS;
                    int linhaDestino = LINHA_INICIO_DADOS;

                    // Chave primária para validar se a linha da LDP deve ser copiada
                    int colKeyLdp = mapLdp.GetValueOrDefault("N&A DOCUMENT NUMBER", 0);

                    // --- LOOP DE CÓPIA AJUSTADO COM HARD MAPPING ---
                    // 5. Loop de Cópia de Dados (AJUSTADO)
                    // 5. Loop de Cópia de Dados (Ajustado para Copiar Direto entre Abas)
                    for (int r = LINHA_INICIO_DADOS; r <= ultimaLinhaLdp; r++)
                    {
                        string docCode = wsLdp.Cells[r, colKeyLdp].Text?.Trim();
                        if (string.IsNullOrWhiteSpace(docCode)) continue;

                        // --- 🎯 AJUSTE AREA E SEQUENTIAL (COPIA DIRETA ENTRE ABAS) ---
                        // Copia o valor da Coluna Z (26) da LDP para a Coluna K (11) da Custo
                        wsCusto.Cells[linhaDestino, 11].Value = wsLdp.Cells[r, 26].Value;

                        // Copia o valor da Coluna AC (29) da LDP para a Coluna N (14) da Custo
                        wsCusto.Cells[linhaDestino, 14].Value = wsLdp.Cells[r, 29].Value;

                        // --- SINCRONIZAÇÃO DINÂMICA PARA O RESTANTE (ITEM, TÍTULO, ETC) ---
                        foreach (var nomeCol in colunasParaCopiar)
                        {
                            // Ignoramos os dois que já fizemos manualmente acima para não dar erro
                            if (nomeCol.Contains("AREA") || nomeCol.Contains("SEQ")) continue;

                            if (mapLdp.ContainsKey(nomeCol) && mapCusto.ContainsKey(nomeCol))
                            {
                                int cOrigem = mapLdp[nomeCol];
                                int cDestino = mapCusto[nomeCol];

                                if (cDestino <= COLUNA_LIMITE_TECNICO)
                                {
                                    wsCusto.Cells[linhaDestino, cDestino].Value = wsLdp.Cells[r, cOrigem].Value;
                                }
                            }
                        }
                        linhaDestino++;
                    }

                    _logger.LogInformation($"✅ Sincronização LDP->CUSTO: {linhaDestino - LINHA_INICIO_DADOS} documentos copiados. Estilos e Fórmulas (O-AH) preservados.");
                    package.Save();
                }

                // 6. Upload da nova versão para o ACC
                using (var ul = new FileStream(tempPath, FileMode.Open))
                    await _autodeskApi.CreateNewVersionAsync(projectId, itemId, folderId, actualFileName, ul, accessToken);

                return "Sincronização concluída. Formatação e Fórmulas preservadas.";
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Erro na Etapa 3: {ex.Message}");
                throw;
            }
            finally 
            { 
                if (File.Exists(tempPath)) File.Delete(tempPath); 
            }
        }

        // --- MÉTODOS AUXILIARES ---

        private Dictionary<string, int> MapearColunas(ExcelWorksheet ws)
        {
            var dic = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            // Forçamos o scan até a coluna 100 para garantir que pegamos Z (26) e AC (29)
            for (int c = 1; c <= 100; c++)
            {
                string header = ws.Cells[LINHA_CABECALHO, c].Text?.Trim();
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
                new[] { "09 - DESENVOLVIMENTO", "09.10 - GESTÃO" }, 
                new[] { "09 - MODEL DEVELOPING", "09.10 - MANAGEMENT" } 
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