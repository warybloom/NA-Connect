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
using NA_Connect.Services;

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
        private readonly DatabaseService _dbService;
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
        AccReviewWorkflowService workflowService,
        DatabaseService dbService)
        {
            _context = context;
            _autodeskApi = autodeskApi;
            _configuration = configuration;
            _logger = logger;
            _workflowService = workflowService;
            _metadataService = metadataService;
            _workflowApi = workflowApi;
        _statusResolver = statusResolver;
        _dbService = dbService;
        }

        /// <summary>
        /// Lê o valor de uma célula com segurança, verificando merges e fazendo Trim.
        /// </summary>
        private string GetSafeCellValue(ExcelWorksheet sheet, int row, int col)
        {
            var cell = sheet.Cells[row, col];
            string value = cell.Text?.Trim();
            if (!string.IsNullOrEmpty(value)) return value;
            if (cell.Merge)
            {
                var mergeAddress = sheet.MergedCells[row, col];
                if (mergeAddress != null)
                {
                    var firstCell = new ExcelAddress(mergeAddress).Start;
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
public async Task<LdpUpdateResult> UpdateLdpFromDatabaseAsync(string hubId, string projectId, string accessToken, string ldpFileId)
{
    _logger.LogWarning($"--- [ETAPA 1/7] REVERSE SYNC (BANCO HH + REV. COM TRAVA ACC) ---");

    var result = new LdpUpdateResult();
    string tempFilePath = Path.Combine(Path.GetTempPath(), $"LDP_STEP1_{Guid.NewGuid()}.xlsx");
    
    try
    {
        // -----------------------------------------------------------------------------------------
        // 1. PREPARAÇÃO: OBTER DETALHES DO ITEM (Versão e Pasta Pai)
        // -----------------------------------------------------------------------------------------
        // Isso resolve o erro "The provided urn is invalid" (no download) 
        // e o erro "Value for id not found" (no upload)
        var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);

        if (itemDetails == null) 
            throw new Exception($"Não foi possível recuperar detalhes do item {ldpFileId}. Verifique se o método GetItemDetailsAsync foi implementado no AutodeskApiService.");

        string versionUrn = itemDetails.TipVersionUrn;      // URN da última versão (para download)
        string parentFolderId = itemDetails.ParentFolderId; // ID da pasta pai (para upload)
        string realFileName = itemDetails.DisplayName;      // Nome original do arquivo

        _logger.LogInformation($"📄 Arquivo: {realFileName} | Versão: {versionUrn} | Pasta Pai: {parentFolderId}");

        // -----------------------------------------------------------------------------------------
        // 2. DOWNLOAD DIRETO (Usando a URN da Versão)
        // -----------------------------------------------------------------------------------------
        using (var downloadStream = await _autodeskApi.DownloadFileStreamAsync(projectId, versionUrn, accessToken))
        using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write)) {
            await downloadStream.CopyToAsync(fileStream);
        }

        // -----------------------------------------------------------------------------------------
        // 3. PROCESSAMENTO (Lógica de Negócio Mantida)
        // -----------------------------------------------------------------------------------------
        
        // A. Ler Filtros (X8 e Y8)
        string valCR = "";
        string valCROS = "";
        using (var package = new ExcelPackage(new FileInfo(tempFilePath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
            valCR = ws.Cells[8, 24].Text?.Trim();
            valCROS = ws.Cells[8, 25].Text?.Trim();
        }

        _logger.LogInformation($"🔍 Visão extraída: Projeto='{valCR}' AND Doc1='{valCROS}'");

        // B. Indexar Arquivos (Para validar existência e revisão)
        var arquivosAtivosNoAcc = await _autodeskApi.IndexarArquivosProjetoAsync(hubId, projectId, accessToken);
        var mapaArquivosVivos = arquivosAtivosNoAcc
            .GroupBy(f => Path.GetFileNameWithoutExtension(f.FileName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().VersionUrn, StringComparer.OrdinalIgnoreCase);

        // C. Buscar Dados do Banco (Horas)
        var sql = "SELECT * FROM [tbTimeSheetRealizadoDocumento] WHERE Projeto = {0} AND doc1 = {1}";
        var registrosBanco = await _context.TbTimeSheetRealizadoDocumentos
            .FromSqlRaw(sql, valCR, valCROS).AsNoTracking().ToListAsync();

        var dadosHorasBanco = registrosBanco.GroupBy(t => t.DocumentoCodigo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Trabalhadas.GetValueOrDefault()), StringComparer.OrdinalIgnoreCase);

        // D. Buscar Atributos de Revisão
        var allUrnsAtivas = arquivosAtivosNoAcc.Select(f => f.VersionUrn).Distinct().ToList();
        var accAttributesMap = await _autodeskApi.GetBatchAttributesAsync(projectId, allUrnsAtivas, accessToken);

        // E. Atualizar o Excel
        using (var package = new ExcelPackage(new FileInfo(tempFilePath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
            int colRev = 14; 
            int colNA_Number = 4;
            int colRealized = -1;

            // Localiza coluna de Horas
            for (int c = 1; c <= ws.Dimension.End.Column; c++) {
                string h = ws.Cells[7, c].Text?.Trim().ToUpper();
                if (h.Contains("HH REALIZADO") || h.Contains("ACTUAL HH")) { colRealized = c; break; }
            }

            // Loop nas linhas
            for (int row = 8; row <= ws.Dimension.End.Row; row++)
            {
                string docCode = ws.Cells[row, colNA_Number].Text?.Trim();
                if (string.IsNullOrWhiteSpace(docCode)) continue;
                bool changed = false;

                // Atualiza Revisão (se arquivo existir no ACC)
                if (mapaArquivosVivos.TryGetValue(docCode, out string urn)) {
                    if (accAttributesMap.TryGetValue(urn, out var attrs)) {
                        var revAttr = attrs.FirstOrDefault(a => a.Key.Equals("REV", StringComparison.OrdinalIgnoreCase) || a.Key.Equals("REV.", StringComparison.OrdinalIgnoreCase));
                        string valRev = revAttr.Value;
                        // Filtra datas que as vezes vem no campo de revisão por engano
                        if (!string.IsNullOrEmpty(valRev) && !(valRev.Contains(":") && valRev.Length > 10)) {
                            ws.Cells[row, colRev].Value = valRev;
                            changed = true;
                        }
                    }
                } else {
                    // Limpa revisão se não existir arquivo físico
                    if (ws.Cells[row, colRev].Value != null) {
                        ws.Cells[row, colRev].Value = null;
                        changed = true;
                    }
                }

                // Atualiza Horas Realizadas
                if (colRealized > 0 && dadosHorasBanco.TryGetValue(docCode, out var hrs)) {
                    // Verifica se mudou para não marcar 'changed' a toa
                    var valorAtual = ws.Cells[row, colRealized].Text;
                    if (valorAtual != hrs.ToString()) {
                        ws.Cells[row, colRealized].Value = hrs;
                        changed = true;
                    }
                }
                
                if (changed) result.DocumentosAtualizados++;
            }
            package.Save();
        }

        // -----------------------------------------------------------------------------------------
        // 4. UPLOAD CORRIGIDO (Passando ldpFileId + ParentFolderId)
        // -----------------------------------------------------------------------------------------
        if (result.DocumentosAtualizados > 0)
        {
            using (var ul = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read)) {
                // Aqui usamos o ParentFolderId descoberto no passo 1
                await _autodeskApi.CreateNewVersionAsync(projectId, ldpFileId, parentFolderId, realFileName, ul, accessToken);
            }
            result.SummaryMessage = $"Sucesso! {result.DocumentosAtualizados} itens atualizados e nova versão gerada.";
        }
        else
        {
            result.SummaryMessage = "Processamento concluído. Nenhuma alteração necessária na LDP.";
        }

        return result;
    }
    catch (Exception ex) 
    { 
        _logger.LogError(ex, "❌ Erro Fatal na Etapa 1 (Reverse Sync)");
        return new LdpUpdateResult { SummaryMessage = $"Erro: {ex.Message}" }; 
    }
    finally 
    { 
        if (File.Exists(tempFilePath)) File.Delete(tempFilePath); 
    }
}


       // Módulo: Services/LdpUpdateService.cs

public async Task<string> UpdateWorkflowStatusAsync(string hubId, string projectId, string accessToken, string userId, string ldpFileId)
{
    _logger.LogInformation("🔄 [ETAPA 2/7] STATUS DE WORKFLOW...");
    string tempFilePath = Path.Combine(Path.GetTempPath(), $"LDP_WF_{Guid.NewGuid()}.xlsx");

    try
    {
        // -----------------------------------------------------------------------
        // 1. RECUPERA DETALHES (Versão para Download e PASTA para Upload)
        // -----------------------------------------------------------------------
        var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
        if (itemDetails == null) throw new Exception("Detalhes do arquivo não encontrados (Item/Folder).");

        string versionUrn = itemDetails.TipVersionUrn;
        string parentFolderId = itemDetails.ParentFolderId; // 🎯 ESSENCIAL PARA O UPLOAD
        string fileName = itemDetails.DisplayName;

        // 2. DADOS DE SUPORTE
        var rawAccMap = await _autodeskApi.GetAllProjectFilesAsync(hubId, projectId, accessToken);
        var filesInAccNorm = rawAccMap.Keys.Select(n => NormalizeKeyOnly(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rawWorkflowMap = await _workflowService.GetProjectWorkflowMapAsync(projectId, accessToken);
        var workflowMapNorm = rawWorkflowMap
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => NormalizeKeyOnly(x.Key))
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

        // 3. DOWNLOAD (Usando a versão correta)
        using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, versionUrn, accessToken))
        using (var fs = new FileStream(tempFilePath, FileMode.Create)) await dl.CopyToAsync(fs);

        int count = 0;
        using (var package = new ExcelPackage(new FileInfo(tempFilePath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
            
            // ... (Lógica de colunas mantida) ...
            int colStatus = 17; // Q
            
            for (int r = 8; r <= ws.Dimension.End.Row; r++)
            {
                string y = ws.Cells[r, 25].Text?.Trim();
                string ac = ws.Cells[r, 29].Text?.Trim();
                if (string.IsNullOrEmpty(y) || string.IsNullOrEmpty(ac)) continue;

                string key = NormalizeKeyOnly($"{y}-{ws.Cells[r, 26].Text}-{ws.Cells[r, 27].Text}-{ws.Cells[r, 28].Text}-{ac}");
                string atual = ws.Cells[r, colStatus].Text?.Trim().ToUpper();

                if (atual == "APROVADO" || atual == "REJEITADO" || atual == "APROV. C/ OBS") continue;

                if (filesInAccNorm.Contains(key)) {
                    if (workflowMapNorm.TryGetValue(key, out string wfStatus)) {
                        if (atual != wfStatus.ToUpper()) { ws.Cells[r, colStatus].Value = wfStatus.ToUpper(); count++; }
                    } else if (atual != "-") { ws.Cells[r, colStatus].Value = "-"; }
                } else if (!string.IsNullOrEmpty(atual)) { ws.Cells[r, colStatus].Value = null; }
            }
            package.Save();
        }

        if (count > 0) 
        {
            // 4. UPLOAD CORRIGIDO (Passando parentFolderId)
            using (var ul = new FileStream(tempFilePath, FileMode.Open))
                await _autodeskApi.CreateNewVersionAsync(projectId, ldpFileId, parentFolderId, fileName, ul, accessToken);
            
            return $"✅ Sucesso! {count} status atualizados.";
        }
        
        return "Nada para atualizar na Etapa 2.";
    }
    catch (Exception ex) { throw; }
    finally { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); }
}


      public async Task<string> RunApprovalVerdictSyncAsync(string hubId, string projectId, string accessToken, string userId, string ldpFileId)
{
    _logger.LogInformation($"🏁 [ETAPA 5/7] VEREDITOS...");
    string tempPath = Path.Combine(Path.GetTempPath(), $"LDP_VERDITO_{Guid.NewGuid()}.xlsx");

    try
    {
        // -----------------------------------------------------------------------
        // 1. RECUPERA DETALHES (Versão e PASTA)
        // -----------------------------------------------------------------------
        var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
        if (itemDetails == null) throw new Exception("Detalhes do arquivo não encontrados.");

        string versionUrn = itemDetails.TipVersionUrn;
        string parentFolderId = itemDetails.ParentFolderId; // 🎯 ESSENCIAL
        string fileName = itemDetails.DisplayName;

        // 2. DADOS DE SUPORTE
        var rawReviews = await _workflowApi.GetProjectReviewsAsync(projectId, accessToken, userId);
        var allVersions = new List<JToken>(); 
        
        // ... (Lógica de buscar reviews mantida) ...
        foreach (var rev in rawReviews) {
            string rid = rev["id"]?.ToString();
            if(!string.IsNullOrEmpty(rid)) {
                var versions = await _workflowApi.GetReviewVersionsAsync(projectId, rid, accessToken, userId);
                foreach(var v in versions) if(v is JObject obj) { obj["reviewId"] = rid; allVersions.Add(obj); }
            }
        }

        // 3. DOWNLOAD
        using (var dl = await _autodeskApi.DownloadFileStreamAsync(projectId, versionUrn, accessToken))
        using (var fs = new FileStream(tempPath, FileMode.Create)) await dl.CopyToAsync(fs);

        int count = 0;
        using (var package = new ExcelPackage(new FileInfo(tempPath)))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP")) ?? package.Workbook.Worksheets[0];
            for (int r = 8; r <= ws.Dimension.End.Row; r++)
            {
                string key = NormalizeKeyOnly($"{ws.Cells[r, 25].Text}-{ws.Cells[r, 26].Text}-{ws.Cells[r, 27].Text}-{ws.Cells[r, 28].Text}-{ws.Cells[r, 29].Text}");
                if (string.IsNullOrEmpty(key)) continue;

                var veredito = _statusResolver.ResolverVereditoFinal(key, rawReviews, allVersions);
                if (veredito != null) {
                    string atual = ws.Cells[r, 17].Text?.Trim().ToUpper();
                    if (atual != veredito) { ws.Cells[r, 17].Value = veredito; count++; }
                }
            }
            package.Save();
        }

        if (count > 0) {
            // 4. UPLOAD CORRIGIDO
            using (var ul = new FileStream(tempPath, FileMode.Open))
                await _autodeskApi.CreateNewVersionAsync(projectId, ldpFileId, parentFolderId, fileName, ul, accessToken);
            
            return $"✅ {count} vereditos atualizados.";
        }
        return "Vereditos atualizados.";
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
public async Task<string> SyncAccAttributesFromLdpAsync(string hubId, string projectId, string token, string ldpFileId)
        {
            _logger.LogInformation("⚡ [ETAPA 7/11] Sincronizando Atributos no ACC (Lendo Config do SQL)...");
            string tempFilePath = null;

            try {
                // 1. BUSCA A CHAVE NO BANCO DE DADOS (Substitui o AppSettings)
                string keyColumnConfig = _dbService.ObterColunaChaveProjeto(projectId);
                _logger.LogInformation($"[Sync] Usando a coluna chave definida no Banco: '{keyColumnConfig}'");

                // 2. DOWNLOAD DIRETO
                // "Vacina" para garantir a versão correta
                var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, token);
                string downloadUrn = itemDetails?.TipVersionUrn ?? ldpFileId;

                tempFilePath = Path.Combine(Path.GetTempPath(), $"LDP_SYNC_{Guid.NewGuid()}.xlsx");
                
                using (var stream = await _autodeskApi.DownloadFileStreamAsync(projectId, downloadUrn, token))
                using (var fileStream = new FileStream(tempFilePath, FileMode.Create)) { await stream.CopyToAsync(fileStream); }

                var ldpData = new List<Dictionary<string, string>>();
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                
                using (var package = new ExcelPackage(new FileInfo(tempFilePath))) {
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.Trim().ToUpper().Contains("LDP")) 
                                    ?? package.Workbook.Worksheets[0];
                    
                    // Tenta calcular fórmulas se necessário (opcional)
                    // worksheet.Cells.Calculate(); 

                    int headerRow = 7, groupRow = 6;
                    // Tenta achar a linha de cabeçalho se não for a 7
                    for (int r = 1; r <= 20; r++) {
                        if (GetSafeCellValue(worksheet, r, 1).Equals("ITEM", StringComparison.OrdinalIgnoreCase)) { 
                            headerRow = r; 
                            groupRow = r - 1; 
                            break; 
                        }
                    }

                    var headerMap = new Dictionary<string, int>();
                    int keyIdx = -1;

                    // Mapeia colunas
                    for (int col = 1; col <= worksheet.Dimension.End.Column; col++) {
                        string h = GetSafeCellValue(worksheet, headerRow, col);
                        if (string.IsNullOrEmpty(h)) continue;

                        if (h.Trim().Equals(keyColumnConfig, StringComparison.OrdinalIgnoreCase)) keyIdx = col;

                        string g = groupRow > 0 ? GetSafeCellValue(worksheet, groupRow, col) : "";
                        string combinedH = (g != "" && (g.ToUpper().Contains("EMISSÃO") || g.ToUpper().Contains("ISSUE"))) 
                            ? (ColumnMatch(h, "PROPÓSITO", "PURPOSE") ? $"{g} - PROPÓSITO DE EMISSÃO" : $"{g} - {h}") : h;
                        combinedH = Regex.Replace(combinedH, @"\s+", " ").Trim();
                        headerMap[combinedH] = col;
                    }

                    if (keyIdx == -1) return $"Erro: A coluna chave '{keyColumnConfig}' não foi encontrada na planilha (Linha {headerRow}). Verifique o SQL 'Metadados_Projetos'.";

                    // Lê os dados
                    for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++) {
                        string keyValue = GetSafeCellValue(worksheet, row, keyIdx);
                        if (string.IsNullOrWhiteSpace(keyValue)) continue;

                        var rowData = new Dictionary<string, string>();
                        rowData["_DOC_NUMBER_KEY_"] = keyValue;
                        foreach (var kvp in headerMap) rowData[kvp.Key] = GetSafeCellValue(worksheet, row, kvp.Value);
                        ldpData.Add(rowData);
                    }
                }

                if (ldpData.Count == 0) return "Nenhum dado encontrado para sincronizar.";

                // Envia para atualização em lote na API da Autodesk
                var result = await _autodeskApi.SyncLdpAttributesAsync(hubId, projectId, ldpData, token);
                return result.SummaryMessage;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Erro na Etapa 7 (Atributos).");
                return $"Erro: {ex.Message}";
            }
            finally { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); }
        }
