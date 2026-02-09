// Ficheiro: Services/PlaceholderCreationService.cs (COM MELHORIA DE ERRO E PREVENÇÃO)
using APS.WebApp.Models;
using Microsoft.AspNetCore.Hosting; // Removido se não usado diretamente aqui
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging; // Adicionado para logging

namespace APS.WebApp.Services
{
    // Enum NormaNomenclatura e classes LdpRow, PlaceholderResult permanecem iguais...
    public enum NormaNomenclatura
    {
        NA,
        Cliente
    }

    public class LdpRow
    {
        public int RowNumber { get; set; }
        public string NomeDocumentoNA { get; set; } = string.Empty;
        public string NomeDocumentoCliente { get; set; } = string.Empty;
        // Adicionar campos para filtro fácil
        public string DisciplinaCodigo { get; set; } = string.Empty;
        public string TipoDocumentoCodigo { get; set; } = string.Empty;

    }

    public class PlaceholderResult
    {
        public int TotalRowsInLdp { get; set; } // Total de linhas lidas ANTES do filtro
        public int RowsProcessed { get; set; } // Total de linhas que passaram pelo filtro e foram TENTADAS
        public int PlaceholdersCreated { get; set; }
        public int FilesSkippedExisting { get; set; } // Renomeado para clareza
        public int FilesSkippedFiltered { get; set; } // NOVO: Contagem de linhas ignoradas pelo filtro
        public List<string> CreatedFiles { get; set; } = new List<string>();
        public List<string> SkippedExistingFiles { get; set; } = new List<string>(); // Renomeado
        public List<string> SkippedFilteredInfo { get; set; } = new List<string>(); // NOVO: Detalhes das linhas filtradas
        public List<string> Errors { get; set; } = new List<string>();
        public string SummaryMessage { get; set; } = string.Empty;
    }


    public class PlaceholderCreationService
    {
        private readonly AutodeskApiService _autodeskApi;
        private readonly NomenclaturaService _nomenclaturaService;
        // private readonly IWebHostEnvironment _hostingEnvironment; // Removido se não usado
        private readonly ILogger<PlaceholderCreationService> _logger; // Adicionado Logger

        // Construtor atualizado para incluir ILogger
        public PlaceholderCreationService(
            AutodeskApiService autodeskApi,
            NomenclaturaService nomenclaturaService,
            // IWebHostEnvironment hostingEnvironment, // Removido se não usado
            ILogger<PlaceholderCreationService> logger) // Adicionado Logger
        {
            _autodeskApi = autodeskApi;
            _nomenclaturaService = nomenclaturaService;
            // _hostingEnvironment = hostingEnvironment; // Removido se não usado
            _logger = logger; // Adicionado Logger
        }


        // ▼▼▼ MÉTODO PRINCIPAL COM O FILTRO ADICIONADO ▼▼▼
        // ▼▼▼ MÉTODO PRINCIPAL COM ASSINATURA ALTERADA E LÓGICA DE DOWNLOAD ADICIONADA ▼▼▼
        public async Task<PlaceholderResult> CreatePlaceholdersFromProjectLdpAsync( // <--- Nome alterado para clareza
            string hubId,
            string projectId,
            // Stream ldpStream, // <--- REMOVIDO este parâmetro
            NormaNomenclatura norma,
            string accessToken)
        {
            var result = new PlaceholderResult();
            _logger.LogInformation("Iniciando processo de criação de placeholders a partir da LDP do projeto {ProjectId}", projectId);

            // Define as disciplinas e tipos a serem excluídos (MESMA LÓGICA DO LDPUPDATESERVICE)
            var excludedDisciplines = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PL1", "PL2", "PL3", "PL4", "PL5", "PL6", "PL7", "PL8", "PL9",
            "CT0", "CT1", "CT2", "CT3", "CT4", "CT5", "CT6", "CT7", "CT8", "CT9",
            "PC0", "TU0", "EL0", "IA0", "EP0", "DC0", "GC0" // Adicionado GC0
        };
            var excludedDocumentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AT", "3D"
        };

            List<LdpRow> ldpRows; // Declarado aqui

