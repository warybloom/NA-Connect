using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using OfficeOpenXml;
using APS.WebApp.Services;
using APS.WebApp.Services.Qualidade;
using APS.WebApp.Models;
using System.Collections.Generic;
using System.Xml.Linq;

namespace NA_Connect.Services
{
    /// <summary>
    /// Sincroniza o arquivo de Qualidade (pasta "... - NCR NONCONFORMITY REPORT"/"... - RNC RELATÓRIO DE
    /// NÃO CONFORMIDADE" no ACC), que contém TRÊS abas obrigatórias — BASE LANÇAMENTO, RNC e DESVIO DE
    /// QUALIDADE — cada uma alimentando uma tabela SQL diferente ([dbo].[NCR BASE LANCAMENTO],
    /// [dbo].[NCR NONCONFORMITY REPORT] e [dbo].[DESVIO DE QUALIDADE], respectivamente). Cada tabela é
    /// preparada e persistida de forma INDEPENDENTE (IQualidadePreflightService.PrepararTabela +
    /// IQualidadeRepository.PersistirTabelaAsync, uma transação própria por tabela — seção 11/12 do
    /// pedido de preenchimento automático): uma falha estrutural ou de SQL numa tabela nunca bloqueia as
    /// outras duas.
    /// </summary>
    public class QualityControlSyncService
    {
        private readonly ILogger<QualityControlSyncService> _logger;
        private readonly AutodeskApiService _autodeskApi;
        private readonly AccScheduleSyncService _scheduleService;
        private readonly IQualidadePreflightService _preflightService;
        private readonly IQualidadeRepository _repository;

        public const string StatusConcluidoComSucesso = "CONCLUIDO COM SUCESSO";
        public const string StatusConcluidoComAlertas = "CONCLUIDO COM ALERTAS";
        public const string StatusConcluidoParcialmente = "CONCLUIDO PARCIALMENTE";
        public const string StatusConcluidoComErros = "CONCLUIDO COM ERROS";

        public QualityControlSyncService(
            ILogger<QualityControlSyncService> logger,
            AutodeskApiService autodeskApi,
            AccScheduleSyncService scheduleService,
            IQualidadePreflightService preflightService,
            IQualidadeRepository repository)
        {
            _logger = logger;
            _autodeskApi = autodeskApi;
            _scheduleService = scheduleService;
            _preflightService = preflightService;
            _repository = repository;
        }

