using AccIntegration.WebApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace APS.WebApp.Services
{
    public class LdpUpdateResult
    {
        public int DocumentosEncontradosNaLdp { get; set; }
        public int DocumentosAtualizados { get; set; }
        public List<string> DocumentosNaoEncontradosNoDb { get; set; } = new List<string>();
        public string NovaVersaoUrn { get; set; } = string.Empty;
        public string SummaryMessage { get; set; } = string.Empty;
        public int RegistrosTimesheetAdicionados { get; set; }
    }

    public class FileAttributeUpdate
    {
        public string Urn { get; set; }
        public Dictionary<string, string> Attributes { get; set; }
    }

    public class LdpUpdateService
    {
        private class HorasInfo
        {
            public decimal? HorasRealizadas { get; set; }
        }

        private readonly ApplicationDbContext _context;
        private readonly AutodeskApiService _autodeskApi;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LdpUpdateService> _logger;
        private readonly AccMetadataService _metadataService;
        private readonly AccReviewWorkflowService _workflowService;
        private readonly AccStatusResolverService _statusResolver;
        private readonly AccReviewWorkflowService _workflowApi;
        
        // Mesma "Visão" do Dump: LDP linha 8, Colunas X e Y
        private const int LINHA_FILTRO_LDP = 8;
        private const int COLUNA_X_CR = 24;
        private const int COLUNA_Y_CROS = 25;
        public LdpUpdateService(
        
        ApplicationDbContext context, 
        AutodeskApiService autodeskApi, 
        IConfiguration configuration, 
        ILogger<LdpUpdateService> logger,
        AccMetadataService metadataService,
        AccReviewWorkflowService workflowApi,
        AccStatusResolverService statusResolver,
        AccReviewWorkflowService workflowService)
        {
            _context = context;
            _autodeskApi = autodeskApi;
            _configuration = configuration;
            _logger = logger;
            _workflowService = workflowService;
            _metadataService = metadataService;
            _workflowApi = workflowApi;
        _statusResolver = statusResolver;
        }

        /// <summary>
        /// Lê o valor de uma célula com segurança, verificando merges e fazendo Trim.
        /// </summary>
        private string GetSafeCellValue(ExcelWorksheet sheet, int row, int col)
        {
            // 1. Tenta ler o valor direto
            var cell = sheet.Cells[row, col];
            string value = cell.Text?.Trim();

            if (!string.IsNullOrEmpty(value)) return value;

            // 2. Se vazio, verifica se é uma célula mesclada (Merge)
            // Às vezes o Excel relata merge onde visualmente parece célula única
            if (cell.Merge)
            {
                var mergeAddress = sheet.MergedCells[row, col];
                if (mergeAddress != null)
                {
                    var firstCell = new ExcelAddress(mergeAddress).Start;
                    // Retorna o valor da célula "mãe" da mesclagem
                    return sheet.Cells[firstCell.Row, firstCell.Column].Text?.Trim();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Verifica se o valor da célula corresponde a uma das opções (PT/EN), ignorando maiúsculas/minúsculas.
        /// </summary>
        private bool ColumnMatch(string value, params string[] options)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return options.Any(opt => value.Contains(opt, StringComparison.OrdinalIgnoreCase));
        }
public async Task<LdpUpdateResult> UpdateLdpFromDatabaseAsync(string hubId, string projectId, string accessToken)
{
    _logger.LogWarning($"--- [ETAPA 1/7] REVERSE SYNC (BANCO HH + REV. COM TRAVA ACC) ---");

    var result = new LdpUpdateResult();
    string tempFilePath = Path.Combine(Path.GetTempPath(), $"LDP_STEP1_{Guid.NewGuid()}.xlsx");
    
    var pathsControle = new[] { 
        new[] { "09 - DESENVOLVIMENTO", "09.10 - GESTÃO" }, 
        new[] { "09 - MODEL DEVELOPING", "09.10 - MANAGEMENT" } 
    };

    try
    {
        // 1. LOCALIZAR E BAIXAR O ARQUIVO EXCEL DA LDP
        string ldpSearchName = GetLdpFileNameFromConfig(projectId);
        var folderId = await FindFolderIdByAlternativePathsAsync(hubId, projectId, pathsControle, accessToken);
        var (itemId, actualLdpFileName) = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderId, ldpSearchName, accessToken);
        
        if (string.IsNullOrEmpty(itemId)) throw new Exception("Arquivo LDP não encontrado no ACC.");

        using (var downloadStream = await _autodeskApi.DownloadFileStreamAsync(projectId, itemId, accessToken))
        using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write)) {
            await downloadStream.CopyToAsync(fileStream);
        }

        // 2. LER FILTROS NA PLANILHA (X8 e Y8)
        string valCR = "";
        string valCROS = "";
        using (var package = new ExcelPackage(new FileInfo(tempFilePath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
            valCR = ws.Cells[8, 24].Text?.Trim();   // Coluna X
            valCROS = ws.Cells[8, 25].Text?.Trim(); // Coluna Y
        }

        _logger.LogInformation($"🔍 Visão extraída para Sync: Projeto='{valCR}' AND Doc1='{valCROS}'");

        // 3. INDEXAR ARQUIVOS ATIVOS NO ACC (CORREÇÃO DO ERRO DE DUPLICIDADE)
        var arquivosAtivosNoAcc = await _autodeskApi.IndexarArquivosProjetoAsync(hubId, projectId, accessToken);
        
        // Usamos GroupBy para evitar o erro "item with the same key has already been added"
        var mapaArquivosVivos = arquivosAtivosNoAcc
            .GroupBy(f => Path.GetFileNameWithoutExtension(f.FileName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().VersionUrn, StringComparer.OrdinalIgnoreCase);

        // 4. BUSCAR DADOS DO BANCO (SQL PURO)
        var sql = "SELECT * FROM [tbTimeSheetRealizadoDocumento] WHERE Projeto = {0} AND doc1 = {1}";
        var registrosBanco = await _context.TbTimeSheetRealizadoDocumentos
            .FromSqlRaw(sql, valCR, valCROS)
            .AsNoTracking()
            .ToListAsync();

        var dadosHorasBanco = registrosBanco.GroupBy(t => t.DocumentoCodigo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Trabalhadas.GetValueOrDefault()), StringComparer.OrdinalIgnoreCase);

        // 5. BUSCAR ATRIBUTOS DE REVISÃO NO ACC
        var allUrnsAtivas = arquivosAtivosNoAcc.Select(f => f.VersionUrn).Distinct().ToList();
        var accAttributesMap = await _autodeskApi.GetBatchAttributesAsync(projectId, allUrnsAtivas, accessToken);

        // 6. PROCESSAR E ATUALIZAR O EXCEL
        using (var package = new ExcelPackage(new FileInfo(tempFilePath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];

            int headerRow = 7;
            int startRow = 8;
            int colRev = 14;      // Coluna N
            int colNA_Number = 4; // Coluna D
            int colRealized = -1;

            // Localiza a coluna de HH REALIZADO dinamicamente
            for (int col = 1; col <= ws.Dimension.End.Column; col++) {
                string h = ws.Cells[headerRow, col].Text?.Trim().ToUpper();
                if (h.Contains("HH REALIZADO") || h.Contains("ACTUAL HH")) { colRealized = col; break; }
            }

            for (int row = startRow; row <= ws.Dimension.End.Row; row++)
            {
                string docCodeExcel = ws.Cells[row, colNA_Number].Text?.Trim();
                if (string.IsNullOrWhiteSpace(docCodeExcel)) continue;

                bool atualizouLinha = false;

                        // --- CAPTURA DE REVISÃO TÉCNICA (VERSÃO V44 FUNCIONAL) ---
                        if (mapaArquivosVivos.TryGetValue(docCodeExcel, out string urnAtiva))
                        {
                            if (accAttributesMap.TryGetValue(urnAtiva, out var attrs))
                            {
                                // Pega o primeiro atributo que tenha "REV" no nome
                                var revAttr = attrs.FirstOrDefault(a => a.Key.Equals("REV", StringComparison.OrdinalIgnoreCase) ||
                                                                        a.Key.Equals("REV.", StringComparison.OrdinalIgnoreCase));

                                string valorParaGravar = revAttr.Value;

                                if (!string.IsNullOrEmpty(valorParaGravar))
                                {
                                    // FILTRO: Se o texto for longo e tiver ":", é uma data de sistema da Autodesk.
                                    // Revisão técnica é curta (0, 1, A...). Se for data, não escrevemos nada.
                                    if (valorParaGravar.Contains(":") && valorParaGravar.Length > 10)
                                    {
                                        _logger.LogWarning($"⚠️ Data detectada no campo REV para {docCodeExcel} - Ignorando.");
                                    }
                                    else
                                    {
                                        ws.Cells[row, colRev].Value = valorParaGravar;
                                        atualizouLinha = true;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Se o arquivo não está no ACC, limpamos a revisão (evita o erro "edgar")
                            ws.Cells[row, colRev].Value = null;
                        }

                        // --- ATUALIZAÇÃO DE HH (BANCO DE DADOS) ---
                        if (colRealized > 0 && dadosHorasBanco.TryGetValue(docCodeExcel, out var totalHoras))
                {
                    ws.Cells[row, colRealized].Value = totalHoras;
                    atualizouLinha = true;
                }

                if (atualizouLinha) result.DocumentosAtualizados++;
            }
            package.Save();
        }

        // 7. UPLOAD
        using (var uploadStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read)) {
            await _autodeskApi.CreateNewVersionAsync(projectId, itemId, folderId, actualLdpFileName, uploadStream, accessToken);
        }

        result.SummaryMessage = $"Sucesso Etapa 1! {result.DocumentosAtualizados} itens sincronizados.";
        return result;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "❌ Erro na Etapa 1 (Reverse Sync)");
        return new LdpUpdateResult { SummaryMessage = ex.Message };
    }
    finally { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); }
}




        public async Task<string> UpdateWorkflowStatusAsync(string hubId, string projectId, string accessToken, string userId)
        {
            _logger.LogInformation("🔄 [ETAPA 2/7] SINCRONIZANDO ETAPAS TÉCNICAS (EXECUTOR/VERIFICADOR)...");
            string tempFilePath = Path.Combine(Path.GetTempPath(), $"LDP_STATUS_{Guid.NewGuid()}.xlsx");

            try
            {
                // --- 1. PREPARAÇÃO DAS VARIÁVEIS (O que estava faltando) ---

                // Busca arquivos do ACC
                var rawAccMap = await _autodeskApi.GetAllProjectFilesAsync(hubId, projectId, accessToken);

                // Cria a lista normalizada para busca rápida (filesInAccNorm)
                var filesInAccNorm = rawAccMap.Keys
                    .Select(name => NormalizeKeyOnly(name)) // Usa a nova função de limpeza
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                _logger.LogInformation($"📂 Arquivos Físicos validados: {filesInAccNorm.Count}");

                // Busca o mapa de workflow atual
                var rawWorkflowMap = await _workflowService.GetProjectWorkflowMapAsync(projectId, accessToken);

                // Cria o dicionário normalizado (workflowMapNorm)
                var workflowMapNorm = rawWorkflowMap
                    .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                    .GroupBy(x => NormalizeKeyOnly(x.Key))
                    .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

                // --- 2. DOWNLOAD E PROCESSAMENTO ---

                string ldpSearchName = GetLdpFileNameFromConfig(projectId);
                var folderId = await FindFolderIdByAlternativePathsAsync(hubId, projectId, new[] {
            new[] { "09 - DESENVOLVIMENTO", "09.10 - GESTÃO" },
            new[] { "09 - MODEL DEVELOPING", "09.10 - MANAGEMENT" }
        }, accessToken);

                var (itemId, actualFileName) = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderId, ldpSearchName, accessToken);

                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, itemId, accessToken))
                using (var fs = new FileStream(tempFilePath, FileMode.Create)) await dl.CopyToAsync(fs);

                int totalAtualizados = 0; // Variável contadora declarada aqui

                using (var package = new ExcelPackage(new FileInfo(tempFilePath)))
                {
                    var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];

                    for (int row = 8; row <= ws.Dimension.End.Row; row++)
                    {
                        // Leitura das 5 colunas para montar a chave
                        string crOs = ws.Cells[row, 25].Text?.Trim(); // Y
                        string area = ws.Cells[row, 26].Text?.Trim(); // Z
                        string discipline = ws.Cells[row, 27].Text?.Trim(); // AA
                        string docType = ws.Cells[row, 28].Text?.Trim(); // AB
                        string seq = ws.Cells[row, 29].Text?.Trim(); // AC

                        if (string.IsNullOrEmpty(crOs) || string.IsNullOrEmpty(seq)) continue;

                        // Monta a chave única e limpa
                        string uniqueDocKey = $"{crOs}-{area}-{discipline}-{docType}-{seq}";
                        string keyExcel = NormalizeKeyOnly(uniqueDocKey);

                        if (string.IsNullOrEmpty(keyExcel)) continue;

                        string valorAtual = ws.Cells[row, 17].Text?.Trim().ToUpper();

                        // Trava de Soberania
                        if (valorAtual == "APROVADO" || valorAtual == "REJEITADO" || valorAtual == "APROV. C/ OBS") continue;

                        // Verifica existência física
                        if (filesInAccNorm.Contains(keyExcel))
                        {
                            // Verifica se tem workflow ativo
                            if (workflowMapNorm.TryGetValue(keyExcel, out string? etapaAtiva))
                            {
                                string statusUpper = etapaAtiva.ToUpper();
                                if (valorAtual != statusUpper)
                                {
                                    ws.Cells[row, 17].Value = statusUpper;
                                    totalAtualizados++;
                                }
                            }
                            else
                            {
                                // Existe mas sem fluxo -> Hífen
                                if (valorAtual != "-") ws.Cells[row, 17].Value = "-";
                            }
                        }
                        else
                        {
                            // Arquivo fantasma -> Limpa status
                            if (!string.IsNullOrEmpty(valorAtual)) ws.Cells[row, 17].Value = null;
                        }
                    }
                    package.Save();
                }

                if (totalAtualizados > 0)
                {
                    using (var ul = new FileStream(tempFilePath, FileMode.Open))
                        await _autodeskApi.CreateNewVersionAsync(projectId, itemId, folderId, actualFileName, ul, accessToken);
                    return $"✅ Sucesso! {totalAtualizados} linhas atualizadas (Etapa 2 Completa).";
                }

                return "Nada para atualizar na Etapa 2.";
            }
            catch (Exception ex) { throw; }
            finally { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); }
        }




        public async Task<string> RunApprovalVerdictSyncAsync(string hubId, string projectId, string accessToken, string userId)
        {
            _logger.LogInformation($"🏁 [ETAPA 5/7] SINCRO DE VEREDITOS (FULL FIX) - USER: {userId}");
            string tempPath = Path.Combine(Path.GetTempPath(), $"LDP_VERDITO_{Guid.NewGuid()}.xlsx");

            try
            {
                // --- 1. PREPARAÇÃO DAS VARIÁVEIS (O que estava faltando) ---

                // Busca todas as Reviews do projeto
                var rawReviews = await _workflowApi.GetProjectReviewsAsync(projectId, accessToken, userId);
                var allVersions = new List<JToken>();

                // Busca versões detalhadas para cada review
                foreach (var rev in rawReviews)
                {
                    string? rid = rev["id"]?.ToString();
                    if (string.IsNullOrEmpty(rid)) continue;

                    // Aqui buscamos as versões dentro da review
                    var versions = await _workflowApi.GetReviewVersionsAsync(projectId, rid, accessToken, userId);
                    foreach (var v in versions)
                    {
                        if (v is JObject obj)
                        {
                            obj["reviewId"] = rid; // Vincula ID da review na versão
                            allVersions.Add(obj);
                        }
                    }
                }

                // --- 2. DOWNLOAD E PROCESSAMENTO ---

                var folderId = await FindFolderIdByAlternativePathsAsync(hubId, projectId, new[] {
            new[] { "09 - DESENVOLVIMENTO", "09.10 - GESTÃO" },
            new[] { "09 - MODEL DEVELOPING", "09.10 - MANAGEMENT" }
        }, accessToken);

                var (itemId, actualFileName) = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderId, GetLdpFileNameFromConfig(projectId), accessToken);

                using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, itemId, accessToken))
                using (var fs = new FileStream(tempPath, FileMode.Create)) await dl.CopyToAsync(fs);

                int totalAprovados = 0; // Variável contadora declarada aqui

                using (var package = new ExcelPackage(new FileInfo(tempPath)))
                {
                    var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];

                    for (int row = 8; row <= ws.Dimension.End.Row; row++)
                    {
                        // Leitura detalhada (Y-AC)
                        string crOs = ws.Cells[row, 25].Text?.Trim();
                        string area = ws.Cells[row, 26].Text?.Trim();
                        string discipline = ws.Cells[row, 27].Text?.Trim();
                        string docType = ws.Cells[row, 28].Text?.Trim();
                        string seq = ws.Cells[row, 29].Text?.Trim();

                        if (string.IsNullOrEmpty(crOs) || string.IsNullOrEmpty(seq)) continue;

                        string uniqueDocKey = $"{crOs}-{area}-{discipline}-{docType}-{seq}";

                        // Usa a nova função de limpeza segura
                        string keyLimpa = NormalizeKeyOnly(uniqueDocKey);

                        // Chama o Resolver passando a chave limpa e as listas carregadas lá em cima
                        var statusCalculado = _statusResolver.ResolverVereditoFinal(keyLimpa, rawReviews, allVersions);

                        if (statusCalculado != null)
                        {
                            string valorLdp = ws.Cells[row, 17].Text?.Trim().ToUpper();
                            if (valorLdp != statusCalculado)
                            {
                                _logger.LogInformation($"   ✅ [VEREDITO] {uniqueDocKey}: '{valorLdp}' -> '{statusCalculado}'");
                                ws.Cells[row, 17].Value = statusCalculado;
                                totalAprovados++;
                            }
                        }
                    }
                    package.Save();
                }

                if (totalAprovados > 0)
                {
                    using (var ul = new FileStream(tempPath, FileMode.Open))
                        await _autodeskApi.CreateNewVersionAsync(projectId, itemId, folderId, actualFileName, ul, accessToken);
                    return $"✅ Sucesso! {totalAprovados} vereditos sincronizados.";
                }

                return "Nada para atualizar (Vereditos em dia).";
            }
            catch (Exception ex) { throw; }
            finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
        }
        private string NormalizeDoc(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // 1. Pega o nome sem extensão e coloca em Maiúsculo
            string name = Path.GetFileNameWithoutExtension(input).ToUpperInvariant();

            // 2. Padroniza separadores para facilitar a remoção de sufixos
            name = name.Replace("_", "-").Replace(" ", "-");

            // 3. Remove os sufixos de revisão (ex: -R01, -REV02, -V1) 
            // Fazemos isso antes da limpeza final para não sobrar números da revisão
            name = Regex.Replace(name, @"-R\d+$", "");
            name = Regex.Replace(name, @"-REV\d+$", "");
            name = Regex.Replace(name, @"-V\d+$", "");

            // 4. LIMPEZA FINAL (O "Pulo do Gato"):
            // Remove TUDO o que não for Letra ou Número.
            // Ex: "CON005-000-AQM-3D-003" vira "CON005000AQM3D003"
            return Regex.Replace(name, @"[^A-Z0-9]", "");
        }

        // --- NOVO MÉTODO PARA LIMPAR CHAVES DO EXCEL (SEM CORTAR O PONTO) ---
        private string NormalizeKeyOnly(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string upper = input.ToUpperInvariant();
            return Regex.Replace(upper, @"[^A-Z0-9]", "");
        }


        private string GetProjectSetting(string projectId, string key)
{
    var val = _configuration[$"ProjectSettings:{projectId}:{key}"];
    return string.IsNullOrEmpty(val) ? _configuration[$"ProjectSettings:Default:{key}"] : val;
}

