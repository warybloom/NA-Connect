using APS.WebApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace APS.WebApp.Services
{
    public class GrdInfo
    {
        public string TransmittalNumber { get; set; }
        public DateTime EmissionDate { get; set; }
        public string IssuedPurpose { get; set; }
    }

    public class GrdToLdpSyncService
    {
        private readonly AutodeskApiService _autodeskApi;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GrdToLdpSyncService> _logger;

        public GrdToLdpSyncService(AutodeskApiService autodeskApi, IConfiguration configuration, ILogger<GrdToLdpSyncService> logger)
        {
            _autodeskApi = autodeskApi;
            _configuration = configuration;
            _logger = logger;
        }

        // 🎯 MUDANÇA: Adicionado 'string ldpFileId' na assinatura
public async Task<string> SyncGrdsToLdpAsync(string hubId, string projectId, string accessToken, string ldpFileId)
{
    _logger.LogInformation($"📂 [GRD SYNC] Iniciando rastreamento de correspondências para o projeto {projectId}...");

    // 1. Localizar Pasta de GRDs (ISSO MANTÉM, pois estamos buscando GRDs, não a LDP)
    var pathsGrd = new[] {
        new[] { "02 - CORRESPONDENCE", "02.02 - OUTGOING TRANSMITTALS TO CLIENT" },
        new[] { "02 - CORRESPONDÊNCIA", "02.02 - GRD ENVIADA AO CLIENTE" }
    };

    string folderGrdId = null;
    foreach (var path in pathsGrd)
    {
        folderGrdId = await _autodeskApi.FindFolderIdByPathAsync(hubId, projectId, path, accessToken);
        if (!string.IsNullOrEmpty(folderGrdId)) break;
    }

    if (string.IsNullOrEmpty(folderGrdId))
    {
        return "⚠️ Pasta de GRDs não localizada. Verifique a estrutura de pastas 02/02.02.";
    }

    // 2. Mapear arquivos de GRD na pasta (Recursivo)
    var mapaGrdsNoAcc = new List<AccFolderItem>();
    
    // Certifique-se que este método 'ListAllExcelsRecursiveAsync' existe na sua classe
    await ListAllExcelsRecursiveAsync(projectId, folderGrdId, accessToken, mapaGrdsNoAcc);
    
    _logger.LogInformation($"🔍 Encontradas {mapaGrdsNoAcc.Count} planilhas de GRD para análise.");

    // 3. Dicionário de Consolidação (Chave: DocNumber, Valor: Dados da GRD mais recente)
    var consolidadoLdp = new Dictionary<string, GrdInfo>(StringComparer.OrdinalIgnoreCase);

    foreach (var grdFile in mapaGrdsNoAcc)
    {
        try
        {
            // Certifique-se que este método 'ProcessSingleGrdAsync' existe na sua classe
            await ProcessSingleGrdAsync(projectId, grdFile.VersionUrn, accessToken, consolidadoLdp);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"⚠️ Falha ao processar GRD {grdFile.Name}: {ex.Message}");
        }
    }

    if (consolidadoLdp.Count == 0) return "Nenhuma informação de documento encontrada nas GRDs.";

    // 4. Atualizar a LDP (AGORA PASSANDO O ID DIRETO)
    return await UpdateLdpWithConsolidatedData(hubId, projectId, accessToken, consolidadoLdp, ldpFileId);
}


       private async Task ProcessSingleGrdAsync(string projectId, string versionUrn, string accessToken, Dictionary<string, GrdInfo> consolidado)
{
    using (var stream = await _autodeskApi.DownloadFileStreamAsync(projectId, versionUrn, accessToken))
    using (var ms = new MemoryStream())
    {
        await stream.CopyToAsync(ms);
        ms.Position = 0;

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using (var package = new ExcelPackage(ms))
        {
            // 1. Localização da aba da GRD
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("GRD")) 
                     ?? package.Workbook.Worksheets[0];
            
            _logger.LogInformation($"      🔍 Analisando aba: {ws.Name}");

            // 2. Captura e Concatenação do Transmittal (G3+H3+I3)
            string rawT = $"{ws.Cells[3, 7].Text}{ws.Cells[3, 8].Text}{ws.Cells[3, 9].Text}".Trim(); 
            string rawDate = ws.Cells[4, 7].Text?.Trim();

            if (string.IsNullOrEmpty(rawT) || !rawT.Contains("GRD", StringComparison.OrdinalIgnoreCase)) 
            {
                _logger.LogWarning($"      ⚠️ Planilha em G3 não contém 'GRD'. Ignorando.");
                return;
            }

            // Isola o sufixo (ex: GRD-001)
            string cleanTransmittal = rawT.Substring(rawT.IndexOf("GRD", StringComparison.OrdinalIgnoreCase));

            // Conversão de Data — GrdDateParser tenta o parser padrão primeiro e só recorre ao fallback de
            // mês abreviado PT/EN (ex.: "25/Agu/2026") quando o padrão falha; nenhum formato que já
            // funcionava muda de comportamento.
            if (!GrdDateParser.TentarParse(rawDate, out DateTime emissionDate))
            {
                _logger.LogWarning($"      ⚠️ Data '{rawDate}' inválida em G4. Ignorando.");
                return;
            }

            // 3. Varredura de Documentos (Definição de Variáveis de Escopo)
            int colDoc = 3;      // Coluna C (N&A DOCUMENT NUMBER)
            int colPurpose = 7;  // Coluna G (ISSUED POUPURSE)
            int startRow = 24;   // Início da lista de documentos
            int countProcessadosNestaGrd = 0;
            int cabecalhosIgnorados = 0;

            int lastRow = ws.Dimension?.End.Row ?? 500;

            for (int row = startRow; row <= lastRow; row++)
            {
                string docNumber = ws.Cells[row, colDoc].Text?.Trim();
                string purpose = ws.Cells[row, colPurpose].Text?.Trim();

                if (string.IsNullOrEmpty(docNumber)) continue;
                // GRDs convertidas de PDF (aba "Table 1") deslocam o layout: a linha de cabeçalho da
                // lista de documentos ("Codigo N&A" em C / finalidade em G) cai DENTRO da região de dados
                // (>= linha 24) e se repete em cada GRD — era ela que aparecia como "SOBREPOSIÇÃO: Codigo
                // N&A". Cabeçalho nunca entra no consolidado.
                if (GrdLdpWriteback.EhCabecalhoDaListaGrd(docNumber))
                {
                    cabecalhosIgnorados++;
                    continue;
                }

                var novaInfo = new GrdInfo 
                { 
                    TransmittalNumber = cleanTransmittal, 
                    EmissionDate = emissionDate, 
                    IssuedPurpose = purpose 
                };

                // 4. Lógica de Sobreposição (Data Recente + Desempate por Número da GRD)
                if (consolidado.TryGetValue(docNumber, out var existente))
                {
                    bool ehMaisRecente = novaInfo.EmissionDate > existente.EmissionDate;
                    
                    // Se a data for igual, desempata pelo final do número da GRD (ex: 002 > 001)
                    if (!ehMaisRecente && novaInfo.EmissionDate == existente.EmissionDate)
                    {
                        ehMaisRecente = string.Compare(novaInfo.TransmittalNumber, existente.TransmittalNumber, StringComparison.OrdinalIgnoreCase) > 0;
                    }

                    if (ehMaisRecente)
                    {
                        consolidado[docNumber] = novaInfo;
                        _logger.LogInformation($"      🔄 SOBREPOSIÇÃO: {docNumber} atualizado para {cleanTransmittal}.");
                    }
                }
                else
                {
                    consolidado[docNumber] = novaInfo;
                }
                
                countProcessadosNestaGrd++;
            }
            _logger.LogInformation($"      ✅ {countProcessadosNestaGrd} documentos processados na planilha {cleanTransmittal} (cabeçalhos ignorados={cabecalhosIgnorados}).");
        }
    }
}


        // 🎯 MUDANÇA: Adicionado 'string ldpFileId' na assinatura
