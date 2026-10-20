using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Leitor completo e dinâmico da aba "LDP" de um arquivo de LDP Tipo 2. Não conhece Autodesk, SQL
    /// nem upload — recebe um Stream já em mãos (ver LdpTipo2Service, responsável por obter esse
    /// Stream do ACC) e devolve um LdpTipo2Resultado inteiramente em memória.
    ///
    /// Nenhuma coluna é fixa: tudo é descoberto a partir da linha 7 (ver LdpTipo2HeaderResolver). A
    /// única posição fixa em todo o parser é a LINHA do cabeçalho.
    /// </summary>
    public sealed class LdpTipo2Parser : ILdpTipo2Parser
    {
        private readonly ILogger<LdpTipo2Parser> _logger;

        public LdpTipo2Parser(ILogger<LdpTipo2Parser> logger)
        {
            _logger = logger;
        }

        public Task<LdpTipo2Resultado> ProcessarAsync(
            Stream arquivoExcel,
            string projectIdAcc,
            string nomeArquivo,
            DateTime dataSincronizacao,
            CancellationToken cancellationToken = default)
        {
            if (arquivoExcel == null) throw new ArgumentNullException(nameof(arquivoExcel));
            if (string.IsNullOrWhiteSpace(projectIdAcc)) throw new ArgumentException("projectIdAcc não pode ser vazio.", nameof(projectIdAcc));

            cancellationToken.ThrowIfCancellationRequested();

            var resultado = new LdpTipo2Resultado
            {
                ProjectIdAcc = projectIdAcc,
                NomeArquivo = nomeArquivo
            };

            using var package = new ExcelPackage(arquivoExcel);

            ExcelWorksheet ws;
            try
            {
                ws = LdpTipo2HeaderResolver.LocalizarAbaLdp(package);
            }
            catch (LdpTipo2LayoutException ex)
            {
                _logger.LogError($"❌ {ex.Message}");
                resultado.Erros.Add(ex.Message);
                return Task.FromResult(resultado);
            }

            LdpTipo2Layout layout;
            try
            {
                layout = LdpTipo2HeaderResolver.ResolverLayout(ws, _logger, nomeArquivo, resultado.Avisos);
            }
            catch (LdpTipo2LayoutException ex)
            {
                _logger.LogError($"❌ {ex.Message}");
                resultado.Erros.Add(ex.Message);
                return Task.FromResult(resultado);
            }

            resultado.LinhaCabecalho = layout.LinhaCabecalho;

            ProcessarLinhas(ws, layout, projectIdAcc, dataSincronizacao, resultado);
            DetectarDuplicidadesDocumento(resultado);
            ValidarConsistencia(resultado);

            return Task.FromResult(resultado);
        }

        private void ProcessarLinhas(ExcelWorksheet ws, LdpTipo2Layout layout, string projectIdAcc, DateTime dataSincronizacao, LdpTipo2Resultado resultado)
        {
            int linhaInicial = layout.PrimeiraLinhaDocumento;
            int linhaFinal = ws.Dimension?.End.Row ?? (linhaInicial - 1);

            int linhasExaminadas = 0;
            int linhasVaziasIgnoradas = 0;
            int linhasSemNaIgnoradas = 0;

            int colunaNa = layout.ColunasComuns[LdpTipo2CabecalhosComuns.NaDocumentNumber];

            for (int linha = linhaInicial; linha <= linhaFinal; linha++)
            {
                linhasExaminadas++;

                string item = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.Item);
                string clienteBruto = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.ClientDocumentNumber);
                string tituloBruto = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.DocumentTitle);

                string numeroNa = LdpTipo2CellReader.LerNumeroDocumentoNaCalculado(ws.Cells[linha, colunaNa], out bool formulaSemValor);

                bool temCandidato = !string.IsNullOrWhiteSpace(item)
                    || !string.IsNullOrWhiteSpace(clienteBruto)
                    || !string.IsNullOrWhiteSpace(tituloBruto)
                    || !string.IsNullOrWhiteSpace(numeroNa)
                    || formulaSemValor;

                if (!temCandidato)
                {
                    linhasVaziasIgnoradas++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(numeroNa))
                {
                    linhasSemNaIgnoradas++;
                    string motivo = formulaSemValor
                        ? "célula do N&A tem fórmula, mas nenhum valor calculado disponível"
                        : "número N&A vazio";
                    string aviso = $"[LDP TIPO 2] Linha {linha}: {motivo} — linha ignorada.";
                    resultado.Avisos.Add(aviso);
                    _logger.LogWarning(aviso);
                    continue;
                }

                var documento = new LdpTipo2Documento
                {
                    LinhaExcel = linha,
                    Item = item,
                    NumeroDocumentoNa = numeroNa,
                    NumeroDocumentoCliente = clienteBruto,
                    TituloDocumento = tituloBruto,
                    ScheduleId = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.ScheduleId),
                    Disciplina = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.Discipline),
                    Formato = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.Format),
                    NumeroPaginas = LerComumInt(ws, layout, linha, LdpTipo2CabecalhosComuns.NoOfPages),
                    PlannedBaseline = LerComumData(ws, layout, linha, LdpTipo2CabecalhosComuns.PlannedBaseline),
                    ReplannedDate = LerComumData(ws, layout, linha, LdpTipo2CabecalhosComuns.ReplannedDate),
                    EmissionDate = LerComumData(ws, layout, linha, LdpTipo2CabecalhosComuns.EmissionData),
                    TransmittalNumber = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.TransmittalNumber),
                    IssuedPurpose = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.IssuedPurpose),
                    Revision = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.Revision),
                    DocumentStatus = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.DocumentStatus),
                    WbsPercent = LerComumPercentual(ws, layout, linha, LdpTipo2CabecalhosComuns.WbsPercent, normalizarEscalaCronogramaTipo2: true),
                    StatusInFlow = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.StatusInFlow),
                    Situation = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.Situation),
                    DateCommentsReceived = LerComumData(ws, layout, linha, LdpTipo2CabecalhosComuns.DateCommentsReceived),
                    CommentStatus = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.CommentStatus),
                    InclusionDate = LerComumData(ws, layout, linha, LdpTipo2CabecalhosComuns.InclusionDate),
                    Cr = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.Cr),
                    CrOs = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.CrOs),
                    Area = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.Area),
                    DisciplineActivity = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.DisciplineActivity),
                    DocTypeCode = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.DocTypeCode),
                    Seq = LerComumTexto(ws, layout, linha, LdpTipo2CabecalhosComuns.Seq),
                    PlannedHh = LerComumDecimal(ws, layout, linha, LdpTipo2CabecalhosComuns.PlannedHh),
                    ActualHh = LerComumDecimal(ws, layout, linha, LdpTipo2CabecalhosComuns.ActualHh),
                    RemainingHh = LerComumDecimal(ws, layout, linha, LdpTipo2CabecalhosComuns.RemainingHh),
                    PlannedCost = LerComumDecimal(ws, layout, linha, LdpTipo2CabecalhosComuns.PlannedCost),
                    ActualCost = LerComumDecimal(ws, layout, linha, LdpTipo2CabecalhosComuns.ActualCost),
                    RemainingCost = LerComumDecimal(ws, layout, linha, LdpTipo2CabecalhosComuns.RemainingCost)
                };

                documento.Gates.Add(CriarGate(ws, layout.Gate30, linha, projectIdAcc, documento, dataSincronizacao, resultado));
                documento.Gates.Add(CriarGate(ws, layout.Gate60, linha, projectIdAcc, documento, dataSincronizacao, resultado));
                documento.Gates.Add(CriarGate(ws, layout.Gate90, linha, projectIdAcc, documento, dataSincronizacao, resultado));

                foreach (var gate in documento.Gates)
                {
                    if (gate.GateAtivo) resultado.GatesAtivos++;
                    else resultado.GatesInativos++;
                }

                resultado.Documentos.Add(documento);

                _logger.LogDebug(
                    $"[LDP TIPO 2] Linha: {linha} | Número N&A: {documento.NumeroDocumentoNa} | Número Cliente: {documento.NumeroDocumentoCliente} | " +
                    $"Título: {documento.TituloDocumento} | Gate 30 ativo: {documento.Gates[0].GateAtivo} | " +
                    $"Gate 60 ativo: {documento.Gates[1].GateAtivo} | Gate 90 ativo: {documento.Gates[2].GateAtivo}");
            }

            resultado.LinhasAnalisadas = linhasExaminadas;
            resultado.DocumentosEncontrados = resultado.Documentos.Count;

            _logger.LogInformation(
                $"[LDP TIPO 2] Resumo | Linhas examinadas: {linhasExaminadas} | Linhas vazias ignoradas: {linhasVaziasIgnoradas} | " +
                $"Linhas sem N&A ignoradas: {linhasSemNaIgnoradas} | Documentos válidos: {resultado.Documentos.Count} | " +
                $"Gates ativos: {resultado.GatesAtivos} | Gates inativos: {resultado.GatesInativos} | " +
                $"Warnings: {resultado.Avisos.Count} | Erros: {resultado.Erros.Count}");
        }

        /// <summary>
        /// Cria o objeto de gate a partir do layout já resolvido. SEMPRE cria o objeto — mesmo sem
        /// "X" — com GateAtivo=false, preservando qualquer dado manual eventualmente preenchido (seção
        /// 18). Somente o indicador de aplicabilidade consecutivo define GateAtivo — a coluna "Fase"
        /// manual (valores como FA/FD/P/FQ) nunca é interpretada como ativação.
        /// </summary>
        private LdpTipo2Gate CriarGate(
            ExcelWorksheet ws,
            LdpTipo2GateLayout gateLayout,
            int linha,
            string projectIdAcc,
            LdpTipo2Documento documento,
            DateTime dataSincronizacao,
            LdpTipo2Resultado resultado)
        {
            string textoIndicador = LdpTipo2CellReader.LerTexto(ws.Cells[linha, gateLayout.ColunaIndicadorAplicabilidade]);
            bool ativo = LdpTipo2GateMarkingHelper.InterpretarMarcacaoGate(textoIndicador);

            if (!ativo && !string.IsNullOrWhiteSpace(textoIndicador))
            {
                string aviso = $"[LDP TIPO 2] Linha {linha}, gate {gateLayout.Percentual}%: valor inesperado no indicador de aplicabilidade ('{textoIndicador}') — tratado como inativo.";
                resultado.Avisos.Add(aviso);
                _logger.LogWarning(aviso);
            }

            var gate = new LdpTipo2Gate
            {
                ProjectIdAcc = projectIdAcc,
                NumeroDocumentoNa = documento.NumeroDocumentoNa,
                NumeroDocumentoCliente = documento.NumeroDocumentoCliente,
                GatePercentual = gateLayout.Percentual,
                GateAtivo = ativo,
                Fase = LdpTipo2CellReader.LerTexto(ws.Cells[linha, gateLayout.ColunaFaseManual]),
                DocumentStatus = LdpTipo2CellReader.LerTexto(ws.Cells[linha, gateLayout.ColunaDocumentStatus]),
                Situation = LdpTipo2CellReader.LerTexto(ws.Cells[linha, gateLayout.ColunaSituation]),
                PlannedBaseline = LdpTipo2CellReader.LerData(ws.Cells[linha, gateLayout.ColunaPlannedBaseline], _logger, linha, LdpTipo2CabecalhosComuns.PlannedBaselineGate(gateLayout.Percentual)),
                ReplannedDate = LdpTipo2CellReader.LerData(ws.Cells[linha, gateLayout.ColunaReplannedDate], _logger, linha, LdpTipo2CabecalhosComuns.ReplannedDateGate(gateLayout.Percentual)),
                PlannedPhysical = LdpTipo2CellReader.LerPercentual(ws.Cells[linha, gateLayout.ColunaPlannedPhysical], _logger, linha, LdpTipo2CabecalhosComuns.PlannedPhysicalGate(gateLayout.Percentual)),
                PhysicalCompleted = LdpTipo2CellReader.LerPercentual(ws.Cells[linha, gateLayout.ColunaPhysicalCompleted], _logger, linha, LdpTipo2CabecalhosComuns.PhysicalCompletedGate(gateLayout.Percentual)),
                DataSincronizacao = dataSincronizacao
            };

            bool possuiDadosManuais = !string.IsNullOrWhiteSpace(gate.Fase)
                || !string.IsNullOrWhiteSpace(gate.DocumentStatus)
                || !string.IsNullOrWhiteSpace(gate.Situation)
                || gate.PlannedBaseline.HasValue
                || gate.ReplannedDate.HasValue
                || gate.PlannedPhysical.HasValue
                || gate.PhysicalCompleted.HasValue;

            if (!gate.GateAtivo && possuiDadosManuais)
            {
                string aviso = $"[LDP TIPO 2] Linha {linha}, gate {gateLayout.Percentual}%: gate sem X, mas possui informações preenchidas.";
                resultado.Avisos.Add(aviso);
                _logger.LogWarning(aviso);
            }

            return gate;
        }

        /// <summary>
        /// Detecta N&amp;A ou Client Document Number repetidos entre LINHAS diferentes da mesma LDP —
        /// erro de dado que impede matching determinístico com o cronograma (nunca escolher uma linha
        /// arbitrariamente). Gera apenas avisos (não bloqueia a persistência) — mesmo padrão já usado
        /// pelo restante do parser. Comparação de igualdade exata local, sem depender de
        /// CronogramaTipo2DocumentNumberNormalizer (LdpTipo2 não deve conhecer o pacote CronogramaTipo2).
        /// </summary>
        private void DetectarDuplicidadesDocumento(LdpTipo2Resultado resultado)
        {
            void Registrar(Func<LdpTipo2Documento, string> seletor, string rotulo)
            {
                var grupos = resultado.Documentos
                    .Select(d => new { Doc = d, Chave = NormalizarParaComparacao(seletor(d)) })
                    .Where(x => !string.IsNullOrEmpty(x.Chave))
                    .GroupBy(x => x.Chave, StringComparer.Ordinal)
                    .Where(g => g.Count() > 1);

                foreach (var grupo in grupos)
                {
                    var linhas = string.Join(", ", grupo.Select(x => x.Doc.LinhaExcel).OrderBy(l => l));
                    string aviso = $"[LDP TIPO 2] {rotulo}_DUPLICADO: '{grupo.Key}' aparece em {grupo.Count()} linhas ({linhas}) — matching com o cronograma resultará em MATCH_AMBIGUO para esse número.";
                    resultado.Avisos.Add(aviso);
                    _logger.LogWarning(aviso);
                }
            }

            Registrar(d => d.NumeroDocumentoNa, "N&A");
            Registrar(d => d.NumeroDocumentoCliente, "CLIENT");
        }

        private static string NormalizarParaComparacao(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
            string semNbsp = valor.Replace(' ', ' ');
            return System.Text.RegularExpressions.Regex.Replace(semNbsp, @"\s+", " ").Trim().ToUpperInvariant();
        }

        private void ValidarConsistencia(LdpTipo2Resultado resultado)
        {
            int esperado = resultado.DocumentosEncontrados * 3;
            int total = resultado.GatesAtivos + resultado.GatesInativos;

            if (total != esperado)
            {
                string erro = $"[LDP TIPO 2] Erro de consistência: GatesAtivos ({resultado.GatesAtivos}) + GatesInativos ({resultado.GatesInativos}) = {total}, mas eram esperados {esperado} (DocumentosEncontrados x 3).";
                resultado.Erros.Add(erro);
                _logger.LogError(erro);
            }
        }

        private static string LerComumTexto(ExcelWorksheet ws, LdpTipo2Layout layout, int linha, string cabecalho)
        {
            return layout.ColunasComuns.TryGetValue(cabecalho, out int coluna)
                ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, coluna])
                : null;
        }

        private static int? LerComumInt(ExcelWorksheet ws, LdpTipo2Layout layout, int linha, string cabecalho)
        {
            return layout.ColunasComuns.TryGetValue(cabecalho, out int coluna)
                ? LdpTipo2CellReader.LerInt(ws.Cells[linha, coluna])
                : null;
        }

        private DateTime? LerComumData(ExcelWorksheet ws, LdpTipo2Layout layout, int linha, string cabecalho)
        {
            return layout.ColunasComuns.TryGetValue(cabecalho, out int coluna)
                ? LdpTipo2CellReader.LerData(ws.Cells[linha, coluna], _logger, linha, cabecalho)
                : null;
        }

        private decimal? LerComumDecimal(ExcelWorksheet ws, LdpTipo2Layout layout, int linha, string cabecalho)
        {
            return layout.ColunasComuns.TryGetValue(cabecalho, out int coluna)
                ? LdpTipo2CellReader.LerDecimal(ws.Cells[linha, coluna], _logger, linha, cabecalho)
                : null;
        }

        private decimal? LerComumPercentual(ExcelWorksheet ws, LdpTipo2Layout layout, int linha, string cabecalho, bool normalizarEscalaCronogramaTipo2 = false)
        {
            return layout.ColunasComuns.TryGetValue(cabecalho, out int coluna)
                ? LdpTipo2CellReader.LerPercentual(ws.Cells[linha, coluna], _logger, linha, cabecalho, normalizarEscalaCronogramaTipo2)
                : null;
        }
    }
}