/// <summary>
        /// Verifica arquivos que existem fisicamente no ACC mas NÃO constam na planilha LDP.
        /// </summary>
        public async Task<List<string>> CheckFilesMissingInLdpAsync(string hubId, string projectId, string accessToken)
        {
            _logger.LogInformation("Iniciando auditoria: Arquivos ACC vs LDP...");

            // 1. Obter todos os arquivos do ACC (Nome -> URNs)
            // O método GetAllProjectFilesAsync já remove a extensão (.pdf, .dwg) e retorna o nome limpo
            var accFilesMap = await _autodeskApi.GetAllProjectFilesAsync(hubId, projectId, accessToken);
            var accFileNames = accFilesMap.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation($"Total de arquivos únicos encontrados no ACC: {accFileNames.Count}");

            // 2. Baixar e Ler a LDP para pegar os códigos esperados
            string ldpVersionUrn = await _autodeskApi.FindLdpFileUrnAsync(hubId, projectId, accessToken);
            string tempFilePath = await _autodeskApi.DownloadFileToTempPathAsync(projectId, ldpVersionUrn, accessToken);
            
            var ldpCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var package = new ExcelPackage(new FileInfo(tempFilePath)))
                {
                    var ldpWorksheet = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals("LDP", StringComparison.OrdinalIgnoreCase));
                    if (ldpWorksheet == null || ldpWorksheet.Dimension == null) 
                        throw new Exception("Aba 'LDP' inválida ou não encontrada.");

                    // --- Lógica simplificada de detecção de cabeçalho (Reaproveitando a lógica robusta) ---
                    int headerRow = -1;
                    int colKey = -1;

                    // Busca a linha de cabeçalho
                    for (int r = 1; r <= Math.Min(20, ldpWorksheet.Dimension.End.Row); r++)
                    {
                        for (int c = 1; c <= Math.Min(50, ldpWorksheet.Dimension.End.Column); c++)
                        {
                            var val = GetSafeCellValue(ldpWorksheet, r, c);
                            // Tenta achar "CR" ou "NÚMERO DOCUMENTO CLIENTE" ou "CÓDIGO"
                            if (val.Equals("CR", StringComparison.OrdinalIgnoreCase) || 
                                val.Contains("DOCUMENTO CLIENTE", StringComparison.OrdinalIgnoreCase))
                            {
                                headerRow = r;
                                break;
                            }
                        }
                        if (headerRow != -1) break;
                    }

                    if (headerRow == -1) headerRow = 7; // Fallback padrão

                    // Busca a coluna CHAVE (A que contém o nome do arquivo)
                    // Geralmente é uma combinação ou o NÚMERO DOCUMENTO CLIENTE.
                    // Vou procurar por colunas comuns que identificam o documento.
                    string[] possibleHeaders = { "NÚMERO DOCUMENTO CLIENTE", "CODIGO", "CÓDIGO", "DOC. CLIENTE", "CR" };
                    
                    // Tenta achar a coluna exata através dos headers
                    for (int c = 1; c <= ldpWorksheet.Dimension.End.Column; c++)
                    {
                        string headerVal = GetSafeCellValue(ldpWorksheet, headerRow, c);
                        // Verifica se o cabeçalho bate com algum dos possíveis
                        if (possibleHeaders.Any(ph => headerVal.Contains(ph, StringComparison.OrdinalIgnoreCase)))
                        {
                            // Validação extra: Se for "CR", precisamos garantir que não é apenas parte do código
                            // Mas assumindo que você usa a lógica de compor nome, vamos pegar a coluna principal se houver.
                            // Se o seu projeto usa concatenação (CR-AREA...), essa lógica simples pega a coluna explícita.
                            // Se precisar montar o nome (CR + AREA + DISC), avise. 
                            // *Assumindo aqui que existe uma coluna com o NOME FINAL ou NÚMERO CLIENTE*.
                            
                            // PRIORIDADE: NÚMERO DOCUMENTO CLIENTE
                            if (headerVal.Contains("DOCUMENTO CLIENTE", StringComparison.OrdinalIgnoreCase))
                            {
                                colKey = c;
                                break; 
                            }
                        }
                    }

                    // Se não achou "Documento Cliente", tenta pegar a primeira coluna válida (muitas vezes a coluna B ou C é o código)
                    if (colKey == -1) 
                    {
                        // Fallback: Tenta achar a coluna que tem dados parecidos com nomes de arquivos na linha seguinte
                        // Isso é um pouco arriscado, então idealmente você deve saber o nome da coluna.
                        // Vou assumir que se não achou "Documento Cliente", usa a lógica do "File Identifier" padrão do projeto ou erro.
                        _logger.LogWarning("Não foi possível identificar automaticamente a coluna de Código na LDP. Tentando coluna 'NÚMERO DOCUMENTO CLIENTE' via string exata.");
                        // Aqui você pode forçar uma busca se souber o nome exato que usa na sua LDP
                    }

                    // Loop de leitura
                    int startRow = headerRow + 1;
                    int endRow = ldpWorksheet.Dimension.End.Row;

                    // Busca dinâmica de "Qual coluna usar como chave" baseada no appsettings se necessário, 
                    // mas aqui vou varrer procurando a coluna "NÚMERO DOCUMENTO CLIENTE" que é o padrão da maioria.
                    
                    // REFAZENDO A BUSCA DE COLUNA MAIS SEGURA:
                    var headers = new Dictionary<string, int>();
                    for(int c=1; c <= ldpWorksheet.Dimension.End.Column; c++)
                    {
                         string h = GetSafeCellValue(ldpWorksheet, headerRow, c);
                         if(!string.IsNullOrEmpty(h)) headers[h.ToUpper()] = c;
                    }

                    // Tenta chaves comuns
                    if (headers.ContainsKey("NÚMERO DOCUMENTO CLIENTE")) colKey = headers["NÚMERO DOCUMENTO CLIENTE"];
                    else if (headers.ContainsKey("CODIGO")) colKey = headers["CODIGO"];
                    else if (headers.ContainsKey("CÓDIGO")) colKey = headers["CÓDIGO"];
                    
                    if (colKey != -1)
                    {
                        for (int row = startRow; row <= endRow; row++)
                        {
                            string code = GetSafeCellValue(ldpWorksheet, row, colKey);
                            if (!string.IsNullOrWhiteSpace(code))
                            {
                                ldpCodes.Add(code.Trim());
                            }
                        }
                    }
                    else
                    {
                         _logger.LogError("Coluna chave não encontrada na LDP para comparação.");
                         throw new Exception("Coluna 'NÚMERO DOCUMENTO CLIENTE' (ou similar) não encontrada na LDP.");
                    }
                }
            }
            finally
            {
                if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
            }

            _logger.LogInformation($"Total de códigos encontrados na LDP: {ldpCodes.Count}");

            // 3. Comparação: (Arquivos ACC) - (Códigos LDP)
            // Retorna arquivos que estão no ACC mas NÃO estão na LDP
            var filesNotInLdp = accFileNames
                .Where(accFile => !ldpCodes.Contains(accFile) && !accFile.StartsWith("~$")) // Ignora temporários do Excel
                .OrderBy(x => x)
                .ToList();

            _logger.LogInformation($"Resultado: {filesNotInLdp.Count} arquivos no ACC sem registro na LDP.");

            return filesNotInLdp;
        }