            try // Adicionado um try/catch geral para a busca e leitura da LDP
            {
                // === INÍCIO: Buscar e Baixar a LDP do Projeto ===
                _logger.LogInformation("Localizando e baixando a LDP do projeto ACC...");
                string ldpVersionUrn = await _autodeskApi.FindLdpFileUrnAsync(hubId, projectId, accessToken);
                if (string.IsNullOrEmpty(ldpVersionUrn))
                {
                    // FindLdpFileUrnAsync já lança exceção se não encontrar, mas adicionamos segurança
                    throw new FileNotFoundException("Arquivo LDP principal não encontrado no projeto.");
                }
                _logger.LogInformation("LDP encontrada (URN: {LdpUrn}). Iniciando download...", ldpVersionUrn);

                using var ldpStream = await _autodeskApi.DownloadFileStreamAsync(projectId, ldpVersionUrn, accessToken);
                _logger.LogInformation("Download da LDP concluído. Analisando arquivo...");
                // === FIM: Buscar e Baixar a LDP do Projeto ===


                // 1. LER E VALIDAR A PLANILHA LDP (ParseLdp modificado para extrair códigos de filtro)
                ldpRows = ParseLdp(ldpStream, result, norma); // Usa o stream baixado
                result.TotalRowsInLdp = ldpRows.Count + result.FilesSkippedFiltered;
                if (!ldpRows.Any() && result.FilesSkippedFiltered == 0)
                {
                    if (!result.Errors.Any())
                    {
                        result.SummaryMessage = "A LDP baixada do projeto está vazia ou não contém as colunas e/ou a aba 'LDP' necessárias.";
                        _logger.LogWarning("Análise da LDP falhou: {SummaryMessage}", result.SummaryMessage);
                    }
                    else
                    {
                        _logger.LogError("Erro na análise da LDP: {ErrorMessage}", string.Join("; ", result.Errors));
                    }
                    return result;
                }
                _logger.LogInformation("{TotalRows} linhas encontradas na LDP para processamento.", result.TotalRowsInLdp);
            }
            catch (Exception ex) // Captura erros na busca, download ou leitura inicial da LDP
            {
                _logger.LogError(ex, "Erro ao buscar, baixar ou ler a LDP do projeto.");
                result.Errors.Add($"Erro ao acessar ou ler a LDP do projeto: {ex.Message}");
                result.SummaryMessage = "Falha ao obter a LDP do projeto. Verifique se o arquivo existe e se a formatação está correta.";
                return result; // Retorna o erro imediatamente
            }


            // O restante do método (a partir do try para buscar pastas) permanece IDÊNTICO
            try
            {
                // 2. BUSCA DINÂMICA DAS PASTAS PRINCIPAIS (RAIZ E TEMPLATES) - Inalterado
                _logger.LogInformation("Buscando pasta raiz e pasta de templates...");
                // ... (código igual ao anterior para buscar rootFolderId e templateFolderId) ...
                var rootFolder = await _autodeskApi.GetProjectRootFolderAsync(hubId, projectId, accessToken);
                if (rootFolder == null || !rootFolder.Value.TryGetProperty("id", out var rootFolderIdElement) || string.IsNullOrEmpty(rootFolderIdElement.GetString()))
                {
                    result.Errors.Add("Não foi possível encontrar a pasta raiz 'Project Files' do projeto.");
                    result.SummaryMessage = "Falha na Pré-Verificação: Pasta raiz do projeto não encontrada.";
                    _logger.LogError(result.SummaryMessage);
                    return result;
                }
                string rootFolderId = rootFolderIdElement.GetString()!;

                //const string templateFolderName = "05.06 - TEMPLATES API";
                const string templateFolderName = "09.09.05 - TEMPLATES API"; // <-- NOVO NOME FINAL DA PASTA
                string? templateFolderId = await _autodeskApi.FindFolderByNameRecursiveAsync(projectId, rootFolderId, templateFolderName, accessToken);

                if (string.IsNullOrEmpty(templateFolderId))
                {
                    result.Errors.Add($"A pasta de templates '{templateFolderName}' não foi encontrada no projeto.");
                    result.SummaryMessage = "Falha na Pré-Verificação: A pasta de templates não foi encontrada.";
                    _logger.LogError(result.SummaryMessage + " " + result.Errors.LastOrDefault());
                    return result;
                }
                _logger.LogInformation("Pasta de templates encontrada: {TemplateFolderId}", templateFolderId);


                // 3. PRÉ-VERIFICAÇÃO DE PASTAS DE DESTINO E ARQUIVOS EXISTENTES - Inalterado
                // ... (código igual ao anterior para requiredFolderNames, disciplineFoldersMap, existingFileNames) ...
                var requiredFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in ldpRows)
                {
                    bool excludeByType = !string.IsNullOrEmpty(row.TipoDocumentoCodigo) && excludedDocumentTypes.Contains(row.TipoDocumentoCodigo);
                    bool excludeByDiscipline = !string.IsNullOrEmpty(row.DisciplinaCodigo) && excludedDisciplines.Contains(row.DisciplinaCodigo);

                    if (excludeByType || excludeByDiscipline)
                    {
                        result.FilesSkippedFiltered++;
                        result.SkippedFilteredInfo.Add($"Linha {row.RowNumber}: Ignorada (Tipo='{row.TipoDocumentoCodigo}', Disc='{row.DisciplinaCodigo}')");
                        continue;
                    }

                    var nomePastaDestino = _nomenclaturaService.GetNomePastaDestino(row.DisciplinaCodigo);
                    if (!string.IsNullOrEmpty(nomePastaDestino))
                    {
                        requiredFolderNames.Add(nomePastaDestino);
                    }
                    else
                    {
                        string errorMsg = $"Linha {row.RowNumber}: Não foi encontrado mapeamento de pasta para a disciplina '{row.DisciplinaCodigo}' no appsettings.json (NomenclaturaMappings:Disciplinas).";
                        result.Errors.Add(errorMsg);
                        _logger.LogError(errorMsg);
                    }
                }