        /// <summary>
        /// Localiza o arquivo único de Qualidade no ACC e processa as três abas de forma INDEPENDENTE:
        /// cada uma é preparada (preflight) e persistida (transação própria) sem esperar pelas outras.
        /// Nunca executa o Worker completo — este método é chamado UMA vez por projeto, como uma única
        /// etapa de Qualidade. Devolve o status agregado (seção 15 do pedido) e uma mensagem detalhada.
        /// </summary>
        public async Task<(string Status, string Mensagem)> SincronizarArquivoQualidadeAsync(string hubId, string projectId, string robotToken)
        {
            var tempPathsParaLimpar = new List<string>();

            try
            {
                _logger.LogInformation("[QUALIDADE][INICIO] Buscando pasta de Qualidade (NCR/RNC/DESVIO)... Project_ID_ACC={ProjectIdAcc}", projectId);

                var pathsToSearch = new[]
                {
                    new[] { "03 - QUALITY", "03.06 - NCR NONCONFORMITY REPORT" },
                    new[] { "05 - QUALITY", "05.06 - NCR NONCONFORMITY REPORT" },
                    new[] { "03 - QUALIDADE", "03.06 - NCR NONCONFORMITY REPORT" },
                    new[] { "05 - QUALIDADE", "05.06 - NCR NONCONFORMITY REPORT" },
                    new[] { "03 - QUALIDADE", "03.06 - RNC RELATÓRIO DE NÃO CONFORMIDADE" },
                    new[] { "05 - QUALIDADE", "05.06 - RNC RELATÓRIO DE NÃO CONFORMIDADE" }
                };

                string folderId = null;
                string caminhoEncontrado = null;
                foreach (var pathArray in pathsToSearch)
                {
                    folderId = await _scheduleService.FindFolderByPaths(hubId, projectId, new[] { pathArray }, robotToken);
                    if (!string.IsNullOrEmpty(folderId))
                    {
                        caminhoEncontrado = string.Join(" / ", pathArray);
                        break;
                    }
                }

                if (string.IsNullOrEmpty(folderId))
                {
                    _logger.LogWarning("⚠️ [QUALIDADE][INICIO] Pasta de Qualidade não encontrada no ACC em nenhuma das variações (03 ou 05). Project_ID_ACC={ProjectIdAcc}", projectId);
                    return (StatusConcluidoComErros, "Pasta não encontrada: NCR NONCONFORMITY REPORT / RNC RELATÓRIO DE NÃO CONFORMIDADE");
                }

                // Nunca "primeiro Excel encontrado" (seção 12): enumera TODOS os candidatos da pasta e só
                // aceita o(s) que tiverem, simultaneamente, as três abas obrigatórias.
                var candidatos = await _autodeskApi.ListarArquivosDaPastaAsync(projectId, folderId, robotToken);

                foreach (var candidatoBruto in candidatos)
                {
                    _logger.LogInformation(
                        "[QUALIDADE][ARQUIVO_CANDIDATO] Pasta={Pasta} | Arquivo={Arquivo} | Extensao={Extensao} | UltimaModificacao={UltimaModificacao}",
                        caminhoEncontrado, candidatoBruto.FileName, candidatoBruto.EhXls ? ".xls" : ".xlsx/.xlsm", candidatoBruto.LastModifiedUtc);
                }

                var candidatosXlsx = candidatos.Where(c => c.EhXlsxOuXlsm).ToList();

                if (candidatosXlsx.Count == 0)
                {
                    if (candidatos.Any(c => c.EhXls))
                    {
                        string msgXls =
                            "QUALIDADE_XLS_REQUER_CONVERSAO_MANUAL: O arquivo de Qualidade está no formato .xls. Converta manualmente para .xlsx, preservando as abas RNC, BASE LANCAMENTO e DESVIO DE QUALIDADE.";
                        _logger.LogError("[QUALIDADE][INICIO][ERRO] Project_ID_ACC={ProjectIdAcc} | {Mensagem}", projectId, msgXls);
                        return (StatusConcluidoComErros, msgXls);
                    }

                    _logger.LogWarning("⚠️ [QUALIDADE][INICIO] Nenhum Excel encontrado na pasta de Qualidade. Project_ID_ACC={ProjectIdAcc} | Pasta={Pasta}", projectId, caminhoEncontrado);
                    return (StatusConcluidoComErros, "Nenhum Excel encontrado na pasta de Qualidade");
                }

                var validos = new List<(AutodeskApiService.ArquivoCandidato Candidato, string TempPath)>();

                foreach (var candidato in candidatosXlsx)
                {
                    string tempPathCandidato = Path.Combine(Path.GetTempPath(), $"SYNC_QUAL_{Guid.NewGuid():N}.xlsx");
                    tempPathsParaLimpar.Add(tempPathCandidato);

                    using (var downloadStream = await _autodeskApi.DownloadFileStreamAsync(projectId, candidato.FileId, robotToken))
                    using (var fileStream = new FileStream(tempPathCandidato, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await downloadStream.CopyToAsync(fileStream);
                    }

                    RemoverFormatacoesCondicionaisOrfas(tempPathCandidato);

                    bool assinaturaOk;
                    ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                    using (var package = new ExcelPackage(new FileInfo(tempPathCandidato)))
                    {
                        assinaturaOk = TemAssinaturaQualidade(package);
                    }

                    _logger.LogInformation(
                        "[QUALIDADE][ASSINATURA_ABAS] Arquivo={Arquivo} | AssinaturaValida={Valida}",
                        candidato.FileName, assinaturaOk);

                    if (assinaturaOk)
                    {
                        validos.Add((candidato, tempPathCandidato));
                    }
                }

                if (validos.Count == 0)
                {
                    string msgSemAssinatura =
                        $"ARQUIVO_COM_ASSINATURA_ESPERADA_NAO_ENCONTRADO: nenhum arquivo na pasta de Qualidade contém simultaneamente as abas " +
                        $"'{QualidadeAbas.Rnc}', '{QualidadeAbas.BaseLancamento}' e '{QualidadeAbas.DesvioQualidade}'. Candidatos avaliados: [{string.Join(", ", candidatosXlsx.Select(c => c.FileName))}].";
                    _logger.LogError("[QUALIDADE][INICIO][ERRO] Project_ID_ACC={ProjectIdAcc} | {Mensagem}", projectId, msgSemAssinatura);
                    return (StatusConcluidoComErros, msgSemAssinatura);
                }

                var escolhido = validos.Count == 1
                    ? validos[0]
                    : validos.OrderByDescending(v => v.Candidato.LastModifiedUtc ?? DateTime.MinValue).First();

                if (validos.Count > 1)
                {
                    _logger.LogWarning(
                        "[QUALIDADE][ARQUIVO_SELECIONADO] Mais de um candidato com as três abas — usando o mais recente: {Escolhido} | Demais candidatos: [{Outros}]",
                        escolhido.Candidato.FileName,
                        string.Join(", ", validos.Where(v => v.Candidato.FileId != escolhido.Candidato.FileId).Select(v => v.Candidato.FileName)));
                }

                _logger.LogInformation(
                    "[QUALIDADE][ARQUIVO_SELECIONADO] Project_ID_ACC={ProjectIdAcc} | Pasta={Pasta} | Arquivo={Arquivo} | FileId={FileId}",
                    projectId, caminhoEncontrado, escolhido.Candidato.FileName, ResumirIdentificador(escolhido.Candidato.FileId));

                var definicoes = new[]
                {
                    QualidadeTabelaDefinicoes.BaseLancamento(),
                    QualidadeTabelaDefinicoes.Rnc(),
                    QualidadeTabelaDefinicoes.DesvioQualidade()
                };

                var resultados = new List<ResultadoTabelaQualidade>();

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using (var package = new ExcelPackage(new FileInfo(escolhido.TempPath)))
                {
                    foreach (var definicao in definicoes)
                    {
                        var preparado = _preflightService.PrepararTabela(package, definicao, projectId);

                        ResultadoTabelaQualidade final = preparado.Sucesso
                            ? await _repository.PersistirTabelaAsync(projectId, preparado)
                            : preparado;

                        resultados.Add(final);
                    }
                }

                return MontarResultadoAgregado(projectId, resultados);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [QUALIDADE][ERRO] Project_ID_ACC={ProjectIdAcc} | TipoExcecao={TipoExcecao}", projectId, ex.GetType().Name);
                return (StatusConcluidoComErros, $"ERRO: {ex.Message}");
            }
            finally
            {
                foreach (var tempPath in tempPathsParaLimpar)
                {
                    if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Agrega os três resultados independentes no status geral da Etapa 17 (seção 15 do pedido):
        /// todas OK sem alerta -&gt; SUCESSO; todas OK mas alguma com preenchimento automático -&gt; ALERTAS;
        /// só algumas OK -&gt; PARCIALMENTE; nenhuma OK -&gt; ERROS.
        /// </summary>
        private (string Status, string Mensagem) MontarResultadoAgregado(string projectId, List<ResultadoTabelaQualidade> resultados)
        {
            string status = DeterminarStatusGeral(resultados);
            int comSucesso = resultados.Count(r => r.Sucesso);
            int comAlertas = resultados.Count(r => r.Sucesso && r.SucessoComAlertas);
            int comErro = resultados.Count(r => !r.Sucesso);

            var linhasResumo = resultados.Select(r =>
                $"{r.Aba} ({r.TabelaDestino}): {r.CodigoResultado} | LinhasInseridas={r.LinhasInseridas} | " +
                $"LinhasComPreenchimentoAutomatico={r.LinhasComPreenchimentoAutomatico} | CamposPreenchidosAutomaticamente={r.CamposPreenchidosAutomaticamente} | " +
                $"LinhasRejeitadas={r.LinhasRejeitadas} | {r.Mensagem}");

            string mensagem = $"Etapa 17: {status}\n" + string.Join("\n", linhasResumo);

            foreach (var r in resultados)
            {
                _logger.LogInformation(
                    "[QUALIDADE][RESUMO_ABA] Aba={Aba} | TabelaDestino={Tabela} | CodigoResultado={Codigo} | LinhasInseridas={Inseridas} | " +
                    "LinhasComPreenchimentoAutomatico={ComAjuste} | CamposPreenchidosAutomaticamente={CamposPreenchidos} | LinhasRejeitadas={Rejeitadas} | " +
                    "CommitExecutado={Commit} | RollbackExecutado={Rollback}",
                    r.Aba, r.TabelaDestino, r.CodigoResultado, r.LinhasInseridas, r.LinhasComPreenchimentoAutomatico,
                    r.CamposPreenchidosAutomaticamente, r.LinhasRejeitadas, r.CommitExecutado, r.RollbackExecutado);
            }

            _logger.LogInformation(
                "[QUALIDADE][RESULTADO_GERAL] Project_ID_ACC={ProjectIdAcc} | Status={Status} | TabelasComSucesso={ComSucesso}/3 | TabelasComAlertas={ComAlertas} | TabelasComErro={ComErro}",
                projectId, status, comSucesso, comAlertas, comErro);

            return (status, mensagem);
        }

        /// <summary>
        /// Pura (sem log/IO) — determina o status geral da Etapa 17 a partir dos três resultados
        /// independentes (seção 15 do pedido): todas OK sem alerta -&gt; SUCESSO; todas OK mas alguma com
        /// preenchimento automático -&gt; ALERTAS; só algumas OK -&gt; PARCIALMENTE; nenhuma OK -&gt; ERROS.
        /// Extraída à parte para ser testável sem SQL/ACC.
        /// </summary>
        public static string DeterminarStatusGeral(IEnumerable<ResultadoTabelaQualidade> resultados)
        {
            var lista = resultados as IList<ResultadoTabelaQualidade> ?? resultados.ToList();

            int comSucesso = lista.Count(r => r.Sucesso);
            int comAlertas = lista.Count(r => r.Sucesso && r.SucessoComAlertas);
            int comErro = lista.Count(r => !r.Sucesso);

            if (comErro == 0)
            {
                return comAlertas > 0 ? StatusConcluidoComAlertas : StatusConcluidoComSucesso;
            }

            if (comSucesso == 0)
            {
                return StatusConcluidoComErros;
            }

            return StatusConcluidoParcialmente;
        }

        /// <summary>
        /// Assinatura do arquivo consolidado de Qualidade (seção 12): possui, simultaneamente, as três
        /// abas obrigatórias (nome exato após normalização — nunca posição/primeira aba com dados).
        /// </summary>
        private static bool TemAssinaturaQualidade(ExcelPackage package)
        {
            var nomesNormalizados = new HashSet<string>(
                package.Workbook.Worksheets.Select(w => QualidadeNormalizador.NormalizarNomeAba(w.Name)),
                StringComparer.OrdinalIgnoreCase);

            return nomesNormalizados.Contains(QualidadeNormalizador.NormalizarNomeAba(QualidadeAbas.BaseLancamento))
                && nomesNormalizados.Contains(QualidadeNormalizador.NormalizarNomeAba(QualidadeAbas.Rnc))
                && nomesNormalizados.Contains(QualidadeNormalizador.NormalizarNomeAba(QualidadeAbas.DesvioQualidade));
        }

        private static string ResumirIdentificador(string valor, int inicio = 10, int fim = 6)
        {
            if (string.IsNullOrEmpty(valor) || valor.Length <= inicio + fim + 3) return valor;
            return $"{valor.Substring(0, inicio)}...{valor.Substring(valor.Length - fim)}";
        }

        private int RemoverFormatacoesCondicionaisOrfas(string filePath)
        {
            const string X14Namespace = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
            XNamespace x14 = X14Namespace;
            int totalRemovido = 0;

            using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Update))
            {
                var worksheetEntries = archive.Entries
                    .Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                                    entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .Select(entry => new { entry.FullName, entry.LastWriteTime }).ToList();

                foreach (var worksheetInfo in worksheetEntries)
                {
                    var entry = archive.GetEntry(worksheetInfo.FullName);
                    if (entry == null) continue;

                    XDocument document;
                    using (var stream = entry.Open())
                    {
                        document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
                    }

                    HashSet<string> idsValidos = document.Descendants(x14 + "id")
                        .Select(element => element.Value?.Trim())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var regrasOrfas = document.Descendants(x14 + "conditionalFormatting")
                        .Where(conditionalFormatting =>
                        {
                            var rule = conditionalFormatting.Element(x14 + "cfRule");
                            string id = rule?.Attribute("id")?.Value?.Trim();
                            return !string.IsNullOrWhiteSpace(id) && !idsValidos.Contains(id);
                        }).ToList();

                    if (regrasOrfas.Count == 0) continue;

                    foreach (var regraOrfa in regrasOrfas)
                    {
                        regraOrfa.Remove();
                        totalRemovido++;
                    }

                    var containersVazios = document.Descendants(x14 + "conditionalFormattings")
                        .Where(container => !container.Elements(x14 + "conditionalFormatting").Any()).ToList();

                    foreach (var container in containersVazios)
                    {
                        var extensionElement = container.Parent;
                        if (extensionElement != null && extensionElement.Name.LocalName == "ext") extensionElement.Remove();
                        else container.Remove();
                    }

                    var extensionListsVazias = document.Descendants()
                        .Where(element => element.Name.LocalName == "extLst" && !element.Elements().Any()).ToList();

                    foreach (var extensionList in extensionListsVazias) extensionList.Remove();

                    entry.Delete();
                    var newEntry = archive.CreateEntry(worksheetInfo.FullName, System.IO.Compression.CompressionLevel.Optimal);
                    newEntry.LastWriteTime = worksheetInfo.LastWriteTime;

                    using (var outputStream = newEntry.Open())
                    {
                        document.Save(outputStream, SaveOptions.DisableFormatting);
                    }
                }
            }
            return totalRemovido;
        }
    }
}
