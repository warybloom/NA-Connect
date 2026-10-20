using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using APS.WebApp.Services.LdpTipo2;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>Um gate (30/60/90) de UM documento a atualizar na cópia da planilha — construído pelo
    /// chamador a partir dos MATCH_ENCONTRADO já decididos por ICronogramaTipo2LdpSyncService (nunca
    /// recalculado aqui).</summary>
    public sealed class CronogramaTipo2LdpPlanilhaAtualizacaoItem
    {
        public string NumeroDocumentoNaNormalizadoLdp { get; set; }
        public byte GatePercentual { get; set; }
        public DateTime? TerminoLinhaBase { get; set; }
        public DateTime? Termino { get; set; }

        /// <summary>Fonte: Cronograma_Projetos.Percent_Previsto_Reprogramado (checkpoint LDP Tipo 2 —
        /// "Planned Physical"). Pontos percentuais (0-100) — mesma escala de PercentRealizado.</summary>
        public decimal? PlannedPhysical { get; set; }

        public decimal? PercentRealizado { get; set; }
        public decimal? WbsPercentPorWork { get; set; }
    }

    public sealed class PlanilhaAtualizacaoResultado
    {
        public bool Sucesso { get; set; }
        public int CelulasDataBaselineAtualizadas { get; set; }
        public int CelulasDataReplanejadaAtualizadas { get; set; }
        public int CelulasPlannedPhysicalAtualizadas { get; set; }
        public int CelulasRealizadoAtualizadas { get; set; }
        public int CelulasWbsAtualizadas { get; set; }

        /// <summary>Documentos (não células) com pelo menos uma célula de Planned Physical ou Physical
        /// Completed realmente escrita no gate 30/60/90 — usado no resumo "PHYSICAL SYNC" (seção 12 do
        /// checkpoint), nunca conta duas vezes o mesmo documento/gate.</summary>
        public int Gate30FisicoAtualizado { get; set; }
        public int Gate60FisicoAtualizado { get; set; }
        public int Gate90FisicoAtualizado { get; set; }

        /// <summary>Contagens agregadas dos dois campos físicos (Planned Physical + Physical Completed)
        /// para o resumo "PHYSICAL SYNC": valor do Cronograma presente mas igual ao já publicado na LDP
        /// (nenhuma escrita) e valor do Cronograma ausente (NULL — nenhuma origem comprovada, célula
        /// existente preservada).</summary>
        public int ValoresIguaisPhysical { get; set; }
        public int ValoresSemOrigemPhysical { get; set; }

        public List<string> Avisos { get; set; } = new List<string>();
        public List<string> Erros { get; set; } = new List<string>();

        /// <summary>true se QUALQUER célula foi realmente escrita porque o valor calculado diferia do
        /// valor já presente na planilha — usada para decidir se vale a pena publicar uma nova versão
        /// (nunca publica se nada mudou de verdade, mesmo que o matching tenha "encontrado" o gate).</summary>
        public bool HouveAlteracao =>
            CelulasDataBaselineAtualizadas + CelulasDataReplanejadaAtualizadas +
            CelulasPlannedPhysicalAtualizadas + CelulasRealizadoAtualizadas + CelulasWbsAtualizadas > 0;
    }

    /// <summary>
    /// Atualizador ISOLADO e OPCIONAL (seção 18/19 do pedido da Fase 4B) de uma CÓPIA temporária da LDP
    /// Tipo 2 — nunca abre/altera o arquivo original, nunca faz upload ao ACC. Usa o mesmo layout dinâmico
    /// já resolvido por LdpTipo2HeaderResolver (nenhuma coluna fixa, nenhuma letra A/B/C). Escreve somente:
    /// Termino_Linha_Base → PLANNED DATA (BASE LINE) {gate}%, Termino → REPLANNED DATE {gate}%,
    /// Percent_Previsto_Reprogramado → %PLANNED PHYSICAL {gate}%, Percent_Realizado → %PHYSICAL COMPLETED
    /// {gate}% — nunca toca em Fase manual, Document Status, Situation, Revision, Emission Date,
    /// Transmittal ou Status In Flow. WBS (%) recebe a soma das participa??es por Work do documento.
    /// Percentuais s?o escritos em fra??o (pontos / 100), como Planned Physical. O teste de round-trip
    /// verifica Value, Text e NumberFormat; valida??o operacional da LDP real permanece manual.
    /// </summary>
    public static class CronogramaTipo2LdpPlanilhaAtualizador
    {
        /// <summary>Tolerância de comparação em PONTOS percentuais (não em fração de Excel) — evita
        /// reescrever/contar como alteração por causa de erro de arredondamento binário de ponto
        /// flutuante ao converter pontos↔fração (ex.: 64.97/100 armazenado e lido de volta).</summary>
        private const decimal ToleranciaPontosPercentuais = 0.00005m;

        public static PlanilhaAtualizacaoResultado AtualizarCopia(
            string caminhoArquivoOrigem,
            string caminhoArquivoDestino,
            IReadOnlyList<CronogramaTipo2LdpPlanilhaAtualizacaoItem> atualizacoes,
            ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(caminhoArquivoOrigem)) throw new ArgumentException("caminhoArquivoOrigem não pode ser vazio.", nameof(caminhoArquivoOrigem));
            if (string.IsNullOrWhiteSpace(caminhoArquivoDestino)) throw new ArgumentException("caminhoArquivoDestino não pode ser vazio.", nameof(caminhoArquivoDestino));
            if (atualizacoes == null) throw new ArgumentNullException(nameof(atualizacoes));

            var resultado = new PlanilhaAtualizacaoResultado();

            // Cópia temporária primeiro — todo o resto desta função só toca no destino, nunca na origem.
            File.Copy(caminhoArquivoOrigem, caminhoArquivoDestino, overwrite: true);

            using var package = new ExcelPackage(new FileInfo(caminhoArquivoDestino));

            ExcelWorksheet ws;
            try
            {
                ws = LdpTipo2HeaderResolver.LocalizarAbaLdp(package);
            }
            catch (LdpTipo2LayoutException ex)
            {
                resultado.Erros.Add(ex.Message);
                return resultado;
            }

            LdpTipo2Layout layout;
            try
            {
                layout = LdpTipo2HeaderResolver.ResolverLayout(ws, logger, Path.GetFileName(caminhoArquivoDestino), resultado.Avisos);
            }
            catch (LdpTipo2LayoutException ex)
            {
                resultado.Erros.Add(ex.Message);
                return resultado;
            }

            int colunaNa = layout.ColunasComuns[LdpTipo2CabecalhosComuns.NaDocumentNumber];
            var linhaPorNaNormalizado = new Dictionary<string, int>(StringComparer.Ordinal);
            int linhaFinal = ws.Dimension?.End.Row ?? (layout.PrimeiraLinhaDocumento - 1);

            for (int linha = layout.PrimeiraLinhaDocumento; linha <= linhaFinal; linha++)
            {
                string naOriginal = LdpTipo2CellReader.LerNumeroDocumentoNaCalculado(ws.Cells[linha, colunaNa], out _);
                if (string.IsNullOrWhiteSpace(naOriginal)) continue;

                string normalizado = CronogramaTipo2DocumentNumberNormalizer.Normalizar(naOriginal);
                // Mesma regra do parser (Fase 4A): primeira ocorrência encontrada nunca é sobrescrita
                // silenciosamente por uma duplicata posterior.
                if (!linhaPorNaNormalizado.ContainsKey(normalizado)) linhaPorNaNormalizado[normalizado] = linha;
            }

            foreach (var item in atualizacoes)
            {
                var gateLayout = item.GatePercentual switch
                {
                    30 => layout.Gate30,
                    60 => layout.Gate60,
                    90 => layout.Gate90,
                    _ => null
                };

                if (gateLayout == null)
                {
                    resultado.Avisos.Add($"[PLANILHA LDP TIPO 2] Gate percentual inválido ({item.GatePercentual}) — item ignorado.");
                    continue;
                }

                if (!linhaPorNaNormalizado.TryGetValue(item.NumeroDocumentoNaNormalizadoLdp, out int linha))
                {
                    resultado.Avisos.Add($"[PLANILHA LDP TIPO 2] Documento '{item.NumeroDocumentoNaNormalizadoLdp}' não encontrado na cópia da planilha — gate {item.GatePercentual}% não atualizado.");
                    continue;
                }

                // Diff antes de escrever (idempotência da publicação — seção 3.1 do checkpoint): só
                // grava e só conta como alteração real quando o valor calculado difere do que já está
                // na célula. Rodar o mesmo matching duas vezes sem mudança na origem produz
                // HouveAlteracao=false na segunda vez, e a publicação no ACC é pulada.
                //
                // Leitura da célula EXISTENTE via LdpTipo2CellReader.LerData (checkpoint pós-canário:
                // GetValue<DateTime?>() lançava FormatException em "-", o placeholder legítimo de "sem
                // data" usado pela LDP) — nunca GetValue<DateTime?>() direto numa célula que pode conter
                // "-", vazio, DateTime nativo, serial Excel ou texto. NULL/""/whitespace/"-" e qualquer
                // valor não reconhecido (warning, nunca exceção) são tratados como "sem data" — o mesmo
                // resultado de "não altera" que já ocorre quando o Cronograma não tem valor (o bloco
                // inteiro é pulado via HasValue), preservando o "-" quando não há dado novo.
                if (item.TerminoLinhaBase.HasValue)
                {
                    DateTime? atual = LdpTipo2CellReader.LerData(
                        ws.Cells[linha, gateLayout.ColunaPlannedBaseline], logger, linha, $"PLANNED DATA (BASE LINE) {item.GatePercentual}%");
                    DateTime novo = item.TerminoLinhaBase.Value.Date;
                    if (atual?.Date != novo)
                    {
                        ws.Cells[linha, gateLayout.ColunaPlannedBaseline].Value = novo;
                        resultado.CelulasDataBaselineAtualizadas++;
                    }
                }

                if (item.Termino.HasValue)
                {
                    DateTime? atual = LdpTipo2CellReader.LerData(
                        ws.Cells[linha, gateLayout.ColunaReplannedDate], logger, linha, $"REPLANNED DATE {item.GatePercentual}%");
                    DateTime novo = item.Termino.Value.Date;
                    if (atual?.Date != novo)
                    {
                        ws.Cells[linha, gateLayout.ColunaReplannedDate].Value = novo;
                        resultado.CelulasDataReplanejadaAtualizadas++;
                    }
                }

                // Planned Physical / Physical Completed (checkpoint LDP Tipo 2, seções 4/7/8/9/12): mesma
                // mecânica de diff das datas acima, em PONTOS percentuais (nunca em fração de Excel — a
                // conversão pontos→fração só acontece no momento de ESCREVER, seção 7 do pedido). NULL no
                // Cronograma nunca é tratado como 0% (seção 8) — o bloco inteiro é pulado via HasValue,
                // preservando a célula existente da LDP.
                bool naoAlteradoFisicoNesteItem = true;

                if (!item.PlannedPhysical.HasValue)
                {
                    resultado.ValoresSemOrigemPhysical++;
                }
                else
                {
                    decimal? atual = LerPercentualCelulaBruta(ws.Cells[linha, gateLayout.ColunaPlannedPhysical], logger, linha, $"%PLANNED PHYSICAL {item.GatePercentual}%");
                    decimal novoPontos = item.PlannedPhysical.Value;
                    bool alterado = !atual.HasValue || Math.Abs(atual.Value - novoPontos) > ToleranciaPontosPercentuais;

                    if (alterado)
                    {
                        ws.Cells[linha, gateLayout.ColunaPlannedPhysical].Value = novoPontos / 100m;
                        resultado.CelulasPlannedPhysicalAtualizadas++;
                        naoAlteradoFisicoNesteItem = false;
                    }
                    else
                    {
                        resultado.ValoresIguaisPhysical++;
                    }

                    LogAtualizacaoFisica(logger, item.NumeroDocumentoNaNormalizadoLdp, item.GatePercentual,
                        $"%PLANNED PHYSICAL {item.GatePercentual}%", atual, novoPontos, alterado ? novoPontos : atual, alterado);
                }

                if (!item.PercentRealizado.HasValue)
                {
                    resultado.ValoresSemOrigemPhysical++;
                }
                else
                {
                    decimal? atual = LerPercentualCelulaBruta(ws.Cells[linha, gateLayout.ColunaPhysicalCompleted], logger, linha, $"%PHYSICAL COMPLETED {item.GatePercentual}%");
                    decimal novoPontos = item.PercentRealizado.Value;
                    bool alterado = !atual.HasValue || Math.Abs(atual.Value - novoPontos) > ToleranciaPontosPercentuais;

                    if (alterado)
                    {
                        ws.Cells[linha, gateLayout.ColunaPhysicalCompleted].Value = novoPontos / 100m;
                        resultado.CelulasRealizadoAtualizadas++;
                        naoAlteradoFisicoNesteItem = false;
                    }
                    else
                    {
                        resultado.ValoresIguaisPhysical++;
                    }

                    LogAtualizacaoFisica(logger, item.NumeroDocumentoNaNormalizadoLdp, item.GatePercentual,
                        $"%PHYSICAL COMPLETED {item.GatePercentual}%", atual, novoPontos, alterado ? novoPontos : atual, alterado);
                }

                if (!naoAlteradoFisicoNesteItem)
                {
                    switch (item.GatePercentual)
                    {
                        case 30: resultado.Gate30FisicoAtualizado++; break;
                        case 60: resultado.Gate60FisicoAtualizado++; break;
                        case 90: resultado.Gate90FisicoAtualizado++; break;
                    }
                }
            }

            // WBS ? comum ao documento: soma as participa??es das tarefas/gates com matching.
            // Sem origem completa ou com gate repetido, n?o escolhe um valor arbitr?rio.
            if (layout.ColunasComuns.TryGetValue(LdpTipo2CabecalhosComuns.WbsPercent, out int colunaWbs))
            foreach (var grupo in atualizacoes.Where(i => i.GatePercentual is 30 or 60 or 90)
                .GroupBy(i => i.NumeroDocumentoNaNormalizadoLdp))
            {
                if (!linhaPorNaNormalizado.TryGetValue(grupo.Key, out int linha)) continue;
                if (grupo.Any(i => !i.WbsPercentPorWork.HasValue) || grupo.GroupBy(i => i.GatePercentual).Any(g => g.Count() > 1))
                {
                    resultado.Avisos.Add($"[WORK] WBS de '{grupo.Key}' preservado: origem ausente ou gate duplicado.");
                    continue;
                }
                decimal pontos = grupo.Sum(i => i.WbsPercentPorWork.Value);
                if (pontos < 0 || pontos > 100)
                {
                    resultado.Avisos.Add($"[WORK] WBS de '{grupo.Key}' fora de 0-100: {pontos}.");
                    continue;
                }
                var cell = ws.Cells[linha, colunaWbs];
                var atual = LerPercentualCelulaBruta(cell, logger, linha, "WBS (%)");
                logger?.LogDebug("[WORK][EXCEL] Value={Value} | Text={Text} | NumberFormat={Format} | NovoPontos={Pontos}",
                    cell.Value, cell.Text, cell.Style.Numberformat.Format, pontos);
                // A origem ? sempre Work. O valor antigo serve somente para detectar altera??es.
                if (!atual.HasValue || Math.Abs(atual.Value - pontos) > ToleranciaPontosPercentuais
                    || !cell.Style.Numberformat.Format.Contains('%') || !string.IsNullOrEmpty(cell.Formula))
                {
                    cell.Formula = string.Empty;
                    cell.Value = pontos / 100m;
                    cell.Style.Numberformat.Format = "0.00%";
                    resultado.CelulasWbsAtualizadas++;
                }
            }
            package.Save();
            resultado.Sucesso = true;
            return resultado;
        }

        /// <summary>
        /// Leitura defensiva da célula EXISTENTE de %PLANNED PHYSICAL/%PHYSICAL COMPLETED, em PONTOS
        /// percentuais (25% -> 25). Nunca usa ExcelRange.GetValue&lt;decimal?&gt;() sem proteção — igual ao
        /// problema já corrigido para datas (ver LerData): uma célula com "-", texto livre ou qualquer
        /// valor não numérico faria GetValue&lt;decimal?&gt;() lançar FormatException. Aqui, qualquer
        /// falha de conversão vira "sem valor atual" (warning, nunca exceção) — mesmo resultado de "célula
        /// vazia", nunca inventa um valor.
        /// </summary>
        private static decimal? LerPercentualCelulaBruta(ExcelRange cell, ILogger logger, int linha, string cabecalho)
        {
            if (cell == null) return null;

            object valor = cell.Value;
            if (valor == null) return null;

            try
            {
                decimal? fracao = cell.GetValue<decimal?>();
                return fracao.HasValue ? fracao.Value * 100m : (decimal?)null;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    "[CRONOGRAMA TIPO 2 -> LDP][PHYSICAL] Linha {Linha} | Cabeçalho: {Cabecalho} | Valor não reconhecido ('{Valor}') — tratado como vazio: {Erro}",
                    linha, cabecalho, valor, ex.Message);
                return null;
            }
        }

        /// <summary>Log por-atualização exigido pela seção 12 do checkpoint — sempre em Debug (alto
        /// volume: um por gate×campo×documento processado, não só quando altera).</summary>
        private static void LogAtualizacaoFisica(
            ILogger logger, string naNormalizado, byte gate, string campo,
            decimal? valorLdpAntesPontos, decimal? valorCronogramaPontos, decimal? valorLdpDepoisPontos, bool alterado)
        {
            logger?.LogDebug(
                "[CRONOGRAMA TIPO 2 -> LDP][PHYSICAL] N&A={NA} | Gate={Gate}% | Campo={Campo} | ValorLdpAntes={Antes} | ValorCronograma={Cronograma} | ValorLdpDepois={Depois} | Alterado={Alterado}",
                naNormalizado, gate, campo,
                valorLdpAntesPontos.HasValue ? $"{valorLdpAntesPontos.Value:0.####}%" : "(vazio)",
                valorCronogramaPontos.HasValue ? $"{valorCronogramaPontos.Value:0.####}%" : "(sem origem)",
                valorLdpDepoisPontos.HasValue ? $"{valorLdpDepoisPontos.Value:0.####}%" : "(vazio)",
                alterado ? "SIM" : "NAO");
        }
    }
}