private async Task<string> UpdateLdpWithConsolidatedData(string hubId, string projectId, string accessToken, Dictionary<string, GrdInfo> dadosGrd, string ldpFileId)
{
    _logger.LogInformation("💾 [GRD SYNC] Atualizando LDP com dados consolidados...");
    string tempPath = Path.Combine(Path.GetTempPath(), $"LDP_GRD_UPD_{Guid.NewGuid()}.xlsx");

    try 
    {
        // -----------------------------------------------------------------------
        // 1. VACINA: RECUPERA DETALHES DA LDP
        // -----------------------------------------------------------------------
        var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
        if (itemDetails == null) throw new Exception("Detalhes da LDP não encontrados para sync de GRD.");

        string versionUrn = itemDetails.TipVersionUrn;
        string parentFolderId = itemDetails.ParentFolderId; // 🎯 OBRIGATÓRIO
        string realFileName = itemDetails.DisplayName;

        // 2. DOWNLOAD DIRETO DA LDP (Versão Correta)
        using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, versionUrn, accessToken))
        using (var fs = new FileStream(tempPath, FileMode.Create)) 
        {
            await dl.CopyToAsync(fs);
        }

        int atualizados = 0;

        // 3. PROCESSAMENTO
        using (var package = new OfficeOpenXml.ExcelPackage(new FileInfo(tempPath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP"))
                     ?? package.Workbook.Worksheets[0];

            var colunas = GrdLdpWriteback.ResolverColunas(ws, GrdLdpWriteback.LinhaCabecalhoLdp);
            _logger.LogInformation(
                "[GRD WRITEBACK][HEADERS] Aba={Aba} | Key={Key}{KeyFallback} | EmissionDate={Emission} | Transmittal={Transmittal} | IssuedPurpose={Purpose}",
                ws.Name, colunas.Key, colunas.KeyPorFallback ? " (fallback)" : string.Empty,
                GrdLdpWriteback.DescreverColuna(colunas.EmissionDate), GrdLdpWriteback.DescreverColuna(colunas.Transmittal), GrdLdpWriteback.DescreverColuna(colunas.IssuedPurpose));

            var resumo = GrdLdpWriteback.AplicarConsolidado(ws, colunas, dadosGrd);
            atualizados = resumo.DocumentosAlterados;
            _logger.LogInformation(
                "[GRD WRITEBACK][RESUMO] Consolidados={Consolidados} | MatchesLdp={Matches} | DocumentosAlterados={Alterados} | SemMatch={SemMatch}",
                dadosGrd.Count, resumo.MatchesLdp, resumo.DocumentosAlterados, resumo.SemMatch);

            if (atualizados > 0) package.Save();
        }

        // 4. UPLOAD CORRIGIDO
        if (atualizados > 0)
        {
            using (var ul = new FileStream(tempPath, FileMode.Open))
                // 🎯 Passa parentFolderId e nome real
                await _autodeskApi.CreateNewVersionAsync(projectId, ldpFileId, parentFolderId, realFileName, ul, accessToken);
            
            return $"✅ Sucesso: {atualizados} documentos atualizados com número de GRD.";
        }
        
        return "Nenhuma atualização de GRD necessária.";
    }
    catch (Exception ex)
    {
        _logger.LogError($"❌ Erro GRD Sync: {ex.Message}");
        throw;
    }
    finally 
    { 
        if (File.Exists(tempPath)) File.Delete(tempPath); 
    }
}


        private async Task ListAllExcelsRecursiveAsync(string projectId, string folderId, string token, List<AccFolderItem> results)
        {
            // Obtém o conteúdo da pasta atual (arquivos e pastas)
            var contents = await _autodeskApi.GetFolderContentsAsync(projectId, folderId, token);
            
            foreach (var item in contents)
            {
                if (item.Type == "folders") 
                {
                    // Se encontrou uma subpasta (ex: "GRD-001"), entra nela recursivamente
                    _logger.LogInformation($"   Subindo um nível: Entrando na pasta '{item.Name}'");
                    await ListAllExcelsRecursiveAsync(projectId, item.Id, token, results);
                }
                else if (item.Type == "items" && (item.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || item.Name.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase))) 
                {
                    // Se encontrou um Excel, adiciona à lista de análise
                    _logger.LogInformation($"   Arquivo encontrado para análise: {item.Name}");
                    results.Add(item);
                }
            }
        }
    }

    internal sealed class GrdLdpColunas
    {
        public int Key { get; set; } = GrdLdpWriteback.ColunaChavePadrao;
        public bool KeyPorFallback { get; set; } = true;
        public int Transmittal { get; set; } = -1;
        public int EmissionDate { get; set; } = -1;
        public int IssuedPurpose { get; set; } = -1;
    }

    internal readonly record struct GrdWritebackResumo(int MatchesLdp, int DocumentosAlterados, int SemMatch);

    /// <summary>
    /// Parte pura (sem ACC) do writeback GRD -> LDP da Etapa 04: resolução tolerante dos cabeçalhos da
    /// LDP (inclusive as grafias históricas do template: "TRANMISTAL NUMBER", "EMISSION DATA",
    /// "ISSUED POUPURSE") e aplicação dos 3 campos por documento.
    /// </summary>
    internal static class GrdLdpWriteback
    {
        public const int LinhaCabecalhoLdp = 7;
        public const int LinhaInicioDadosLdp = 8;
        public const int ColunaChavePadrao = 4;

        private static readonly string[] AliasesKey = { "N&A DOCUMENT NUMBER" };
        private static readonly string[] AliasesEmission = { "EMISSION DATA", "EMISSION DATE", "DATA DE EMISSAO", "DATA EMISSAO" };
        private static readonly string[] AliasesPurpose = { "ISSUED POUPURSE", "ISSUED PURPOSE", "FINALIDADE" };
        private static readonly string[] AliasesTransmittal = { "TRANSMITTAL", "TRANMISTAL", "TRANSMITAL", "REMESSA" };
        // Alias genérico já suportado antes ("GRD"): só usado se nenhum alias específico de transmittal existir.
        private static readonly string[] AliasesTransmittalGenericos = { "GRD" };

        private static readonly string[] CabecalhosListaGrd = { "CODIGO N&A", "N&A DOCUMENT NUMBER", "N&A CODE" };

        public static string Normalizar(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
            var decomposto = texto.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(decomposto.Length);
            foreach (var ch in decomposto)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim().ToUpperInvariant();
        }

        /// <summary>Linha de cabeçalho da lista de documentos da GRD ("Codigo N&A", "Código N&A", ...).</summary>
        public static bool EhCabecalhoDaListaGrd(string valorColunaDocumento)
        {
            string n = Normalizar(valorColunaDocumento);
            return n.Length > 0 && CabecalhosListaGrd.Any(c => n.Contains(c, StringComparison.Ordinal));
        }

        public static GrdLdpColunas ResolverColunas(ExcelWorksheet ws, int linhaCabecalho)
        {
            var colunas = new GrdLdpColunas();
            int ultimaColuna = ws.Dimension?.End.Column ?? 0;
            int transmittalGenerico = -1;

            // Primeira ocorrência vence; uma coluna nunca é atribuída a dois campos.
            for (int c = 1; c <= ultimaColuna; c++)
            {
                string h = Normalizar(ws.Cells[linhaCabecalho, c].Text);
                if (h.Length == 0) continue;

                if (colunas.KeyPorFallback && ContemAlgum(h, AliasesKey)) { colunas.Key = c; colunas.KeyPorFallback = false; }
                else if (colunas.EmissionDate == -1 && ContemAlgum(h, AliasesEmission)) colunas.EmissionDate = c;
                else if (colunas.IssuedPurpose == -1 && ContemAlgum(h, AliasesPurpose)) colunas.IssuedPurpose = c;
                else if (colunas.Transmittal == -1 && ContemAlgum(h, AliasesTransmittal)) colunas.Transmittal = c;
                else if (transmittalGenerico == -1 && ContemAlgum(h, AliasesTransmittalGenericos)) transmittalGenerico = c;
            }

            if (colunas.Transmittal == -1) colunas.Transmittal = transmittalGenerico;
            return colunas;
        }

        public static string DescreverColuna(int coluna) => coluna == -1 ? "NAO_ENCONTRADA" : coluna.ToString();

        public static GrdWritebackResumo AplicarConsolidado(ExcelWorksheet ws, GrdLdpColunas colunas, IReadOnlyDictionary<string, GrdInfo> dadosGrd)
        {
            int matches = 0, alterados = 0;
            var chavesComMatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int ultimaLinha = ws.Dimension?.End.Row ?? 0;

            for (int r = LinhaInicioDadosLdp; r <= ultimaLinha; r++)
            {
                string doc = ws.Cells[r, colunas.Key].Text?.Trim();
                if (string.IsNullOrEmpty(doc) || !dadosGrd.TryGetValue(doc, out var info)) continue;

                matches++;
                chavesComMatch.Add(doc);
                if (AplicarGrdNaLinha(ws, r, colunas, info)) alterados++;
            }

            int semMatch = dadosGrd.Keys.Count(k => !chavesComMatch.Contains(k));
            return new GrdWritebackResumo(matches, alterados, semMatch);
        }

        /// <summary>Retorna true se QUALQUER um dos 3 campos da linha mudou (contagem por documento).</summary>
        public static bool AplicarGrdNaLinha(ExcelWorksheet ws, int r, GrdLdpColunas colunas, GrdInfo info)
        {
            bool documentoAlterado = false;

            if (colunas.Transmittal != -1)
            {
                string valorAtual = ws.Cells[r, colunas.Transmittal].Text?.Trim();
                if (!string.Equals(valorAtual, info.TransmittalNumber, StringComparison.OrdinalIgnoreCase))
                {
                    ws.Cells[r, colunas.Transmittal].Value = info.TransmittalNumber;
                    documentoAlterado = true;
                }
            }

            if (colunas.EmissionDate != -1)
            {
                var celulaData = ws.Cells[r, colunas.EmissionDate];
                bool mesmaData = celulaData.Value is DateTime dataAtual
                    ? dataAtual.Date == info.EmissionDate.Date
                    : GrdDateParser.TentarParse(celulaData.Text?.Trim(), out DateTime dataTexto) && dataTexto.Date == info.EmissionDate.Date;

                if (!mesmaData)
                {
                    celulaData.Value = info.EmissionDate.Date;
                    if (string.IsNullOrWhiteSpace(celulaData.Style.Numberformat.Format) || celulaData.Style.Numberformat.Format == "General")
                        celulaData.Style.Numberformat.Format = "dd/MM/yyyy";
                    documentoAlterado = true;
                }
            }

            if (colunas.IssuedPurpose != -1)
            {
                string valorAtual = ws.Cells[r, colunas.IssuedPurpose].Text?.Trim();
                string novoValor = info.IssuedPurpose?.Trim() ?? string.Empty;
                if (!string.Equals(valorAtual, novoValor, StringComparison.OrdinalIgnoreCase))
                {
                    ws.Cells[r, colunas.IssuedPurpose].Value = novoValor;
                    documentoAlterado = true;
                }
            }

            return documentoAlterado;
        }

        private static bool ContemAlgum(string cabecalhoNormalizado, string[] aliases) =>
            aliases.Any(a => cabecalhoNormalizado.Contains(a, StringComparison.Ordinal));
    }
}