public async Task<string> SyncAccAttributesFromLdpAsync(string hubId, string projectId, string accessToken)
{
    _logger.LogInformation("--- ROBÔ: INICIANDO SINCRONIZAÇÃO (LDP -> ACC ATRIBUTOS) ---");
    string tempFilePath = null;

    // 1. Pega a coluna chave exata configurada no appsettings.json (ex: 'N&A DOCUMENT NUMBER')
    string keyColumnConfig = GetProjectSetting(projectId, "KeyColumn");
    _logger.LogInformation($"[Sync] Usando a chave configurada: '{keyColumnConfig}'");

    var pathsEspelho = new[] { 
        new[] { "01 - PLANEJAMENTO E CONTROLE", "01.06 - LISTA DE DOCUMENTOS PROJETO" },
        new[] { "01 - PLANNING & CONTROL", "01.06 - MASTER DOCUMENT LIST" }
    };

    try {
        string ldpSearchName = GetLdpFileNameFromConfig(projectId);
        var folderId = await FindFolderIdByAlternativePathsAsync(hubId, projectId, pathsEspelho, accessToken);
        if (string.IsNullOrEmpty(folderId)) return "Erro: Pasta 01.06 não encontrada.";

        var (fileId, fileName) = await _autodeskApi.FindItemIdByNamePartAsync(projectId, folderId, ldpSearchName, accessToken);
        if (string.IsNullOrEmpty(fileId)) return $"Erro: Arquivo '{ldpSearchName}' não encontrado.";

        tempFilePath = Path.Combine(Path.GetTempPath(), $"LDP_SYNC_{Guid.NewGuid()}.xlsx");
        using (var stream = await _autodeskApi.DownloadFileStreamAsync(projectId, fileId, accessToken))
        using (var fileStream = new FileStream(tempFilePath, FileMode.Create)) { await stream.CopyToAsync(fileStream); }

        var ldpData = new List<Dictionary<string, string>>();
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using (var package = new ExcelPackage(new FileInfo(tempFilePath))) {
    var worksheet = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Trim().ToUpper().Contains("LDP")) 
                    ?? package.Workbook.Worksheets[0];

    // ✅ ADICIONE ESTA LINHA AQUI:
    // Força o EPPlus a processar as concatenações (ex: CR-AREA-DISC...)
    // Sem isso, o robô pode ler "----" se o Excel não foi salvo com os valores calculados.
    worksheet.Cells.Calculate();

            int headerRow = 7, groupRow = 6;
            // Busca dinâmica de cabeçalho
            for (int r = 1; r <= 20; r++) {
                if (GetSafeCellValue(worksheet, r, 1).Equals("ITEM", StringComparison.OrdinalIgnoreCase)) { headerRow = r; groupRow = r - 1; break; }
            }

            var headerMap = new Dictionary<string, int>();
            int keyIdx = -1;

            for (int col = 1; col <= worksheet.Dimension.End.Column; col++) {
                string h = GetSafeCellValue(worksheet, headerRow, col);
                if (string.IsNullOrEmpty(h)) continue;

                // CORREÇÃO: Compara o cabeçalho do Excel com o valor configurado no appsettings
                if (h.Trim().Equals(keyColumnConfig, StringComparison.OrdinalIgnoreCase)) keyIdx = col;

                string g = groupRow > 0 ? GetSafeCellValue(worksheet, groupRow, col) : "";
                string combinedH = (g != "" && (g.ToUpper().Contains("EMISSÃO") || g.ToUpper().Contains("ISSUE"))) 
                    ? (ColumnMatch(h, "PROPÓSITO", "PURPOSE") ? $"{g} - PROPÓSITO DE EMISSÃO" : $"{g} - {h}") : h;
                combinedH = Regex.Replace(combinedH, @"\s+", " ").Trim();
                headerMap[combinedH] = col;
            }

            if (keyIdx == -1) return $"Erro: Coluna '{keyColumnConfig}' não encontrada na LDP.";

            for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++) {
                string keyValue = GetSafeCellValue(worksheet, row, keyIdx);
                if (string.IsNullOrWhiteSpace(keyValue)) continue;

                var rowData = new Dictionary<string, string>();
                rowData["_DOC_NUMBER_KEY_"] = keyValue;
                foreach (var kvp in headerMap) rowData[kvp.Key] = GetSafeCellValue(worksheet, row, kvp.Value);
                ldpData.Add(rowData);
            }
        }

        var result = await _autodeskApi.SyncLdpAttributesAsync(hubId, projectId, ldpData, accessToken);
        return result.SummaryMessage;
    }
    catch (Exception ex) {
        _logger.LogError(ex, "Erro na sincronização.");
        return $"Erro: {ex.Message}";
    }
    finally { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); }
}

