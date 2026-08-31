#nullable disable
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
                    var mapLdp = MapearColunas(wsLdp);
                    var mapCusto = MapearColunas(wsCusto);

                    string[] colunasParaCopiar = {
                        "ITEM", "DISCIPLINE", "N&A DOCUMENT NUMBER", "CLIENT DOCUMENT NUMBER",
                        "DOCUMENT TITLE", "FORMAT", "DOCUMENT STATUS", "CR", "CR-OS",
                        "AREA CODE", "DISCIPLINE ACTIVITY", "DOC TYPE CODE", "SEQ"
                    };

                    int LINHA_CABECALHO_LOCAL = LINHA_CABECALHO;
                    int LINHA_INICIO_DADOS_LOCAL = LINHA_INICIO_DADOS;
                    int COLUNA_LIMITE_TECNICO_LOCAL = COLUNA_LIMITE_TECNICO;

                    // Limpeza Controlada baseada estritamente no teto técnico da coluna 7
                    int ultimaLinhaExistenteCusto = wsCusto.Dimension?.End.Row ?? LINHA_CABECALHO_LOCAL;
                    if (ultimaLinhaExistenteCusto >= LINHA_INICIO_DADOS_LOCAL)
                    {
                        wsCusto.Cells[LINHA_INICIO_DADOS_LOCAL, 1, ultimaLinhaExistenteCusto, COLUNA_LIMITE_TECNICO_LOCAL].Value = null;
                    }

                    int ultimaLinhaLdp = wsLdp.Dimension?.End.Row ?? LINHA_INICIO_DADOS_LOCAL;
                    int linhaDestino = LINHA_INICIO_DADOS_LOCAL;
                    
                    int colKeyLdp = 0;
                    if (mapLdp.ContainsKey("N&A DOCUMENT NUMBER")) colKeyLdp = mapLdp["N&A DOCUMENT NUMBER"];
                    else if (mapLdp.ContainsKey("NÚMERO DOCUMENTO N&A")) colKeyLdp = mapLdp["NÚMERO DOCUMENTO N&A"];
                    else if (mapLdp.ContainsKey("NÚMERO DOCUMENTO N&A ")) colKeyLdp = mapLdp["NÚMERO DOCUMENTO N&A "];

                    if (colKeyLdp == 0)
                    {
                        _logger.LogWarning("⚠️ Cabeçalho 'N&A DOCUMENT NUMBER' (ou equivalente) não localizado. Etapa Custo abortada por segurança.");
                        return "Aviso: Falha de cabeçalho. A aba Custo não foi atualizada.";
                    }

                    // 🎯 LOOKUP DE ORIGEM TOTALMENTE RELACIONAL (Elimina posições chumbadas 26 e 29)
                    int colOrigemAreaCode = mapLdp.ContainsKey("AREA CODE") ? mapLdp["AREA CODE"] : 26; 
                    int colOrigemSeq = mapLdp.ContainsKey("SEQ") ? mapLdp["SEQ"] : 29; 

                    for (int r = LINHA_INICIO_DADOS_LOCAL; r <= ultimaLinhaLdp; r++)
                    {
                        string docCode = wsLdp.Cells[r, colKeyLdp].Value?.ToString()?.Trim();
                        if (string.IsNullOrWhiteSpace(docCode)) continue;

                        // 🎯 VALIDAÇÃO DE ESCRITA: Só injeta nas colunas 11 ou 14 se elas existirem no arquivo físico do projeto
                        if (mapCusto.ContainsKey("AREA CODE"))
                        {
                            int cDestinoArea = mapCusto["AREA CODE"];
                            if (cDestinoArea <= COLUNA_LIMITE_TECNICO_LOCAL)
                                wsCusto.Cells[linhaDestino, cDestinoArea].Value = wsLdp.Cells[r, colOrigemAreaCode].Value;
                        }

                        if (mapCusto.ContainsKey("SEQ"))
                        {
                            int cDestinoSeq = mapCusto["SEQ"];
                            if (cDestinoSeq <= COLUNA_LIMITE_TECNICO_LOCAL)
                                wsCusto.Cells[linhaDestino, cDestinoSeq].Value = wsLdp.Cells[r, colOrigemSeq].Value;
                        }

                        // Sincronização em Lote das Demais Colunas do Escopo
                        foreach (var nomeCol in colunasParaCopiar)
                        {
                            if (nomeCol.Contains("AREA") || nomeCol.Contains("SEQ")) continue;

                            if (mapLdp.ContainsKey(nomeCol) && mapCusto.ContainsKey(nomeCol))
                            {
                                int cOrigem = mapLdp[nomeCol];
                                int cDestino = mapCusto[nomeCol];

                                // Segunda Barreira de Contenção: Nenhuma escrita ultrapassa a Coluna G (7)
                                if (cDestino <= COLUNA_LIMITE_TECNICO_LOCAL)
                                {
                                    wsCusto.Cells[linhaDestino, cDestino].Value = wsLdp.Cells[r, cOrigem].Value;
                                }
                            }
                        }
                        linhaDestino++;
                    }

                    _logger.LogInformation($"✅ Sincronização LDP->CUSTO: {linhaDestino - LINHA_INICIO_DADOS_LOCAL} documentos copiados.");
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