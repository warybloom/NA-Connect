using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using APS.WebApp.Models;

// Importações do MPXJ — mesma biblioteca já usada pelo cronograma Tipo 1 (AccScheduleSyncService),
// nunca um segundo parser de MPP. net.sf.mpxj.Task colide em nome com System.Threading.Tasks.Task —
// mesmos aliases (MpxjTask/SystemTask) usados para desambiguar.
using net.sf.mpxj;
using net.sf.mpxj.reader;
using MpxjTask = net.sf.mpxj.Task;
using SystemTask = System.Threading.Tasks.Task;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>
    /// Lê TODAS as tarefas do MPP Tipo 2 e classifica cada uma (resumo / sem N&amp;A / sem fase / fase
    /// inválida / válida / duplicada) — nenhuma tarefa é descartada antes da persistência (ver seção 4 do
    /// pedido). Não abre SQL, não atualiza LDP/gate, não faz upload, não acessa Workflow.
    /// </summary>
    public sealed class CronogramaTipo2Parser : ICronogramaTipo2Parser
    {
        /// <summary>Valores possíveis de StatusValidacaoDocumentoGate — nunca strings soltas espalhadas pelo código.</summary>
        public static class Status
        {
            public const string TarefaResumo = "TAREFA_RESUMO";
            public const string SemNumeroNa = "SEM_NUMERO_NA";
            public const string SemFase = "SEM_FASE";
            public const string FaseInvalida = "FASE_INVALIDA";
            public const string Valido = "VALIDO";
            public const string Duplicado = "DUPLICADO";
        }

        private const string ALIAS_FASE = "Fase";
        private const string ALIAS_PERCENT_PREVISTO_LB = "% Previsto LB";
        private const string ALIAS_DISCIPLINA = "Disciplina";
        private const string ALIAS_STATUS_DOC_ENGENHARIA = "Status do Documento na Engenharia";

        // Aliases controlados com mais de uma grafia aceita (seção 5 do pedido da Fase 4B) — dois nomes
        // podem significar a mesma informação; a resolução tenta cada um, em ordem, e é sempre exata
        // (nunca Contains/fuzzy). Nenhuma variante aqui foi inventada: as três novas (Término da Linha de
        // Base / Término / Realizado) foram checadas contra os aliases customizados reais do MPP de teste
        // (CHS001-000-PMO-SC-001-05.mpp) antes de serem adicionadas — nenhuma delas existe como campo
        // customizado nesse arquivo específico, então nesse arquivo o parser usa o fallback nativo do
        // MPXJ (ver ALIAS_TERMINO_* abaixo e a leitura de Finish/BaselineFinish/WBS mais adiante).
        private static readonly string[] ALIASES_NA_DOCUMENT_NUMBER = { "NA DOCUMENT NUMBER", "N&A DOCUMENT NUMBER" };

        // Fonte oficial: custom field %realizado, resolvido pela mesma infraestrutura de aliases
        // do previsto. Aceita a grafia com espa?o; nunca usa percentuais nativos ou aliases gen?ricos.
        private static readonly string[] ALIASES_PERCENT_REALIZADO =
            { "%realizado", "% Realizado" };

        // Planned Physical (checkpoint LDP Tipo 2 seção 3): a fonte escolhida é "% Previsto Reprog."
        // (percentual previsto REPROGRAMADO — o único dos dois candidatos com dado real populado no MPP
        // de teste; "% Previsto LB" fica de fora, intocado, reservado para uso futuro), mais as mesmas
        // variantes em inglês do Tipo 1 (AccScheduleSyncService.AliasesControladosPrevisto).
        private static readonly string[] ALIASES_PERCENT_PREVISTO_REPROG =
            { "% Previsto Reprog.", "PLANNED PHYSICAL", "% PLANNED PHYSICAL", "PERCENT PREDICTED PHYSICAL" };

        private static readonly string[] ALIASES_TERMINO_LINHA_BASE = { "TÉRMINO DA LINHA DE BASE", "TERMINO DA LINHA DE BASE" };
        private static readonly string[] ALIASES_TERMINO = { "TÉRMINO", "TERMINO" };
        private const string ALIAS_WBS = "WBS";

        private readonly ILogger<CronogramaTipo2Parser> _logger;

        public CronogramaTipo2Parser(ILogger<CronogramaTipo2Parser> logger)
        {
            _logger = logger;
        }

        public Task<CronogramaTipo2Resultado> ProcessarAsync(
            string caminhoArquivoMpp,
            string projectIdAcc,
            byte tipoLdp,
            string nomeArquivo,
            DateTime dataSincronizacao,
            CancellationToken cancellationToken = default)
        {
            var dataLeituraCronograma = DateTime.UtcNow;
            var resultado = new CronogramaTipo2Resultado
            {
                ProjectIdAcc = projectIdAcc,
                NomeArquivo = nomeArquivo,
                DataSincronizacao = dataSincronizacao
            };

            ProjectFile projectFile;
            try
            {
                try { java.lang.System.setProperty("log4j2.loggerContextFactory", "org.apache.logging.log4j.simple.SimpleLoggerContextFactory"); } catch { }
                projectFile = new UniversalProjectReader().read(caminhoArquivoMpp);
            }
            catch (Exception ex)
            {
                resultado.Erros.Add($"Falha ao ler o arquivo MPP: {ex.Message}");
                _logger.LogError(ex, $"[CRONOGRAMA TIPO 2] Falha ao ler o arquivo MPP do projeto {projectIdAcc}.");
                return SystemTask.FromResult(resultado);
            }

            return SystemTask.FromResult(ProcessarProjeto(projectFile, resultado, tipoLdp, dataSincronizacao, dataLeituraCronograma, cancellationToken));
        }

        internal CronogramaTipo2Resultado ProcessarProjeto(ProjectFile projectFile, CronogramaTipo2Resultado resultado,
            byte tipoLdp, DateTime dataSincronizacao, DateTime dataLeituraCronograma, CancellationToken cancellationToken = default)
        {
            var resolver = new CronogramaTipo2FieldResolver(projectFile);

            bool naResolvido = resolver.TentarResolverQualquer(ALIASES_NA_DOCUMENT_NUMBER, out FieldType campoNa, out string aliasNaResolvido);
            bool faseResolvido = resolver.TentarResolver(ALIAS_FASE, out FieldType campoFase);
            bool prevLbResolvido = resolver.TentarResolver(ALIAS_PERCENT_PREVISTO_LB, out FieldType campoPrevistoLb);
            bool prevReprogResolvido = resolver.TentarResolverQualquer(ALIASES_PERCENT_PREVISTO_REPROG, out FieldType campoPrevistoReprog, out string aliasPrevistoReprogResolvido);
            bool realizadoResolvido = resolver.TentarResolverQualquer(ALIASES_PERCENT_REALIZADO, out FieldType campoRealizado, out string aliasRealizadoResolvido);

            // HOTFIX 4 (checkpoint "Resolução de % Realizado por título de coluna"): comprovado no MPP
            // real que "% Realizado"/"%realizado" NUNCA existiu como alias de CustomField nesse arquivo —
            // é só o TÍTULO de uma coluna de Table (TaskField.TEXT3 na Table "Entry", sem alias nenhum).
            // Só tenta esta via SEGUNDA quando a via de alias (acima) não encontrou nada; nunca substitui
            // um alias já resolvido. Nunca escolhe arbitrariamente entre FieldTypes distintos, e nunca
            // aceita o MESMO FieldType já usado por Planned Physical (Percent_Previsto_Reprogramado) —
            // mesma defesa estrutural do Tipo 1 contra os dois conceitos colidirem no mesmo campo.
            bool realizadoViaTitulo = false;
            bool avisoRealizadoTituloJaEmitido = false;
            IReadOnlyList<CronogramaTipo2FieldResolver.TituloColunaEncontrado> candidatosTituloRealizado = Array.Empty<CronogramaTipo2FieldResolver.TituloColunaEncontrado>();
            if (!realizadoResolvido)
            {
                bool encontradoPorTitulo = resolver.TentarResolverPorTituloDeTabela(
                    projectFile, ALIASES_PERCENT_REALIZADO, out FieldType campoRealizadoTitulo, out candidatosTituloRealizado);

                if (encontradoPorTitulo && prevReprogResolvido && campoRealizadoTitulo.Equals(campoPrevistoReprog))
                {
                    avisoRealizadoTituloJaEmitido = true;
                    resultado.Avisos.Add(
                        $"Título de coluna '% Realizado' resolveu para o MESMO FieldType já usado por Planned Physical ({campoRealizadoTitulo}) — rejeitado, Percent_Realizado ficará NULL.");
                }
                else if (encontradoPorTitulo)
                {
                    campoRealizado = campoRealizadoTitulo;
                    realizadoResolvido = true;
                    realizadoViaTitulo = true;
                    var achado = candidatosTituloRealizado.First(c => c.FieldType.Equals(campoRealizadoTitulo));
                    aliasRealizadoResolvido = $"{achado.TituloOriginal} (título de coluna, Table '{achado.Tabela}')";

                    _logger.LogInformation(
                        "[CRONOGRAMA TIPO 2][PHYSICAL SOURCE] ColumnTitle=\"{Titulo}\" | FieldType={FieldType} | DataType={DataType} | Table={Tabela}",
                        achado.TituloOriginal, campoRealizado, campoRealizado.getDataType(), achado.Tabela);
                }
                else if (candidatosTituloRealizado.Count > 0)
                {
                    avisoRealizadoTituloJaEmitido = true;
                    var distintos = candidatosTituloRealizado.Select(c => c.FieldType).Distinct().ToList();
                    resultado.Avisos.Add(
                        $"Título de coluna '% Realizado' aparece em {distintos.Count} FieldTypes diferentes nas Tables do MPP — ambíguo, nenhum escolhido arbitrariamente. Candidatos: " +
                        string.Join("; ", candidatosTituloRealizado.Select(c => $"{c.FieldType}=Tabela'{c.Tabela}'.Coluna'{c.TituloOriginal}'")) + ". Percent_Realizado ficará NULL.");
                }
            }

            bool disciplinaResolvido = resolver.TentarResolver(ALIAS_DISCIPLINA, out FieldType campoDisciplina);
            bool statusEngResolvido = resolver.TentarResolver(ALIAS_STATUS_DOC_ENGENHARIA, out FieldType campoStatusEngenharia);

            // Fase 4B (seção 5/6/8 do pedido): Término da Linha de Base / Término / WBS são conceitos
            // NATIVOS do MPXJ (Baseline Finish / Finish / WBS estrutural) — o alias customizado só é usado
            // se o Planejamento tiver criado um campo customizado com um desses nomes; caso contrário o
            // fallback é sempre o campo nativo da tarefa (nunca texto reconstituído).
            bool terminoLbAliasResolvido = resolver.TentarResolverQualquer(ALIASES_TERMINO_LINHA_BASE, out FieldType campoTerminoLb, out string aliasTerminoLbResolvido);
            bool terminoAliasResolvido = resolver.TentarResolverQualquer(ALIASES_TERMINO, out FieldType campoTermino, out string aliasTerminoResolvido);
            bool wbsAliasResolvido = resolver.TentarResolver(ALIAS_WBS, out FieldType campoWbsAlias);

            _logger.LogInformation(
                $"[CRONOGRAMA TIPO 2] Campos customizados com alias encontrados: {resolver.TotalCamposComAlias} | " +
                $"NA DOCUMENT NUMBER resolvido para: {(naResolvido ? $"{campoNa} (alias '{aliasNaResolvido}')" : "NÃO ENCONTRADO")} | " +
                $"Alias '{ALIAS_FASE}' resolvido para: {(faseResolvido ? campoFase.ToString() : "NÃO ENCONTRADO")}");

            _logger.LogInformation(
                $"[CRONOGRAMA TIPO 2] Fonte de Término da Linha de Base: {(terminoLbAliasResolvido ? $"campo customizado '{aliasTerminoLbResolvido}' ({campoTerminoLb})" : "campo nativo MPXJ (BaselineFinish)")} | " +
                $"Fonte de Término: {(terminoAliasResolvido ? $"campo customizado '{aliasTerminoResolvido}' ({campoTermino})" : "campo nativo MPXJ (Finish)")} | " +
                $"Fonte de WBS: campo nativo MPXJ (WBS){(wbsAliasResolvido ? $" — alias customizado '{ALIAS_WBS}' também encontrado ({campoWbsAlias}), usado apenas como reserva quando o nativo vier vazio" : "")}.");

            _logger.LogInformation(
                $"[CRONOGRAMA TIPO 2 -> LDP][PHYSICAL] Fonte de Planned Physical (Percent_Previsto_Reprogramado): {(prevReprogResolvido ? $"campo customizado '{aliasPrevistoReprogResolvido}' ({campoPrevistoReprog})" : "NÃO ENCONTRADO")} | " +
                $"Fonte de Physical Completed (Percent_Realizado): {(realizadoResolvido ? $"{(realizadoViaTitulo ? "título de coluna de Table" : "campo customizado")} '{aliasRealizadoResolvido}' ({campoRealizado})" : "NÃO ENCONTRADO")}.");

            if (!naResolvido)
                resultado.Avisos.Add($"Nenhum dos aliases '{string.Join("' / '", ALIASES_NA_DOCUMENT_NUMBER)}' foi encontrado nos campos customizados do MPP — todas as tarefas serão classificadas como '{Status.SemNumeroNa}'.");
            if (!faseResolvido)
                resultado.Avisos.Add($"Alias '{ALIAS_FASE}' não encontrado nos campos customizados do MPP — todas as tarefas com N&A serão classificadas como '{Status.SemFase}'.");

            // Aliases opcionais (seção 9 do pedido da Fase 4A): ausência não bloqueia classificação nem
            // persistência (a coluna correspondente fica NULL), mas precisa gerar aviso — nunca falhar
            // silenciosamente.
            if (!prevLbResolvido)
                resultado.Avisos.Add($"Alias opcional '{ALIAS_PERCENT_PREVISTO_LB}' não encontrado nos campos customizados do MPP — Percent_Previsto_LB ficará NULL em todos os registros.");
            if (!prevReprogResolvido)
                resultado.Avisos.Add($"Nenhum dos aliases opcionais '{string.Join("' / '", ALIASES_PERCENT_PREVISTO_REPROG)}' foi encontrado nos campos customizados do MPP — Percent_Previsto_Reprogramado (fonte de Planned Physical na LDP) ficará NULL em todos os registros.");
            if (!realizadoResolvido && !avisoRealizadoTituloJaEmitido)
                resultado.Avisos.Add($"Nenhum dos aliases opcionais '{string.Join("' / '", ALIASES_PERCENT_REALIZADO)}' foi encontrado nos campos customizados do MPP, nem como título de coluna de Table — Percent_Realizado ficará NULL em todos os registros.");
            if (!disciplinaResolvido)
                resultado.Avisos.Add($"Alias opcional '{ALIAS_DISCIPLINA}' não encontrado nos campos customizados do MPP — Disciplina_Cronograma ficará NULL em todos os registros.");
            if (!statusEngResolvido)
                resultado.Avisos.Add($"Alias opcional '{ALIAS_STATUS_DOC_ENGENHARIA}' não encontrado nos campos customizados do MPP — Status_Documento_Engenharia ficará NULL em todos os registros.");

            var totalWork = CronogramaTipo2Work.ObterTotal(projectFile, out string fonteTotal);
            _logger.LogInformation("[CRONOGRAMA TIPO 2][WORK] TotalHoras={Total} | Fonte={Fonte}", totalWork, fonteTotal);
            if (!totalWork.HasValue) resultado.Avisos.Add("Work total inv?lido: WBS (%) sem origem calcul?vel.");
            var tasks = projectFile.getTasks();
            var iterator = tasks.iterator();

            while (iterator.hasNext())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var task = (MpxjTask)iterator.next();
                if (task == null) continue;

                resultado.TarefasAnalisadas++;

                // WBS (seção 6 do pedido da Fase 4B): 1) campo nativo da tarefa; 2) se vazio e existir
                // alias customizado "WBS", usa-o como reserva; 3) senão fica NULL. Nunca cria outro campo
                // — sempre persistido em EdtWbs (EDT_WBS já existente).
                string wbsNativo = ObterTexto(task.getWBS());
                string wbsFinal = wbsNativo;
                if (string.IsNullOrEmpty(wbsFinal) && wbsAliasResolvido)
                {
                    wbsFinal = ObterTexto(task.get(campoWbsAlias));
                }

                var registro = new CronogramaTipo2Registro
                {
                    TipoLdp = tipoLdp,
                    DataSincronizacao = dataSincronizacao,
                    DataLeituraCronograma = dataLeituraCronograma,
                    TaskId = ObterInteiro(task.getID()),
                    TaskUniqueId = ObterInteiro(task.getUniqueID()),
                    NomeTarefa = ObterTexto(task.getName()),
                    EhTarefaResumo = task.getSummary(),
                    EdtWbs = wbsFinal
                };

                string naOriginal = naResolvido ? ObterTexto(task.get(campoNa)) : null;
                registro.NumeroDocumentoNaOriginal = naOriginal;
                registro.NumeroDocumentoNaNormalizado = CronogramaTipo2DocumentNumberNormalizer.Normalizar(naOriginal);

                registro.FaseOriginal = faseResolvido ? ObterTexto(task.get(campoFase)) : null;

                registro.PercentPrevistoLb = prevLbResolvido ? ConverterPercentualParaPontos(task.get(campoPrevistoLb)) : null;
                registro.PercentPrevistoReprogramado = prevReprogResolvido ? ConverterPercentualParaPontos(task.get(campoPrevistoReprog)) : null;
                registro.PercentRealizado = realizadoResolvido ? ConverterPercentualParaPontos(task.get(campoRealizado)) : null;
                registro.Disciplina = disciplinaResolvido ? ObterTexto(task.get(campoDisciplina)) : null;
                registro.StatusDocumentoEngenharia = statusEngResolvido ? ObterTexto(task.get(campoStatusEngenharia)) : null;

                // Término da Linha de Base / Término (seção 8 do pedido da Fase 4B): se resolvido por
                // alias customizado, lê o valor bruto daquele campo (preservando o tipo Date/LocalDateTime
                // nativo, se for esse o caso); senão usa diretamente o campo nativo da tarefa
                // (BaselineFinish/Finish). Nunca convertido por texto quando existe valor de data nativo.
                registro.TerminoLinhaBase = terminoLbAliasResolvido
                    ? ConverterDataNativa(task.get(campoTerminoLb))
                    : ConverterDataNativa(task.getBaselineFinish());
                registro.Termino = terminoAliasResolvido
                    ? ConverterDataNativa(task.get(campoTermino))
                    : ConverterDataNativa(task.getFinish());

                registro.CamposCustomizadosJson = SerializarCamposCustomizados(resolver, task, totalWork, fonteTotal);

                ClassificarTarefa(registro, resultado);

                resultado.Registros.Add(registro);
            }

            DetectarDuplicidades(resultado);

            resultado.TarefasPersistiveis = resultado.Registros.Count;

            if (resultado.TarefasAnalisadas != resultado.Registros.Count)
            {
                resultado.Erros.Add(
                    $"Inconsistência interna: TarefasAnalisadas ({resultado.TarefasAnalisadas}) != Registros.Count ({resultado.Registros.Count}).");
            }

            return resultado;
        }

        /// <summary>
        /// Classificação (seção 4 do pedido) — nunca filtra a tarefa da lista, só define
        /// EhCandidatoDocumentoGate / StatusValidacaoDocumentoGate.
        /// </summary>
        private static void ClassificarTarefa(CronogramaTipo2Registro registro, CronogramaTipo2Resultado resultado)
        {
            if (registro.EhTarefaResumo)
            {
                registro.EhCandidatoDocumentoGate = false;
                registro.StatusValidacaoDocumentoGate = Status.TarefaResumo;
                resultado.TarefasResumo++;
                return;
            }

            if (string.IsNullOrWhiteSpace(registro.NumeroDocumentoNaOriginal))
            {
                registro.EhCandidatoDocumentoGate = false;
                registro.StatusValidacaoDocumentoGate = Status.SemNumeroNa;
                resultado.TarefasSemNumeroNa++;
                return;
            }

            if (string.IsNullOrWhiteSpace(registro.FaseOriginal))
            {
                registro.EhCandidatoDocumentoGate = false;
                registro.StatusValidacaoDocumentoGate = Status.SemFase;
                resultado.TarefasSemFase++;
                return;
            }

            if (!CronogramaTipo2PhaseParser.TentarObterGatePercentual(registro.FaseOriginal, out byte gatePercentual))
            {
                registro.EhCandidatoDocumentoGate = false;
                registro.StatusValidacaoDocumentoGate = Status.FaseInvalida;
                registro.MensagemValidacaoDocumentoGate = $"Valor de Fase '{registro.FaseOriginal}' não corresponde a 30%, 60% ou 90%.";
                resultado.TarefasComFaseInvalida++;
                return;
            }

            registro.GatePercentual = gatePercentual;
            registro.EhCandidatoDocumentoGate = true;
            registro.StatusValidacaoDocumentoGate = Status.Valido;
            registro.ChaveDocumentoGate = CronogramaTipo2DocumentNumberNormalizer.MontarChaveDocumentoGate(registro.NumeroDocumentoNaNormalizado, gatePercentual);
            resultado.RegistrosValidos++;
        }

        /// <summary>
        /// Depois de classificar todas as tarefas, agrupa os candidatos VALIDO por
        /// NumeroDocumentoNaNormalizado + GatePercentual (seção 14 do pedido). Se houver mais de um
        /// registro para a mesma chave, TODOS permanecem persistidos e TODOS são marcados DUPLICADO —
        /// nunca escolhe o primeiro, o último, nem consolida valores.
        /// </summary>
        private static void DetectarDuplicidades(CronogramaTipo2Resultado resultado)
        {
            var grupos = resultado.Registros
                .Where(r => r.StatusValidacaoDocumentoGate == Status.Valido)
                .GroupBy(r => r.ChaveDocumentoGate, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var grupo in grupos)
            {
                foreach (var registro in grupo)
                {
                    registro.EhCandidatoDocumentoGate = false;
                    registro.StatusValidacaoDocumentoGate = Status.Duplicado;
                    registro.MensagemValidacaoDocumentoGate = $"Chave '{registro.ChaveDocumentoGate}' duplicada {grupo.Count()}x no mesmo arquivo — nenhum registro foi escolhido, todos persistidos para auditoria.";
                    resultado.RegistrosValidos--;
                    resultado.RegistrosDuplicados++;
                }

                resultado.Avisos.Add($"Chave '{grupo.Key}' duplicada {grupo.Count()}x — todos os registros marcados '{Status.Duplicado}', nenhum será usado no matching.");
            }
        }

        /// <summary>
        /// JSON determinístico (seção 7 do pedido): uma entrada por alias com campo customizado
        /// resolvido, chaves ordenadas alfabeticamente, alias original preservado, valor formatado como
        /// texto de exibição (nunca token/credencial — só dados de cronograma).
        /// </summary>
        private static string SerializarCamposCustomizados(CronogramaTipo2FieldResolver resolver, MpxjTask task, decimal? totalWork, string fonteTotal)
        {
            var dicionario = new SortedDictionary<string, string>(StringComparer.Ordinal);

            foreach (var kvp in resolver.AliasesOriginais)
            {
                string aliasOriginal = kvp.Key;
                FieldType tipo = kvp.Value;
                object valorBruto = task.get(tipo);

                dicionario[aliasOriginal] = ObterValorExibicao(valorBruto);
            }

            dicionario[CronogramaTipo2Work.ChaveSnapshot] = JsonSerializer.Serialize(new CronogramaTipo2Work.Snapshot
            { ItemHoras = CronogramaTipo2Work.LerHoras(task), TotalHoras = totalWork, FonteTotal = fonteTotal });
            return JsonSerializer.Serialize(dicionario);
        }

        private static string ObterValorExibicao(object valorBruto)
        {
            if (valorBruto == null) return "";

            if (valorBruto is java.lang.Number numero)
            {
                double bruto = numero.doubleValue();
                return bruto.ToString("0.##", new CultureInfo("pt-BR"));
            }

            if (valorBruto is java.util.Date data)
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(data.getTime()).DateTime;
                return dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            }

            return valorBruto.ToString()?.Trim() ?? "";
        }

        /// <summary>
        /// Converte o valor bruto de um campo percentual em pontos percentuais (seção 7 do pedido):
        /// "53,11%" → 53.11; uma fração "0,5311" (campo percentual nativo do MPXJ, &lt;= 1) também vira
        /// 53.11. Armazenado apenas — não usado nesta fase para atualizar LDP/gate.
        /// </summary>
        private static decimal? ConverterPercentualParaPontos(object valorBruto)
        {
            if (valorBruto == null) return null;

            if (valorBruto is java.lang.Number numero)
            {
                double bruto = numero.doubleValue();
                if (bruto > 0 && bruto <= 1) bruto *= 100.0;
                return Convert.ToDecimal(bruto, CultureInfo.InvariantCulture);
            }

            string texto = valorBruto.ToString();
            if (string.IsNullOrWhiteSpace(texto)) return null;

            string limpo = texto.Replace("%", "").Trim();

            if (decimal.TryParse(limpo, NumberStyles.Any, new CultureInfo("pt-BR"), out decimal valorBr)) return valorBr;
            if (decimal.TryParse(limpo, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal valorInv)) return valorInv;

            return null;
        }

        /// <summary>
        /// Converte o tipo de data nativo devolvido pelo MPXJ (seção 8 do pedido da Fase 4B) — nunca por
        /// texto. Nas versões atuais do MPXJ.Net, Task.getFinish()/getBaselineFinish() devolvem
        /// java.time.LocalDateTime (confirmado por inspeção direta no MPP de teste); campos customizados
        /// de data eventualmente ainda usam java.util.Date. Os dois são tratados — preservando o horário,
        /// nunca truncado para meia-noite.
        /// </summary>
        private static DateTime? ConverterDataNativa(object valorBruto)
        {
            if (valorBruto == null) return null;

            if (valorBruto is java.time.LocalDateTime localDateTime)
            {
                return new DateTime(
                    localDateTime.getYear(),
                    localDateTime.getMonthValue(),
                    localDateTime.getDayOfMonth(),
                    localDateTime.getHour(),
                    localDateTime.getMinute(),
                    localDateTime.getSecond());
            }

            if (valorBruto is java.util.Date data)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(data.getTime()).DateTime;
            }

            return null;
        }

        private static string ObterTexto(object valorMpxj)
        {
            if (valorMpxj == null) return null;
            string texto = valorMpxj.ToString();
            return string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        }

        private static int? ObterInteiro(object valorMpxj)
        {
            if (valorMpxj == null) return null;
            if (valorMpxj is java.lang.Integer inteiro) return inteiro.intValue();
            if (int.TryParse(valorMpxj.ToString(), out int resultado)) return resultado;
            return null;
        }
    }
}