private string GetLdpFileNameFromConfig(string projectId)
{
    // 1. Tenta pegar configuração específica do Projeto
    var fileName = _configuration[$"ProjectSettings:{projectId}:LdpFileName"];

    // 2. Se não existir, tenta pegar o Default
    if (string.IsNullOrEmpty(fileName))
    {
        fileName = _configuration["ProjectSettings:Default:LdpFileName"];
    }

    // 3. Fallback de segurança (se esqueceram de configurar tudo)
    if (string.IsNullOrEmpty(fileName))
    {
        return ".xlsx"; // Procura qualquer Excel (comportamento antigo)
    }

    return fileName;
}

public async Task<string> MirrorLdpControlToProjectAsync(string hubId, string projectId, string accessToken)
{
    _logger.LogInformation("--- 🪞 ESPELHAMENTO LDP: Concatenando via C# e Preservando XML ---");

    var pathsControle = new[] { 
        new[] { "09 - DESENVOLVIMENTO", "09.10 - GESTÃO" },
        new[] { "09 - MODEL DEVELOPING", "09.10 - MANAGEMENT" }
    };
    var pathsEspelho = new[] { 
        new[] { "01 - PLANEJAMENTO E CONTROLE", "01.06 - LISTA DE DOCUMENTOS PROJETO" },
        new[] { "01 - PLANNING & CONTROL", "01.06 - MASTER DOCUMENT LIST" }
    };

    string ldpSearchName = GetLdpFileNameFromConfig(projectId);
    string tempSourcePath = null;
    string tempTargetPath = null;

    try
    {
        // 1. Download do Controle
        var sourceFolderId = await FindFolderIdByAlternativePathsAsync(hubId, projectId, pathsControle, accessToken);
        var searchResult = await _autodeskApi.FindItemIdByNamePartAsync(projectId, sourceFolderId, ldpSearchName, accessToken);
        
        tempSourcePath = Path.Combine(Path.GetTempPath(), $"SRC_{Guid.NewGuid()}.xlsx");
        using (var sourceStream = await _autodeskApi.DownloadFileStreamAsync(projectId, searchResult.FileId, accessToken))
        using (var fileStream = new FileStream(tempSourcePath, FileMode.Create, FileAccess.Write))
        {
            await sourceStream.CopyToAsync(fileStream);
        }

        // 2. Processamento do Excel
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using (var package = new ExcelPackage(new FileInfo(tempSourcePath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) 
                     ?? package.Workbook.Worksheets[0];

            int lastRow = ws.Dimension?.End.Row ?? 8;
            int lastCol = ws.Dimension?.End.Column ?? 30;

            // Busca os cabeçalhos para saber onde colar
            var configNA = _configuration["LdpNormaMappings:PadraoNA"]?.Split(',') ?? new[] { "N&A DOCUMENT NUMBER" };
            int colIndexNA = -1;
            for (int col = 1; col <= lastCol; col++)
            {
                if (configNA.Any(h => h.Trim().ToUpper() == (ws.Cells[7, col].Text?.Trim().ToUpper())))
                {
                    colIndexNA = col;
                    break;
                }
            }

            // --- CONCATENAÇÃO VIA C# (Sugestão do Usuário) ---
            for (int row = 8; row <= lastRow; row++)
            {
                // Lê as partes diretamente (Y, Z, AA, AB, AC)
                string y = ws.Cells[row, 25].Text?.Trim() ?? "";
                string z = ws.Cells[row, 26].Text?.Trim() ?? "";
                string aa = ws.Cells[row, 27].Text?.Trim() ?? "";
                string ab = ws.Cells[row, 28].Text?.Trim() ?? "";
                string ac = ws.Cells[row, 29].Text?.Trim() ?? "";

                if (!string.IsNullOrEmpty(y) && !string.IsNullOrEmpty(z))
                {
                    // Monta a string no C# para não depender de fórmulas do Excel [cite: 2025-07-30]
                    string resultadoNA = $"{y}-{z}-{aa}-{ab}-{ac}";
                    
                    if (colIndexNA != -1) 
                    {
                        ws.Cells[row, colIndexNA].Formula = null; 
                        ws.Cells[row, colIndexNA].Value = resultadoNA; 
                    }
                }
            }

            // --- PROTEÇÃO CONTRA CORRUPÇÃO: Esconder em vez de Deletar ---
            // Deletar colunas em versões antigas do EPPlus corrompe o XML do ACC.
            // Esconder (Hidden) remove a visão do cliente e mantém o arquivo íntegro. [cite: 2025-07-30]
            if (lastCol > 15)
            {
                for (int c = 16; c <= lastCol; c++)
                {
                    ws.Column(c).Hidden = true;
                }
            }

            // Remove abas extras (Preserva a LDP)
            var extraSheets = package.Workbook.Worksheets.Where(s => !s.Name.ToUpper().Contains("LDP")).ToList();
            foreach (var s in extraSheets) package.Workbook.Worksheets.Delete(s);

            tempTargetPath = Path.Combine(Path.GetTempPath(), $"LDP_Mirror_{Guid.NewGuid()}.xlsx");
            package.SaveAs(new FileInfo(tempTargetPath));
        }

        // 3. Upload para o ACC
        var targetFolderId = await FindFolderIdByAlternativePathsAsync(hubId, projectId, pathsEspelho, accessToken);
        var mirrorFileId = await _autodeskApi.FindItemIdByNameExactAsync(projectId, targetFolderId, searchResult.FileName, accessToken);

        using (var uploadStream = new FileStream(tempTargetPath, FileMode.Open, FileAccess.Read))
        {
            if (!string.IsNullOrEmpty(mirrorFileId))
                await _autodeskApi.CreateNewVersionAsync(projectId, mirrorFileId, targetFolderId, searchResult.FileName, uploadStream, accessToken);
            else
                await _autodeskApi.UploadFileToFolderAsync(projectId, targetFolderId, searchResult.FileName, uploadStream, accessToken);
        }

        return "✅ LDP Espelhada com sucesso (Concatenado via C# e colunas de custo ocultas).";
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "❌ Erro no espelhamento.");
        throw;
    }
    finally
    {
        if (File.Exists(tempSourcePath)) File.Delete(tempSourcePath);
        if (File.Exists(tempTargetPath)) File.Delete(tempTargetPath);
    }
}

