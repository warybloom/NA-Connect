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
        
        // LIMITADOR: O robô só pode mexer até a coluna 7. 
        // Da coluna 8 em diante contém fórmulas e serão preservadas.
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
        /// Orquestra o processo de sincronização entre as abas LDP e CUSTO
        /// </summary>
        public async Task<string> ExecuteFullSyncProcessAsync(string hubId, string projectId, string accessToken, string ldpFileId)
        {
            _logger.LogInformation("📊 [ETAPA 6/11] Sincronizando LDP -> CUSTO (Preservando Estilos e Fórmulas O-AH)...");
            string tempPath = Path.Combine(Path.GetTempPath(), $"SYNC_CUSTO_{Guid.NewGuid()}.xlsx");

            try
            {
                // -----------------------------------------------------------------------
                // 1. VACINA: RECUPERA DETALHES (Versão para Download e PASTA para Upload)
                // -----------------------------------------------------------------------
                var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
                if (itemDetails == null) throw new Exception("Detalhes do arquivo não encontrados (Item/Folder).");

                string versionUrn = itemDetails.TipVersionUrn;
                string parentFolderId = itemDetails.ParentFolderId; // 🎯 OBRIGATÓRIO PARA O UPLOAD
                string realFileName = itemDetails.DisplayName;

                // 2. DOWNLOAD DIRETO (Usando a versão correta)
                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, versionUrn, accessToken))
                using (var fs = new FileStream(tempPath, FileMode.Create)) await dl.CopyToAsync(fs);

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using (var package = new ExcelPackage(new FileInfo(tempPath)))
                {
                    var wsLdp = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("LDP", StringComparison.OrdinalIgnoreCase));
                    var wsCusto = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("CUSTO", StringComparison.OrdinalIgnoreCase));

                    if (wsLdp == null || wsCusto == null) throw new Exception("Abas 'LDP' ou 'CUSTO' não encontradas no arquivo!");

                    // 3. Mapeamento Dinâmico dos Cabeçalhos
                    var mapLdp = MapearColunas(wsLdp);
                    var mapCusto = MapearColunas(wsCusto);

                    string[] colunasParaCopiar = {
                        "ITEM", "DISCIPLINE", "N&A DOCUMENT NUMBER", "CLIENT DOCUMENT NUMBER",
                        "DOCUMENT TITLE", "FORMAT", "DOCUMENT STATUS", "CR", "CR-OS",
                        "AREA CODE", "DISCIPLINE ACTIVITY", "DOC TYPE CODE", "SEQ"
                    };

                    // 4. Limpeza Controlada da aba CUSTO
                    int LINHA_CABECALHO_LOCAL = 7;
                    int LINHA_INICIO_DADOS_LOCAL = 8;
                    int COLUNA_LIMITE_TECNICO_LOCAL = 7; // Limite restabelecido para 7

                    int ultimaLinhaExistenteCusto = wsCusto.Dimension?.End.Row ?? LINHA_CABECALHO_LOCAL;
                    if (ultimaLinhaExistenteCusto >= LINHA_INICIO_DADOS_LOCAL)
                    {
                        // Limpa conteúdo mantendo formatação apenas até a coluna 7
                        wsCusto.Cells[LINHA_INICIO_DADOS_LOCAL, 1, ultimaLinhaExistenteCusto, COLUNA_LIMITE_TECNICO_LOCAL].Value = null;
                    }

                    // 5. Loop de Cópia de Dados
                    int ultimaLinhaLdp = wsLdp.Dimension?.End.Row ?? LINHA_INICIO_DADOS_LOCAL;
                    int linhaDestino = LINHA_INICIO_DADOS_LOCAL;
                    int colKeyLdp = mapLdp.GetValueOrDefault("N&A DOCUMENT NUMBER", 0);

                    for (int r = LINHA_INICIO_DADOS_LOCAL; r <= ultimaLinhaLdp; r++)
                    {
                        string docCode = wsLdp.Cells[r, colKeyLdp].Text?.Trim();
                        if (string.IsNullOrWhiteSpace(docCode)) continue;

                        // Cópia Flexível de Colunas Específicas: Agora só escreve se o mapeamento indicar que a coluna de destino é <= 7
                        if (mapCusto.ContainsKey("AREA CODE"))
                        {
                            int cDestinoArea = mapCusto["AREA CODE"];
                            if (cDestinoArea <= COLUNA_LIMITE_TECNICO_LOCAL)
                                wsCusto.Cells[linhaDestino, cDestinoArea].Value = wsLdp.Cells[r, 26].Value;
                        }

                        if (mapCusto.ContainsKey("SEQ"))
                        {
                            int cDestinoSeq = mapCusto["SEQ"];
                            if (cDestinoSeq <= COLUNA_LIMITE_TECNICO_LOCAL)
                                wsCusto.Cells[linhaDestino, cDestinoSeq].Value = wsLdp.Cells[r, 29].Value;
                        }

                        // Sincronização Dinâmica do restante
                        foreach (var nomeCol in colunasParaCopiar)
                        {
                            if (nomeCol.Contains("AREA") || nomeCol.Contains("SEQ")) continue;

                            if (mapLdp.ContainsKey(nomeCol) && mapCusto.ContainsKey(nomeCol))
                            {
                                int cOrigem = mapLdp[nomeCol];
                                int cDestino = mapCusto[nomeCol];

                                // Garantia dupla de que NENHUMA coluna passará do limite 7
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

                // 6. UPLOAD CORRIGIDO
                using (var ul = new FileStream(tempPath, FileMode.Open))
                {
                    // 🎯 AQUI: Passamos parentFolderId e o nome real
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