// 1. Adicione este método helper na classe
private object TratarData(string valor)
{
    if (string.IsNullOrWhiteSpace(valor)) return DBNull.Value;
    
    string v = valor.Trim().ToUpper();
    // Lista de "lixos" comuns em datas de Excel
    if (v == "-" || v == "N/A" || v == "NA" || v == "TBD" || v == "A DEFINIR") return DBNull.Value;

    // Tenta converter formato PT-BR
    if (DateTime.TryParse(valor, new System.Globalization.CultureInfo("pt-BR"), System.Globalization.DateTimeStyles.None, out DateTime data))
    {
        if (data.Year < 1900 || data.Year > 2100) return DBNull.Value; // Proteção contra datas absurdas
        return data;
    }
    
    // Tenta converter número serial do Excel (ex: 45321)
    if (double.TryParse(valor, out double serial))
    {
        try { return DateTime.FromOADate(serial); } catch {}
    }

    return DBNull.Value;
}

// 2. Use assim no seu loop de INSERT (Etapa 11):
// cmd.Parameters.AddWithValue("@DataEmissao", TratarData(ws.Cells[r, 10].Text));

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

        public async Task<string> MirrorLdpControlToProjectAsync(string hubId, string projectId, string accessToken, string ldpFileId)
        {
            _logger.LogInformation("🪞 [ETAPA 10] ESPELHAMENTO (CÓPIA PUBLICA)...");

            var pathsEspelho = new[] {
        new[] { "01 - PLANEJAMENTO E CONTROLE", "01.06 - LISTA DE DOCUMENTOS PROJETO" },
        new[] { "01 - PLANNING & CONTROL", "01.06 - MASTER DOCUMENT LIST" }
    };

            string tempSourcePath = Path.Combine(Path.GetTempPath(), $"SRC_{Guid.NewGuid()}.xlsx");
            string tempTargetPath = Path.Combine(Path.GetTempPath(), $"TGT_{Guid.NewGuid()}.xlsx");

            try
            {
                // 1. RECUPERA DETALHES DA ORIGEM
                var itemDetails = await _autodeskApi.GetItemDetailsAsync(projectId, ldpFileId, accessToken);
                if (itemDetails == null) throw new Exception("Não foi possível recuperar os detalhes da LDP.");

                string sourceVersionUrn = itemDetails.TipVersionUrn;
                string mirrorFileName = itemDetails.DisplayName; // Nome correto garantido

                // DOWNLOAD DA ORIGEM
                using (var sourceStream = await _autodeskApi.DownloadFileStreamAsync(projectId, sourceVersionUrn, accessToken))
                using (var fileStream = new FileStream(tempSourcePath, FileMode.Create, FileAccess.Write))
                {
                    await sourceStream.CopyToAsync(fileStream);
                }

                // 2. PROCESSAMENTO (FIX PARA #REF!)
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using (var package = new ExcelPackage(new FileInfo(tempSourcePath)))
                {
                    var ws = package.Workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("LDP"))
                             ?? package.Workbook.Worksheets[0];

                    int colNA_Number = 4; // Coluna D conforme seu código anterior

                    // 🎯 SOLUÇÃO PARA O #REF!: Converte fórmulas em valores antes de deletar o resto
                    for (int r = 8; r <= ws.Dimension.End.Row; r++)
                    {
                        var cell = ws.Cells[r, colNA_Number];
                        if (cell.Formula != null)
                        {
                            // Pegamos o texto resultante (Value) e sobrescrevemos a fórmula com ele
                            string valorCalculado = cell.Text;
                            cell.Formula = null; // Remove a dependência
                            cell.Value = valorCalculado; // Fixa o texto
                        }
                    }

                    // Agora podemos deletar as outras abas e colunas sem medo de quebrar nada
                    int lastCol = ws.Dimension?.End.Column ?? 30;
                    if (lastCol > 15) ws.DeleteColumn(16, lastCol - 15);

                    var extraSheets = package.Workbook.Worksheets.Where(s => !s.Name.Contains("LDP")).ToList();
                    foreach (var s in extraSheets) package.Workbook.Worksheets.Delete(s);

                    package.SaveAs(new FileInfo(tempTargetPath));
                }

                // 3. UPLOAD (Mantendo o ID existente se houver)
                string targetFolderId = null;
                foreach (var path in pathsEspelho)
                {
                    targetFolderId = await _autodeskApi.FindFolderIdByPathAsync(hubId, projectId, path, accessToken);
                    if (!string.IsNullOrEmpty(targetFolderId)) break;
                }

                if (string.IsNullOrEmpty(targetFolderId)) return "Erro: Pasta 01.06 não encontrada.";

                var existingMirrorId = await _autodeskApi.FindItemIdByNameExactAsync(projectId, targetFolderId, mirrorFileName, accessToken);

                using (var uploadStream = new FileStream(tempTargetPath, FileMode.Open, FileAccess.Read))
                {
                    if (!string.IsNullOrEmpty(existingMirrorId))
                        await _autodeskApi.CreateNewVersionAsync(projectId, existingMirrorId, targetFolderId, mirrorFileName, uploadStream, accessToken);
                    else
                        await _autodeskApi.UploadFileToFolderAsync(projectId, targetFolderId, mirrorFileName, uploadStream, accessToken);
                }

                return $"LDP Espelhada com sucesso: {mirrorFileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Erro no espelhamento.");
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