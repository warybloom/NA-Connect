using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Nomes canônicos (já normalizados — ver LdpTipo2HeaderResolver.NormalizarCabecalho) dos
    /// cabeçalhos comuns da aba LDP (Tipo 2). Usados como chaves em LdpTipo2Layout.ColunasComuns e
    /// como única fonte de "números mágicos" textuais — nunca espalhar essas strings pelo código.
    /// </summary>
    public static class LdpTipo2CabecalhosComuns
    {
        public const string Item = "ITEM";
        public const string ScheduleId = "SCHEDULE ID";
        public const string Discipline = "DISCIPLINE";
        public const string NaDocumentNumber = "N&A DOCUMENT NUMBER";
        public const string ClientDocumentNumber = "CLIENT DOCUMENT NUMBER";
        public const string DocumentTitle = "DOCUMENT TITLE";
        public const string Format = "FORMAT";
        public const string NoOfPages = "Nº OF PAGES";
        public const string PlannedBaseline = "PLANNED DATA (BASE LINE)";
        public const string ReplannedDate = "REPLANNED DATE";
        public const string EmissionData = "EMISSION DATA";
        public const string TransmittalNumber = "TRANMISTAL NUMBER";
        public const string IssuedPurpose = "ISSUED POUPURSE";
        public const string Revision = "REV.";
        public const string DocumentStatus = "DOCUMENT STATUS";
        public const string WbsPercent = "WBS (%)";
        public const string StatusInFlow = "STATUS IN THE FLOW";
        public const string Situation = "SITUATION";
        public const string DateCommentsReceived = "DATE COMMENTS WERE RECEIVED";
        public const string CommentStatus = "COMMENT STATUS";

        public const string PlannedPhysical30 = "%PLANNED PHYSICAL 30%";
        public const string PhysicalCompleted30 = "%PHYSICAL COMPLETED 30%";
        public const string PlannedPhysical60 = "%PLANNED PHYSICAL 60%";
        public const string PhysicalCompleted60 = "%PHYSICAL COMPLETED 60%";
        public const string PlannedPhysical90 = "%PLANNED PHYSICAL 90%";
        public const string PhysicalCompleted90 = "%PHYSICAL COMPLETED 90%";

        public const string InclusionDate = "INCLUSION DATE";
        public const string Cr = "CR";
        public const string CrOs = "CR-OS";
        public const string Area = "AREA";
        public const string DisciplineActivity = "DISCIPLINE ACTIVITY";
        public const string DocTypeCode = "DOC TYPE CODE";
        public const string Seq = "SEQ";
        public const string PlannedHh = "PLANNED HH";
        public const string ActualHh = "ACTUAL HH";
        public const string RemainingHh = "REMAINING HH";
        public const string PlannedCost = "PLANNED COST";
        public const string ActualCost = "ACTUAL COST";
        public const string RemainingCost = "REMAINING COST";

        /// <summary>
        /// Base para os cabeçalhos repetidos "Fase 30%"/"Fase 60%"/"Fase 90%" (bloco manual + indicador
        /// de aplicabilidade — ver LdpTipo2HeaderResolver.ResolverBlocoGate e ResolverIndicadores).
        /// </summary>
        public static string Fase(byte percentual) => $"FASE {percentual}%";
        public static string DocumentStatusGate(byte percentual) => $"DOCUMENT STATUS {percentual}%";
        public static string SituationGate(byte percentual) => $"SITUATION {percentual}%";
        public static string PlannedBaselineGate(byte percentual) => $"PLANNED DATA (BASE LINE) {percentual}%";
        public static string ReplannedDateGate(byte percentual) => $"REPLANNED DATE {percentual}%";
        public static string PlannedPhysicalGate(byte percentual) => $"%PLANNED PHYSICAL {percentual}%";
        public static string PhysicalCompletedGate(byte percentual) => $"%PHYSICAL COMPLETED {percentual}%";

        /// <summary>
        /// Campos obrigatórios para o processamento (seção 8 do pedido): sua ausência interrompe todo
        /// o processamento do arquivo. Os %PLANNED/PHYSICAL COMPLETED de cada gate também entram aqui —
        /// os blocos manuais e o conjunto de indicadores são validados separadamente (ver
        /// LdpTipo2HeaderResolver.ResolverLayout).
        /// </summary>
        public static readonly IReadOnlyList<string> Obrigatorios = new[]
        {
            NaDocumentNumber,
            DocumentTitle,
            PlannedPhysical30, PhysicalCompleted30,
            PlannedPhysical60, PhysicalCompleted60,
            PlannedPhysical90, PhysicalCompleted90
        };

        /// <summary>
        /// Campos opcionais: ausência gera apenas warning (o arquivo continua sendo processado).
        /// </summary>
        public static readonly IReadOnlyList<string> Opcionais = new[]
        {
            Item, ScheduleId, Discipline, ClientDocumentNumber, Format, NoOfPages,
            PlannedBaseline, ReplannedDate, EmissionData, TransmittalNumber, IssuedPurpose, Revision,
            DocumentStatus, WbsPercent, StatusInFlow, Situation, DateCommentsReceived, CommentStatus,
            InclusionDate, Cr, CrOs, Area, DisciplineActivity, DocTypeCode, Seq,
            PlannedHh, ActualHh, RemainingHh, PlannedCost, ActualCost, RemainingCost
        };
    }

    /// <summary>
    /// Resolve dinamicamente a linha de cabeçalho da aba "LDP" (ver LocalizarLinhaCabecalho) e, a partir
    /// dela, onde cada campo está — nunca por posição fixa de linha ou coluna. Ver seções 3 a 11 do
    /// pedido desta fase.
    /// </summary>
    public static class LdpTipo2HeaderResolver
    {
        /// <summary>
        /// Limite de linhas varridas em busca da linha de cabeçalho (nunca assume linha fixa — ver
        /// LocalizarLinhaCabecalho). Suficiente para qualquer cabeçalho de LDP real observado, com folga.
        /// </summary>
        internal const int LimiteVarreduraLinhaCabecalho = 30;

        private static readonly byte[] PercentuaisGate = { 30, 60, 90 };

        /// <summary>
        /// Aliases explícitos (nunca fuzzy/Contains/Levenshtein): grafia alternativa normalizada ->
        /// grafia canônica normalizada (ver LdpTipo2CabecalhosComuns). Só entram aqui variações
        /// comprovadamente necessárias.
        /// </summary>
        private static readonly Dictionary<string, string> AliasesCabecalho = new(StringComparer.OrdinalIgnoreCase)
        {
            ["TRANSMITTAL NUMBER"] = LdpTipo2CabecalhosComuns.TransmittalNumber,
            ["ISSUED PURPOSE"] = LdpTipo2CabecalhosComuns.IssuedPurpose,
            ["N° OF PAGES"] = LdpTipo2CabecalhosComuns.NoOfPages,
            ["NO OF PAGES"] = LdpTipo2CabecalhosComuns.NoOfPages,
            ["REV"] = LdpTipo2CabecalhosComuns.Revision,
        };

        /// <summary>
        /// Normaliza um cabeçalho para comparação: trata null, troca \r/\n/\t e espaço não separável
        /// ( ) por espaço comum, remove espaços nas pontas, colapsa espaços internos múltiplos em
        /// um único, e maiusculiza. Preserva símbolos relevantes (%, &amp;, -, parênteses) — nunca os
        /// remove. Não usa Contains para comparação em nenhum lugar do resolvedor.
        /// </summary>
        public static string NormalizarCabecalho(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return string.Empty;

            string texto = valor
                .Replace(' ', ' ')
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Trim();

            texto = System.Text.RegularExpressions.Regex.Replace(texto, @"\s+", " ");

            return texto.ToUpperInvariant();
        }

        private static string ResolverAlias(string cabecalhoNormalizado)
        {
            return AliasesCabecalho.TryGetValue(cabecalhoNormalizado, out var canonico)
                ? canonico
                : cabecalhoNormalizado;
        }

        /// <summary>
        /// Localiza, dentre as abas do arquivo, aquela cujo nome normalizado seja EXATAMENTE "LDP"
        /// (tolerando maiúsculas/minúsculas e espaços nas pontas/não separáveis) — nunca por Contains,
        /// já que a pasta pode ter outras abas com nomes parecidos. Interrompe com erro controlado se
        /// não encontrar exatamente uma.
        /// </summary>
        public static ExcelWorksheet LocalizarAbaLdp(ExcelPackage package)
        {
            var candidatas = package.Workbook.Worksheets
                .Where(w => NormalizarCabecalho(w.Name) == "LDP")
                .ToList();

            if (candidatas.Count == 0)
            {
                throw new LdpTipo2LayoutException("[LDP TIPO 2] Aba 'LDP' não encontrada.");
            }

            if (candidatas.Count > 1)
            {
                throw new LdpTipo2LayoutException(
                    $"[LDP TIPO 2] Mais de uma aba corresponde a 'LDP' após normalização: {string.Join(", ", candidatas.Select(w => w.Name))}.");
            }

            return candidatas[0];
        }

        /// <summary>
        /// Lê TODA uma linha (coluna 1 até worksheet.Dimension.End.Column) e monta a lista ordenada de
        /// ocorrências não vazias — células vazias NUNCA encerram a varredura, porque existem colunas
        /// separadoras vazias entre os blocos (custos / bloco 30% / bloco 60% / bloco 90% / indicadores).
        /// </summary>
        private static List<LdpTipo2HeaderOccurrence> LerOcorrenciasDaLinha(ExcelWorksheet ws, int linha)
        {
            var ocorrencias = new List<LdpTipo2HeaderOccurrence>();
            int maxCol = ws.Dimension?.End.Column ?? 0;

            for (int col = 1; col <= maxCol; col++)
            {
                string original = ws.Cells[linha, col].Text;
                string normalizado = ResolverAlias(NormalizarCabecalho(original));

                if (string.IsNullOrEmpty(normalizado)) continue; // vazio: não interrompe a varredura

                ocorrencias.Add(new LdpTipo2HeaderOccurrence
                {
                    Coluna = col,
                    TextoOriginal = original,
                    TextoNormalizado = normalizado
                });
            }

            return ocorrencias;
        }

        /// <summary>
        /// Localiza dinamicamente a linha de cabeçalho: varre as linhas (até o limite de segurança) até
        /// encontrar a primeira em que N&amp;A DOCUMENT NUMBER, CLIENT DOCUMENT NUMBER e DOCUMENT TITLE
        /// aparecem simultaneamente (cada uma em qualquer coluna, já normalizadas). Nunca assume uma
        /// linha fixa — se nenhuma linha satisfizer as 3 âncoras, falha de forma controlada explicando o
        /// que foi encontrado em cada linha examinada.
        /// </summary>
        public static int LocalizarLinhaCabecalho(ExcelWorksheet ws)
        {
            int maxRow = ws.Dimension?.End.Row ?? 0;
            int limite = Math.Min(maxRow, LimiteVarreduraLinhaCabecalho);
            var diagnostico = new List<string>();

            for (int linha = 1; linha <= limite; linha++)
            {
                var ocorrencias = LerOcorrenciasDaLinha(ws, linha);
                if (ocorrencias.Count == 0) continue;

                bool temNa = ocorrencias.Any(o => string.Equals(o.TextoNormalizado, LdpTipo2CabecalhosComuns.NaDocumentNumber, StringComparison.OrdinalIgnoreCase));
                bool temClient = ocorrencias.Any(o => string.Equals(o.TextoNormalizado, LdpTipo2CabecalhosComuns.ClientDocumentNumber, StringComparison.OrdinalIgnoreCase));
                bool temTitulo = ocorrencias.Any(o => string.Equals(o.TextoNormalizado, LdpTipo2CabecalhosComuns.DocumentTitle, StringComparison.OrdinalIgnoreCase));

                if (temNa && temClient && temTitulo) return linha;

                diagnostico.Add($"Linha {linha}: N&A={(temNa ? "sim" : "não")}, Client={(temClient ? "sim" : "não")}, Título={(temTitulo ? "sim" : "não")} ({ocorrencias.Count} cabeçalho(s) não vazio(s)).");
            }

            string detalhe = diagnostico.Count > 0
                ? "Linhas com conteúdo examinadas: " + string.Join(" | ", diagnostico)
                : $"Nenhuma célula não vazia nas primeiras {limite} linha(s).";

            throw new LdpTipo2LayoutException(
                $"[LDP TIPO 2] Linha de cabeçalho não localizada (varredura até a linha {limite}). Esperado " +
                $"'{LdpTipo2CabecalhosComuns.NaDocumentNumber}' + '{LdpTipo2CabecalhosComuns.ClientDocumentNumber}' + " +
                $"'{LdpTipo2CabecalhosComuns.DocumentTitle}' simultaneamente na mesma linha. {detalhe}");
        }

        /// <summary>
        /// Resolve o bloco manual de UM gate (seção 9): a partir de CADA ocorrência de "FASE {p}%",
        /// verifica se os PRÓXIMOS 4 cabeçalhos NÃO VAZIOS da linha do cabeçalho (nesta mesma lista de
        /// ocorrências, que já só contém cabeçalhos não vazios) formam, como CONJUNTO — em QUALQUER
        /// ordem interna —, os 4 cabeçalhos DOCUMENT STATUS / SITUATION / PLANNED DATA (BASE LINE) /
        /// REPLANNED DATE daquele gate. A ordem física entre esses 4 nunca é significativa; a adjacência
        /// (fisicamente os 4 próximos não vazios) continua sendo o sinal usado para diferenciar este
        /// bloco de dados do indicador de aplicabilidade (seção 10) — a ocorrência de "FASE {p}%" que
        /// faz parte do indicador consecutivo nunca é seguida por esse CONJUNTO de 4 — então é
        /// naturalmente descartada aqui, sem ambiguidade.
        /// </summary>
        private static LdpTipo2GateLayout ResolverBlocoGate(List<LdpTipo2HeaderOccurrence> ocorrencias, byte percentual, List<string> problemas)
        {
            string headerFase = LdpTipo2CabecalhosComuns.Fase(percentual);
            string headerStatus = LdpTipo2CabecalhosComuns.DocumentStatusGate(percentual);
            string headerSituation = LdpTipo2CabecalhosComuns.SituationGate(percentual);
            string headerBaseline = LdpTipo2CabecalhosComuns.PlannedBaselineGate(percentual);
            string headerReplanned = LdpTipo2CabecalhosComuns.ReplannedDateGate(percentual);

            var esperados = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                headerStatus, headerSituation, headerBaseline, headerReplanned
            };

            var blocosValidos = new List<LdpTipo2GateLayout>();

            for (int i = 0; i < ocorrencias.Count; i++)
            {
                if (!string.Equals(ocorrencias[i].TextoNormalizado, headerFase, StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 4 >= ocorrencias.Count) continue; // não há 4 cabeçalhos seguintes suficientes

                var janela = new[] { ocorrencias[i + 1], ocorrencias[i + 2], ocorrencias[i + 3], ocorrencias[i + 4] };
                var textosJanela = new HashSet<string>(janela.Select(o => o.TextoNormalizado), StringComparer.OrdinalIgnoreCase);

                if (!textosJanela.SetEquals(esperados)) continue;

                // Conjunto bate — mapeia cada texto para sua coluna real (a ordem física dentro da
                // janela é irrelevante a partir daqui).
                var porTexto = janela.ToDictionary(o => o.TextoNormalizado, o => o.Coluna, StringComparer.OrdinalIgnoreCase);

                blocosValidos.Add(new LdpTipo2GateLayout
                {
                    Percentual = percentual,
                    ColunaFaseManual = ocorrencias[i].Coluna,
                    ColunaDocumentStatus = porTexto[headerStatus],
                    ColunaSituation = porTexto[headerSituation],
                    ColunaPlannedBaseline = porTexto[headerBaseline],
                    ColunaReplannedDate = porTexto[headerReplanned]
                });
            }

            if (blocosValidos.Count == 0)
            {
                problemas.Add(
                    $"Bloco manual do gate {percentual}% não encontrado (sequência esperada: '{headerFase}' / '{headerStatus}' / " +
                    $"'{headerSituation}' / '{headerBaseline}' / '{headerReplanned}').");
                return null;
            }

            if (blocosValidos.Count > 1)
            {
                problemas.Add($"Mais de um bloco manual válido encontrado para o gate {percentual}% — layout ambíguo.");
                return null;
            }

            return blocosValidos[0];
        }

        /// <summary>
        /// Resolve o trio consecutivo de indicadores de aplicabilidade (seção 10): procura 3 colunas
        /// FISICAMENTE consecutivas (c, c+1, c+2 — sem pular vazios, célula a célula) cujo CONJUNTO de
        /// textos seja exatamente {Fase 30%, Fase 60%, Fase 90%}, em QUALQUER ordem interna entre elas
        /// (ex.: Fase 60% / Fase 30% / Fase 90% também é válido). A adjacência física literal continua
        /// sendo o único sinal usado para diferenciar este indicador do bloco de dados de cada gate
        /// (seção 9) — nunca assume que o trio está no fim da planilha nem numa ordem fixa.
        /// </summary>
        private static (int Col30, int Col60, int Col90)? ResolverIndicadoresAplicabilidade(List<LdpTipo2HeaderOccurrence> ocorrencias, List<string> problemas)
        {
            string fase30 = LdpTipo2CabecalhosComuns.Fase(30);
            string fase60 = LdpTipo2CabecalhosComuns.Fase(60);
            string fase90 = LdpTipo2CabecalhosComuns.Fase(90);
            var esperado = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { fase30, fase60, fase90 };

            var porColuna = ocorrencias.ToDictionary(o => o.Coluna, o => o.TextoNormalizado);

            var candidatos = new List<(int Col30, int Col60, int Col90)>();

            foreach (var o in ocorrencias)
            {
                if (!esperado.Contains(o.TextoNormalizado)) continue;
                if (!porColuna.TryGetValue(o.Coluna, out var t0)) continue;
                if (!porColuna.TryGetValue(o.Coluna + 1, out var t1)) continue;
                if (!porColuna.TryGetValue(o.Coluna + 2, out var t2)) continue;

                var trio = new HashSet<string>(new[] { t0, t1, t2 }, StringComparer.OrdinalIgnoreCase);
                if (!trio.SetEquals(esperado)) continue;

                // Mapeia cada texto do trio para sua coluna real — a ordem física entre as 3 é irrelevante.
                var porTexto = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    [t0] = o.Coluna,
                    [t1] = o.Coluna + 1,
                    [t2] = o.Coluna + 2
                };
                candidatos.Add((porTexto[fase30], porTexto[fase60], porTexto[fase90]));
            }

            if (candidatos.Count == 0 || candidatos.Count > 1)
            {
                problemas.Add(
                    "[LDP TIPO 2] O trio consecutivo 'Fase 30% | Fase 60% | Fase 90%' não foi encontrado de forma única na linha do cabeçalho" +
                    (candidatos.Count == 0 ? " (nenhum trio consecutivo)." : $" ({candidatos.Count} trios consecutivos)."));
                return null;
            }

            return candidatos[0];
        }

        /// <summary>
        /// Resolve o layout completo (colunas comuns + 3 blocos de gate + indicadores) a partir da
        /// linha 7 já lida. Agrega TODOS os problemas antes de decidir — se houver qualquer um, lança
        /// LdpTipo2LayoutException com a lista inteira (nunca interrompe na primeira falha isolada).
        /// Avisos de campos opcionais ausentes são devolvidos por <paramref name="avisos"/> e NÃO
        /// interrompem a resolução.
        /// </summary>
        public static LdpTipo2Layout ResolverLayout(ExcelWorksheet ws, ILogger logger, string nomeArquivo, List<string> avisos)
        {
            if (avisos == null) throw new ArgumentNullException(nameof(avisos));

            int linhaCabecalho = LocalizarLinhaCabecalho(ws);
            var ocorrencias = LerOcorrenciasDaLinha(ws, linhaCabecalho);
            var mapa = new LdpTipo2HeaderMap(ocorrencias);
            var problemas = new List<string>();

            var colunasComuns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            void ResolverComum(string cabecalho, bool obrigatorio)
            {
                var ocs = mapa.ObterOcorrencias(cabecalho);
                if (ocs.Count == 1)
                {
                    colunasComuns[cabecalho] = ocs[0].Coluna;
                }
                else if (ocs.Count == 0)
                {
                    if (obrigatorio) problemas.Add($"Cabeçalho obrigatório ausente: '{cabecalho}'.");
                    else avisos.Add($"[LDP TIPO 2] Cabeçalho opcional ausente: '{cabecalho}'.");
                }
                else
                {
                    string msg = $"Cabeçalho '{cabecalho}' aparece {ocs.Count} vez(es) na linha 7 (esperado: 1).";
                    if (obrigatorio) problemas.Add(msg);
                    else avisos.Add("[LDP TIPO 2] " + msg + " Campo opcional não resolvido.");
                }
            }

            foreach (var cab in LdpTipo2CabecalhosComuns.Obrigatorios) ResolverComum(cab, obrigatorio: true);
            foreach (var cab in LdpTipo2CabecalhosComuns.Opcionais) ResolverComum(cab, obrigatorio: false);

            var gate30 = ResolverBlocoGate(ocorrencias, 30, problemas);
            var gate60 = ResolverBlocoGate(ocorrencias, 60, problemas);
            var gate90 = ResolverBlocoGate(ocorrencias, 90, problemas);

            var indicadores = ResolverIndicadoresAplicabilidade(ocorrencias, problemas);

            if (problemas.Count > 0)
            {
                throw new LdpTipo2LayoutException(problemas);
            }

            // A partir daqui, garantidamente resolvidos (senão já teria lançado acima).
            gate30.ColunaPlannedPhysical = colunasComuns[LdpTipo2CabecalhosComuns.PlannedPhysical30];
            gate30.ColunaPhysicalCompleted = colunasComuns[LdpTipo2CabecalhosComuns.PhysicalCompleted30];
            gate30.ColunaIndicadorAplicabilidade = indicadores.Value.Col30;

            gate60.ColunaPlannedPhysical = colunasComuns[LdpTipo2CabecalhosComuns.PlannedPhysical60];
            gate60.ColunaPhysicalCompleted = colunasComuns[LdpTipo2CabecalhosComuns.PhysicalCompleted60];
            gate60.ColunaIndicadorAplicabilidade = indicadores.Value.Col60;

            gate90.ColunaPlannedPhysical = colunasComuns[LdpTipo2CabecalhosComuns.PlannedPhysical90];
            gate90.ColunaPhysicalCompleted = colunasComuns[LdpTipo2CabecalhosComuns.PhysicalCompleted90];
            gate90.ColunaIndicadorAplicabilidade = indicadores.Value.Col90;

            LogarLayout(logger, nomeArquivo, ws, linhaCabecalho, ocorrencias, colunasComuns, gate30, gate60, gate90);

            return new LdpTipo2Layout
            {
                LinhaCabecalho = linhaCabecalho,
                PrimeiraLinhaDocumento = linhaCabecalho + 1,
                ColunasComuns = colunasComuns,
                Gate30 = gate30,
                Gate60 = gate60,
                Gate90 = gate90
            };
        }

        private static void LogarLayout(
            ILogger logger,
            string nomeArquivo,
            ExcelWorksheet ws,
            int linhaCabecalho,
            List<LdpTipo2HeaderOccurrence> ocorrencias,
            Dictionary<string, int> colunasComuns,
            LdpTipo2GateLayout gate30,
            LdpTipo2GateLayout gate60,
            LdpTipo2GateLayout gate90)
        {
            int maxCol = ws.Dimension?.End.Column ?? 0;

            logger?.LogInformation(
                $"[LDP TIPO 2] Layout identificado. Arquivo: {nomeArquivo} | Aba: {ws.Name} | Linha do cabeçalho: {linhaCabecalho} | " +
                $"Total de colunas examinadas: {maxCol} | Total de cabeçalhos não vazios: {ocorrencias.Count}.");

            logger?.LogInformation(
                $"[LDP TIPO 2] N&A DOCUMENT NUMBER: coluna {colunasComuns.GetValueOrDefault(LdpTipo2CabecalhosComuns.NaDocumentNumber)} | " +
                $"CLIENT DOCUMENT NUMBER: coluna {(colunasComuns.TryGetValue(LdpTipo2CabecalhosComuns.ClientDocumentNumber, out var c) ? c.ToString() : "não encontrada (opcional)")} | " +
                $"DOCUMENT TITLE: coluna {colunasComuns.GetValueOrDefault(LdpTipo2CabecalhosComuns.DocumentTitle)}.");

            void LogarGate(LdpTipo2GateLayout gate)
            {
                logger?.LogInformation(
                    $"[LDP TIPO 2] Gate {gate.Percentual}% | Planned Physical: col {gate.ColunaPlannedPhysical} | " +
                    $"Physical Completed: col {gate.ColunaPhysicalCompleted} | Fase manual: col {gate.ColunaFaseManual} | " +
                    $"Document Status: col {gate.ColunaDocumentStatus} | Situation: col {gate.ColunaSituation} | " +
                    $"Planned Baseline: col {gate.ColunaPlannedBaseline} | Replanned Date: col {gate.ColunaReplannedDate} | " +
                    $"Indicador X: col {gate.ColunaIndicadorAplicabilidade}.");
            }

            LogarGate(gate30);
            LogarGate(gate60);
            LogarGate(gate90);
        }
    }
}
