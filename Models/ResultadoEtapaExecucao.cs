using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Status possíveis de uma etapa de execução do Worker (ver GrdAutomationWorker.ProcessarCicloCompleto).
    /// Existe para impedir que uma etapa com falha parcial seja relatada como "sucesso" apenas porque o
    /// ciclo do projeto não lançou uma exceção não tratada.
    /// </summary>
    public enum StatusEtapaExecucao
    {
        SUCESSO,
        AVISO_OPERACIONAL,
        ERRO_TECNICO,
        NAO_EXECUTADO
    }

    /// <summary>
    /// Status final consolidado de UM projeto dentro do lote — ver ResultadoEtapaExecucao.ConsolidarProjeto.
    /// </summary>
    public enum StatusFinalProjeto
    {
        CONCLUIDO_COM_SUCESSO,
        CONCLUIDO_COM_AVISOS_OPERACIONAIS,
        CONCLUIDO_COM_ERROS_PARCIAIS,
        NAO_PROCESSADO,
        FALHA_CRITICA
    }

    /// <summary>
    /// Resultado consolidado de uma única etapa do robô (Etapa 0 a 17). Não substitui os retornos atuais
    /// dos serviços (strings, LdpUpdateResult, etc.) — é montado pelo Worker A PARTIR desses retornos, via
    /// <see cref="Classificar"/>, para permitir um resumo final honesto (ver ConsolidarProjeto).
    /// </summary>
    public sealed class ResultadoEtapaExecucao
    {
        public int NumeroEtapa { get; set; }
        public string NomeEtapa { get; set; }
        public StatusEtapaExecucao Status { get; set; }
        public string Categoria { get; set; }
        public string Mensagem { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime Fim { get; set; }
        public TimeSpan Duracao => Fim - Inicio;
        public string ExceptionType { get; set; }
        public string CodigoErro { get; set; }
        public bool EhObrigatoria { get; set; }

        /// <summary>
        /// Frases (já em uso hoje pelos serviços de sincronização) que indicam ausência operacional —
        /// nunca erro técnico do sistema — mesmo quando a mensagem começa com "Erro:"/"Falha:"/"ERRO:".
        /// Comparação sempre sobre texto normalizado (maiúsculas, sem acento) para tolerar variações.
        /// </summary>
        private static readonly string[] MarcadoresAvisoOperacional =
        {
            "PASTA NAO ENCONTRADA",
            "NENHUM EXCEL ENCONTRADO",
            "NENHUM ARQUIVO EXCEL ENCONTRADO",
            "NAO LOCALIZADO",
            "AINDA NAO POSSUI",
            "PLANILHA_FORA_DO_PADRAO_ACTION_ITEM_LIST",
            "PROJETO_SEM_LDP_PRINCIPAL",
            "DNA_NAO_IDENTIFICADO"
        };

        private static readonly string[] MarcadoresErroTecnico =
        {
            "OVERFLOW",
            "ARITHMETIC_OVERFLOW_SQL_8115",
            "SQLEXCEPTION",
            "EXCEPTION"
        };

        private static string Normalizar(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
            string semAcento = texto.Normalize(NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (char c in semAcento)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }
            return sb.ToString().ToUpperInvariant();
        }

        /// <summary>
        /// Aba obrigatória ausente: exceção lançada por PlanningControlSyncService.SelecionarWorksheet
        /// ("... exige a aba 'ACTION ITEM LIST', mas ela não foi encontrada. ...") propagada como
        /// "ERRO: {mensagem}". É aviso operacional (código PLANILHA_FORA_DO_PADRAO_ACTION_ITEM_LIST),
        /// nunca erro técnico — ver seção 9 da manutenção: CAP/CAP1 não substituem ACTION ITEM LIST.
        /// </summary>
        private static bool ContemAbaObrigatoriaAusente(string mensagemNormalizada) =>
            mensagemNormalizada.Contains("EXIGE A ABA") && mensagemNormalizada.Contains("ACTION ITEM LIST");

        /// <summary>
        /// Classifica o retorno textual (ou a exceção) de uma etapa do Worker. Não altera o que o serviço
        /// devolveu — apenas interpreta. Regra de prioridade: 1) exceção explícita capturada pelo Worker
        /// (sempre ERRO_TECNICO); 2) ausência operacional conhecida (mesmo com prefixo "Erro:"/"Falha:");
        /// 3) indicador técnico (prefixo Erro:/Falha:, overflow, exception); 4) sucesso (default).
        /// </summary>
        public static ResultadoEtapaExecucao Classificar(
            int numeroEtapa,
            string nomeEtapa,
            string mensagemRetorno,
            DateTime inicio,
            DateTime fim,
            bool ehObrigatoria = false,
            Exception excecao = null)
        {
            StatusEtapaExecucao status;
            string categoria;
            string codigoErro = null;
            string exceptionType = excecao?.GetType().Name;

            if (excecao != null)
            {
                status = StatusEtapaExecucao.ERRO_TECNICO;
                categoria = "EXCECAO_NAO_TRATADA";
                if (excecao is SqlException sqlEx)
                {
                    codigoErro = sqlEx.Number.ToString(CultureInfo.InvariantCulture);
                    categoria = sqlEx.Number == 8115 ? "ARITHMETIC_OVERFLOW_SQL_8115" : $"SQLEXCEPTION_{sqlEx.Number}";
                }
                mensagemRetorno ??= excecao.Message;
            }
            else
            {
                string norm = Normalizar(mensagemRetorno);

                bool ehAvisoOperacional = ContemAbaObrigatoriaAusente(norm) ||
                    MarcadoresAvisoOperacional.Any(norm.Contains) ||
                    (mensagemRetorno?.TrimStart().StartsWith("⚠") ?? false);

                bool ehErroTecnico = MarcadoresErroTecnico.Any(norm.Contains) ||
                    norm.TrimStart().StartsWith("ERRO:") ||
                    norm.TrimStart().StartsWith("FALHA:");

                if (ehAvisoOperacional)
                {
                    status = StatusEtapaExecucao.AVISO_OPERACIONAL;
                    categoria = ContemAbaObrigatoriaAusente(norm) ? "PLANILHA_FORA_DO_PADRAO_ACTION_ITEM_LIST" : "AUSENCIA_OPERACIONAL";
                }
                else if (ehErroTecnico)
                {
                    status = StatusEtapaExecucao.ERRO_TECNICO;
                    categoria = norm.Contains("ARITHMETIC_OVERFLOW_SQL_8115") ? "ARITHMETIC_OVERFLOW_SQL_8115"
                        : norm.Contains("CONFIGURACAO_CRONOGRAMA_AMBIGUA") ? "CONFIGURACAO_CRONOGRAMA_AMBIGUA"
                        : norm.Contains("CRONOGRAMA_ALIAS_REALIZADO_NAO_ENCONTRADO") ? "CRONOGRAMA_ALIAS_REALIZADO_NAO_ENCONTRADO"
                        : norm.Contains("CRONOGRAMA_ALIAS_PREVISTO_NAO_ENCONTRADO") ? "CRONOGRAMA_ALIAS_PREVISTO_NAO_ENCONTRADO"
                        : norm.Contains("CRONOGRAMA_ALIAS_MULTIPLO") ? "CRONOGRAMA_ALIAS_MULTIPLO"
                        : norm.Contains("CRONOGRAMA_ZERAMENTO_EM_MASSA_BLOQUEADO") ? "CRONOGRAMA_ZERAMENTO_EM_MASSA_BLOQUEADO"
                        : norm.Contains("CRONOGRAMA_SQL_FALHOU") ? "CRONOGRAMA_SQL_FALHOU"
                        : norm.Contains("CRONOGRAMA_SNAPSHOT_INCOMPLETO") ? "CRONOGRAMA_SNAPSHOT_INCOMPLETO"
                        : norm.Contains("NCR_CLIENTE_NAO_RESOLVIDO") ? "NCR_CLIENTE_NAO_RESOLVIDO"
                        : "RETORNO_ERRO";
                }
                else
                {
                    status = StatusEtapaExecucao.SUCESSO;
                    categoria = "OK";
                }
            }

            return new ResultadoEtapaExecucao
            {
                NumeroEtapa = numeroEtapa,
                NomeEtapa = nomeEtapa,
                Status = status,
                Categoria = categoria,
                Mensagem = mensagemRetorno,
                Inicio = inicio,
                Fim = fim,
                ExceptionType = exceptionType,
                CodigoErro = codigoErro,
                EhObrigatoria = ehObrigatoria
            };
        }

        /// <summary>
        /// Constrói diretamente uma etapa NAO_EXECUTADO (projeto pulado antes do fluxo principal, etapa
        /// não aplicável, ou etapa nunca alcançada por causa de uma falha crítica anterior).
        /// </summary>
        public static ResultadoEtapaExecucao NaoExecutada(int numeroEtapa, string nomeEtapa, string motivo, bool ehObrigatoria, DateTime momento) =>
            new ResultadoEtapaExecucao
            {
                NumeroEtapa = numeroEtapa,
                NomeEtapa = nomeEtapa,
                Status = StatusEtapaExecucao.NAO_EXECUTADO,
                Categoria = motivo,
                Mensagem = motivo,
                Inicio = momento,
                Fim = momento,
                EhObrigatoria = ehObrigatoria
            };

        /// <summary>
        /// Consolida a lista de etapas de UM projeto no status final do projeto (seção 5 da manutenção).
        /// <paramref name="falhaCriticaNaoTratada"/> deve ser true quando uma exceção não tratada por
        /// nenhuma etapa individual encerrou o processamento do projeto (o catch externo do loop de
        /// projetos em GrdAutomationWorker) — nesse caso o status é sempre FALHA_CRITICA, independente do
        /// que as etapas já registradas mostrem.
        /// </summary>
        public static StatusFinalProjeto ConsolidarProjeto(IReadOnlyList<ResultadoEtapaExecucao> etapas, bool falhaCriticaNaoTratada)
        {
            if (falhaCriticaNaoTratada) return StatusFinalProjeto.FALHA_CRITICA;

            bool existeErroTecnico = etapas.Any(e => e.Status == StatusEtapaExecucao.ERRO_TECNICO);
            bool existeAviso = etapas.Any(e => e.Status == StatusEtapaExecucao.AVISO_OPERACIONAL);
            bool existeObrigatoriaNaoExecutada = etapas.Any(e => e.Status == StatusEtapaExecucao.NAO_EXECUTADO && e.EhObrigatoria);
            bool existeAlgumaFuncional = etapas.Any(e => e.Status == StatusEtapaExecucao.SUCESSO || e.Status == StatusEtapaExecucao.ERRO_TECNICO || e.Status == StatusEtapaExecucao.AVISO_OPERACIONAL);

            if (!existeAlgumaFuncional) return StatusFinalProjeto.NAO_PROCESSADO;
            if (existeErroTecnico) return StatusFinalProjeto.CONCLUIDO_COM_ERROS_PARCIAIS;
            if (existeAviso || existeObrigatoriaNaoExecutada) return StatusFinalProjeto.CONCLUIDO_COM_AVISOS_OPERACIONAIS;
            return StatusFinalProjeto.CONCLUIDO_COM_SUCESSO;
        }

        public static string DescreverStatusFinal(StatusFinalProjeto status) => status switch
        {
            StatusFinalProjeto.CONCLUIDO_COM_SUCESSO => "CONCLUÍDO COM SUCESSO",
            StatusFinalProjeto.CONCLUIDO_COM_AVISOS_OPERACIONAIS => "CONCLUÍDO COM AVISOS OPERACIONAIS",
            StatusFinalProjeto.CONCLUIDO_COM_ERROS_PARCIAIS => "CONCLUÍDO COM ERROS PARCIAIS",
            StatusFinalProjeto.NAO_PROCESSADO => "NÃO PROCESSADO",
            StatusFinalProjeto.FALHA_CRITICA => "FALHA CRÍTICA",
            _ => status.ToString()
        };
    }
}