                _logger.LogInformation("Mapeando {Count} pastas de destino necessárias...", requiredFolderNames.Count);
                var disciplineFoldersMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var folderName in requiredFolderNames)
                {
                    var foundFolderId = await _autodeskApi.FindFolderByNameRecursiveAsync(projectId, rootFolderId, folderName, accessToken);
                    if (string.IsNullOrEmpty(foundFolderId))
                    {
                        result.Errors.Add($"A pasta de destino '{folderName}', necessária pela LDP, não foi encontrada no projeto.");
                        _logger.LogWarning("Pasta de destino necessária '{FolderName}' não encontrada.", folderName);
                    }
                    else
                    {
                        disciplineFoldersMap[folderName] = foundFolderId;
                        _logger.LogDebug(" -> Pasta '{FolderName}' mapeada para ID {FolderId}.", folderName, foundFolderId);
                    }
                }

                if (result.Errors.Any(e => e.Contains("pasta de destino") || e.Contains("mapeamento de pasta")))
                {
                    string firstSpecificError = result.Errors.FirstOrDefault(e => e.Contains("mapeamento de pasta") || e.Contains("pasta de destino"));
                    result.SummaryMessage = !string.IsNullOrEmpty(firstSpecificError)
                        ? $"Falha na Pré-Verificação. {firstSpecificError}"
                        : "Falha na Pré-Verificação: Uma ou mais pastas de destino necessárias não foram encontradas ou mapeadas.";
                    _logger.LogError(result.SummaryMessage);
                    return result;
                }

                _logger.LogInformation("Verificando arquivos já existentes no projeto...");
                var allProjectFiles = await _autodeskApi.GetAllProjectFilesAsync(hubId, projectId, accessToken);
                var existingFileNames = new HashSet<string>(
                    allProjectFiles.Keys.Select(Path.GetFileNameWithoutExtension),
                    StringComparer.OrdinalIgnoreCase);
                _logger.LogInformation("{Count} arquivos existentes encontrados.", existingFileNames.Count);

                // 4. PROCESSAMENTO E CRIAÇÃO DOS PLACEHOLDERS - Inalterado
                // ... (código igual ao anterior dentro do loop foreach (var row in ldpRows)) ...
                _logger.LogInformation("Iniciando criação de placeholders...");
                result.RowsProcessed = result.FilesSkippedFiltered;
                foreach (var row in ldpRows)
                {
                    result.RowsProcessed++;

                    bool excludeByType = !string.IsNullOrEmpty(row.TipoDocumentoCodigo) && excludedDocumentTypes.Contains(row.TipoDocumentoCodigo);
                    bool excludeByDiscipline = !string.IsNullOrEmpty(row.DisciplinaCodigo) && excludedDisciplines.Contains(row.DisciplinaCodigo);

                    if (excludeByType || excludeByDiscipline)
                    {
                        _logger.LogDebug("Linha {RowNumber}: Ignorando placeholder (Tipo='{Type}', Disc='{Disc}')", row.RowNumber, row.TipoDocumentoCodigo, row.DisciplinaCodigo);
                        continue;
                    }

                    string nomeDocumentoNA = row.NomeDocumentoNA; // Para usar no log de erro
                    try
                    {
                        string finalFileNameBase = norma == NormaNomenclatura.Cliente ? row.NomeDocumentoCliente : nomeDocumentoNA;
                        if (string.IsNullOrWhiteSpace(finalFileNameBase))
                        {
                            _logger.LogWarning("Linha {RowNumber}: Nome do documento final está vazio, pulando.", row.RowNumber);
                            continue;
                        }

                        var nomePastaDestino = _nomenclaturaService.GetNomePastaDestino(row.DisciplinaCodigo);
                        if (nomePastaDestino == null || !disciplineFoldersMap.TryGetValue(nomePastaDestino, out var targetFolderId))
                        {
                            result.Errors.Add($"Linha {row.RowNumber}: [ERRO INESPERADO] Não foi possível resolver a pasta de destino para a disciplina '{row.DisciplinaCodigo}'.");
                            _logger.LogError("Linha {RowNumber}: Erro inesperado ao resolver pasta para disciplina '{DisciplinaCodigo}' que passou pelo filtro.", row.RowNumber, row.DisciplinaCodigo);
                            continue;
                        }

                        var templateFileName = _nomenclaturaService.GetTemplateFileName(row.TipoDocumentoCodigo);
                        if (string.IsNullOrEmpty(templateFileName))
                        {
                            result.Errors.Add($"Linha {row.RowNumber}: [ERRO INESPERADO] Não foi encontrado um template para o tipo de documento '{row.TipoDocumentoCodigo}'.");
                            _logger.LogError("Linha {RowNumber}: Erro inesperado ao buscar template para tipo '{TipoCodigo}' que passou pelo filtro.", row.RowNumber, row.TipoDocumentoCodigo);
                            continue;
                        }

                        var extension = Path.GetExtension(templateFileName);
                        var nomeArquivoFinal = Regex.Replace(finalFileNameBase, @"[<>:""/\\|?*&%+]", "_") + extension;
                        var nomeArquivoFinalSemExtensao = Path.GetFileNameWithoutExtension(nomeArquivoFinal);

                        if (existingFileNames.Contains(nomeArquivoFinalSemExtensao))
                        {
                            result.FilesSkippedExisting++;
                            result.SkippedExistingFiles.Add(nomeArquivoFinal);
                            _logger.LogDebug("Linha {RowNumber}: Placeholder for '{FileName}' ignorado (já existe).", row.RowNumber, nomeArquivoFinal);
                            continue;
                        }

                        var sourceItemUrn = await _autodeskApi.FindTemplateItemUrnAsync(projectId, templateFolderId, templateFileName, accessToken);
                        if (string.IsNullOrEmpty(sourceItemUrn))
                        {
                            result.Errors.Add($"Linha {row.RowNumber}: O arquivo modelo '{templateFileName}' não foi encontrado na pasta de templates '{templateFolderName}'. A criação do placeholder '{nomeArquivoFinal}' foi ignorada.");
                            _logger.LogWarning("Linha {RowNumber}: Template '{TemplateName}' não encontrado na pasta {TemplateFolderId}.", row.RowNumber, templateFileName, templateFolderId);
                            continue;
                        }

                        var sourceVersionUrn = await _autodeskApi.GetItemTipVersionUrnAsync(projectId, sourceItemUrn, accessToken);
                        if (string.IsNullOrEmpty(sourceVersionUrn))
                        {
                            result.Errors.Add($"Linha {row.RowNumber}: Não foi possível encontrar a versão principal (tip) do template '{templateFileName}'.");
                            _logger.LogWarning("Linha {RowNumber}: Versão TIP do template '{TemplateName}' (Item URN: {ItemUrn}) não encontrada.", row.RowNumber, templateFileName, sourceItemUrn);
                            continue;
                        }

                        _logger.LogInformation("Linha {RowNumber}: Criando placeholder '{FileName}' na pasta '{FolderName}'...", row.RowNumber, nomeArquivoFinal, nomePastaDestino);
                        await _autodeskApi.CopyPlaceholderAsync(projectId, sourceVersionUrn, targetFolderId, nomeArquivoFinal, accessToken);

                        result.PlaceholdersCreated++;
                        result.CreatedFiles.Add(nomeArquivoFinal);
                        existingFileNames.Add(nomeArquivoFinalSemExtensao);

                    }
                    catch (Exception ex)
                    {
                        string errorMessage = $"Linha {row.RowNumber} ('{nomeDocumentoNA}'): Erro - {ex.Message}";
                        result.Errors.Add(errorMessage);
                        _logger.LogError(ex, "Erro ao processar linha {RowNumber} para placeholder.", row.RowNumber);
                    }
                } // Fim do loop principal


            }
            catch (Exception ex)
            {
                // Captura erros que podem acontecer ANTES do loop principal (ex: busca de pastas)
                result.Errors.Add($"Ocorreu um erro geral durante o processo: {ex.Message}");
                result.SummaryMessage = "O processo foi interrompido por um erro crítico.";
                _logger.LogError(ex, "Erro crítico durante a criação de placeholders.");
                return result; // Retorna imediatamente
            }

            // 5. FINALIZAÇÃO E GERAÇÃO DO SUMÁRIO - Inalterado
            result.SummaryMessage = $"Operação concluída. {result.PlaceholdersCreated} placeholders criados, {result.FilesSkippedExisting} ignorados (já existentes), {result.FilesSkippedFiltered} ignorados (filtrados ou linha em branco).";
            if (result.Errors.Any())
            {
                result.SummaryMessage += $" Ocorreram {result.Errors.Count} erros durante o processo, verifique a lista de erros para mais detalhes.";
                _logger.LogWarning("Criação de placeholders concluída com {ErrorCount} erros.", result.Errors.Count);
            }
            else
            {
                _logger.LogInformation("Criação de placeholders concluída com sucesso.");
            }

            return result;
        }



        // ▼▼▼ MÉTODO PARSE LDP ATUALIZADO (Ação 1) ▼▼▼
        // ▼▼▼ MÉTODO PARSE LDP PERMANECE IDÊNTICO ▼▼▼
        private List<LdpRow> ParseLdp(Stream stream, PlaceholderResult result, NormaNomenclatura norma)
        {
            var rows = new List<LdpRow>();
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            try
            {
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets["LDP"];
                    if (worksheet == null || worksheet.Dimension == null)
                    {
                        result.Errors.Add("A aba 'LDP' não foi encontrada ou está vazia.");
                        _logger.LogError("Erro no ParseLdp: Aba 'LDP' não encontrada ou vazia.");
                        return rows;
                    }

                    const int headerRow = 7;
                    if (worksheet.Dimension.Rows < headerRow)
                    {
                        result.Errors.Add($"A planilha não possui a linha de cabeçalho esperada (linha {headerRow}).");
                        _logger.LogError("Erro no ParseLdp: Linha de cabeçalho {HeaderRow} não encontrada.", headerRow);
                        return rows;
                    }

                    // --- MAPEAMENTO DE COLUNAS BILINGUE ---
                    int colCR = -1, colArea = -1, colDisciplina = -1, colTipo = -1, colSequencial = -1, colNomeCliente = -1;

                    for (int col = 1; col <= worksheet.Dimension.Columns; col++)
                    {
                        var rawHeader = worksheet.Cells[headerRow, col].Value?.ToString() ?? "";
                        var header = Regex.Replace(rawHeader, @"\s+", " ").Trim();

                        if (header.Equals("CR", StringComparison.OrdinalIgnoreCase)) 
                            colCR = col;
                        else if (header.Contains("ÁREA", StringComparison.OrdinalIgnoreCase) || header.Contains("AREA", StringComparison.OrdinalIgnoreCase)) 
                            colArea = col;
                        else if (header.Contains("DISCIPLINA", StringComparison.OrdinalIgnoreCase) || header.Contains("DISCIPLINE", StringComparison.OrdinalIgnoreCase)) 
                            colDisciplina = col;
                        else if (header.Contains("TIPO DE DOCUMENTO", StringComparison.OrdinalIgnoreCase) || header.Contains("DOCUMENT TYPE", StringComparison.OrdinalIgnoreCase)) 
                            colTipo = col;
                        else if (header.Contains("SEQUENCIAL", StringComparison.OrdinalIgnoreCase) || header.Contains("SEQUENTIAL", StringComparison.OrdinalIgnoreCase)) 
                            colSequencial = col;
                        else if (header.Contains("NÚMERO DOCUMENTO CLIENTE", StringComparison.OrdinalIgnoreCase) || header.Contains("CLIENT DOCUMENT NUMBER", StringComparison.OrdinalIgnoreCase)) 
                            colNomeCliente = col;
                    }

                    if (colCR == -1 || colArea == -1 || colDisciplina == -1 || colTipo == -1 || colSequencial == -1)
                    {
                        result.Errors.Add("Não foi possível encontrar todas as colunas obrigatórias para formar o nome N&A (CR, ÁREA, DISCIPLINA/ATIVIDADE, TIPO DE DOCUMENTO, SEQUENCIAL) na linha 7.");
                        _logger.LogError("Erro no ParseLdp: Colunas N&A obrigatórias não encontradas.");
                        return rows;
                    }
                    if (norma == NormaNomenclatura.Cliente && colNomeCliente == -1)
                    {
                        result.Errors.Add("A norma 'Cliente' foi selecionada, mas a coluna 'NÚMERO DOCUMENTO CLIENTE' não foi encontrada na linha 7.");
                        _logger.LogError("Erro no ParseLdp: Norma Cliente selecionada, mas coluna não encontrada.");
                        return rows;
                    }

                    _logger.LogInformation("Cabeçalhos da LDP mapeados com sucesso. Iniciando leitura das linhas de dados...");
                    for (int row = headerRow + 1; row <= worksheet.Dimension.Rows; row++)
                    {
                        var p1_cr = worksheet.Cells[row, colCR].Text?.Trim() ?? "";
                        var p2_area = worksheet.Cells[row, colArea].Text?.Trim() ?? "";
                        var p3_disc = worksheet.Cells[row, colDisciplina].Text?.Trim() ?? "";
                        var p4_tipo = worksheet.Cells[row, colTipo].Text?.Trim() ?? "";
                        var p5_seq = worksheet.Cells[row, colSequencial].Text?.Trim() ?? "";

                        var nomeNA = $"{p1_cr}-{p2_area}-{p3_disc}-{p4_tipo}-{p5_seq}";
                        nomeNA = Regex.Replace(nomeNA, "-{2,}", "-").Trim('-');

                        if (string.IsNullOrWhiteSpace(nomeNA.Replace("-", "")))
                        {
                            // Se a linha inteira parece vazia com base no nome N&A, conta como filtrada/pulada
                            result.FilesSkippedFiltered++;
                            result.SkippedFilteredInfo.Add($"Linha {row}: Ignorada (Nome N&A vazio ou inválido).");
                            continue; // Pula linha vazia
                        }

                        if (string.IsNullOrWhiteSpace(p3_disc) || string.IsNullOrWhiteSpace(p4_tipo))
                        {
                            _logger.LogWarning("Linha {RowNumber}: Ignorando placeholder (Disciplina ou Tipo de Documento está em branco). Doc: {DocName}", row, nomeNA);
                            result.FilesSkippedFiltered++;
                            result.SkippedFilteredInfo.Add($"Linha {row}: Ignorada (Disciplina ou Tipo estava em branco).");
                            continue;
                        }

                        var nomeCliente = colNomeCliente != -1 ? worksheet.Cells[row, colNomeCliente].Text?.Trim() ?? "" : "";

                        rows.Add(new LdpRow
                        {
                            RowNumber = row,
                            NomeDocumentoNA = nomeNA,
                            NomeDocumentoCliente = nomeCliente,
                            DisciplinaCodigo = p3_disc.ToUpperInvariant(),
                            TipoDocumentoCodigo = p4_tipo.ToUpperInvariant()
                        });
                    }
                    _logger.LogInformation("Leitura das linhas de dados concluída. {RowCount} linhas adicionadas.", rows.Count);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Erro ao ler o arquivo Excel: {ex.Message}");
                _logger.LogError(ex, "Erro fatal durante a leitura do arquivo Excel no ParseLdp.");
                return new List<LdpRow>();
            }
            return rows;
        }
    

    }
}