/// <summary>
/// Tenta encontrar uma pasta usando múltiplos caminhos possíveis (ex: PT e EN).
/// </summary>
private async Task<string> FindFolderIdByAlternativePathsAsync(string hubId, string projectId, string[][] possiblePaths, string accessToken)
{
    foreach (var path in possiblePaths)
    {
        // Tenta achar o caminho atual
        var folderId = await _autodeskApi.FindFolderIdByPathAsync(hubId, projectId, path, accessToken);
        
        // Se achou, retorna e para de procurar
        if (!string.IsNullOrEmpty(folderId))
        {
            _logger.LogInformation($"Pasta encontrada no caminho: {string.Join(" -> ", path)}");
            return folderId;
        }
    }
    // Se rodou tudo e não achou nada
    return null;
}

// --- DENTRO DE LdpUpdateService.cs ---

public async Task ExecuteUpdateEtapa1Async(string projectId, string tempExcelPath)
{
    using (var package = new ExcelPackage(new FileInfo(tempExcelPath)))
    {
        var wsLdp = package.Workbook.Worksheets["LDP"];
        // PEGA A VISÃO NA LINHA 8 (CONFORME SEU CSV)
        string crFilter = wsLdp.Cells[8, 24].Text.Trim();   // Coluna X
        string crosFilter = wsLdp.Cells[8, 25].Text.Trim(); // Coluna Y

        // BUSCA NO BANCO COM O MESMO FILTRO DA TIMESHEET
        var dadosBanco = await _context.TbTimeSheetRealizadoDocumentos
            .Where(t => t.Projeto == crFilter && t.Doc1 == crosFilter)
            .ToListAsync();

        // Faz o mapeamento usando Trim() para ignorar erros de digitação no Excel
        foreach (var row in wsLdp.Cells[8, 1, wsLdp.Dimension.End.Row, 1])
        {
            int rowIdx = row.Start.Row;
            string docNoExcel = wsLdp.Cells[rowIdx, 4].Text.Trim(); // Coluna D (N&A Number)

            var dadosDoc = dadosBanco.FirstOrDefault(d => d.DocumentoCodigo?.Trim() == docNoExcel);
            if (dadosDoc != null)
            {
                // ATUALIZA REV (Coluna 14 / N)
                wsLdp.Cells[rowIdx, 14].Value = dadosDoc.Doc2; // Ou a coluna de REV no seu banco
                
                // ATUALIZA STATUS (Coluna 17 / Q) - Lógica do seu fluxo
                // wsLdp.Cells[rowIdx, 17].Value = ...; 
            }
        }
        package.Save();
    }
}
public async Task SincronizarAtributosAccAsync(string hubId, string projectId, string tempExcelPath, string accessToken)
        {
            _logger.LogInformation("⚡ [SYNC ATRIBUTOS] Iniciando sincronização baseada no CR-OS...");

            using (var package = new ExcelPackage(new System.IO.FileInfo(tempExcelPath)))
            {
                var wsLdp = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("LDP", StringComparison.OrdinalIgnoreCase));
                if (wsLdp == null) return;

                // 1. PEGAR A VISÃO: CR e CR-OS da planilha
                string valCR = wsLdp.Cells[LINHA_FILTRO_LDP, COLUNA_X_CR].Text?.Trim();
                string valCROS = wsLdp.Cells[LINHA_FILTRO_LDP, COLUNA_Y_CROS].Text?.Trim();

                _logger.LogInformation($"🔍 Sincronizando apenas arquivos da OS: {valCROS}");

                // 2. BUSCAR TODOS OS ARQUIVOS DO PROJETO NO ACC (Indexação)
                var arquivosNoAcc = await _autodeskApi.IndexarArquivosProjetoAsync(hubId, projectId, accessToken);

                // 3. FILTRAR OS ARQUIVOS DO ACC QUE COMEÇAM COM O CR-OS
                // Assim ele ignora BML009.01 se você estiver na BML009.04
                var arquivosDaMinhaOs = arquivosNoAcc
                    .Where(f => f.FileName.StartsWith(valCROS, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                _logger.LogInformation($"📂 {arquivosDaMinhaOs.Count} arquivos encontrados no ACC para a OS {valCROS}.");

                // 4. ATUALIZAR ATRIBUTOS (Sem usar aquela chave N&A Document Number)
                foreach (var arquivo in arquivosDaMinhaOs)
                {
                    try 
                    {
                        // Aqui o robô atualiza os metadados do arquivo direto pelo nome que já casou com o filtro
                        _logger.LogInformation($"✅ Sincronizando metadados do arquivo: {arquivo.FileName}");
                        
                        // Exemplo: Atualiza a Revisão ou Status no ACC
                        await _autodeskApi.UpdateFileAttributesAsync(projectId, arquivo.FileUrn, new {
                            Projeto = valCR,
                            OS = valCROS
                            // Adicione outros atributos conforme sua necessidade
                        }, accessToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"⚠️ Falha ao atualizar atributos de {arquivo.FileName}: {ex.Message}");
                    }
                }
            }
        }
        

    }
}