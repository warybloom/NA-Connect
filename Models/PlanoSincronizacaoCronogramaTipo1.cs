using System;
using System.Collections.Generic;
using System.Linq;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Classificação de UMA alteração percentual proposta para a LDP — nunca decidida "no escuro": toda
    /// alteração para zero precisa ser rastreável até aqui antes de ser aplicada (ver seção 6 da
    /// manutenção técnica — incidente BML011.015/016).
    /// </summary>
    public enum TipoAlteracaoPercentual
    {
        SEM_ALTERACAO,
        AUMENTO,
        REDUCAO,
        ALTERACAO_PARA_ZERO,
        PREENCHIMENTO_INICIAL,
        VALOR_ORIGEM_AUSENTE
    }

    /// <summary>
    /// Uma linha do Cronograma Tipo 1 já validada e pronta para persistência em lote — construída
    /// inteiramente na Fase A (preparação), nunca gravada durante essa fase. Ver seção 7/8 da manutenção
    /// técnica: nenhuma tarefa é persistida individualmente; o SQL só recebe a coleção completa.
    /// </summary>
    public sealed class RegistroCronogramaTipo1Preparado
    {
        public string ProjectIdAcc { get; set; }
        public string TaskUid { get; set; }
        public string DocNumber { get; set; }
        public string TaskName { get; set; }
        public decimal? PhysicalDone { get; set; }
        public decimal? PredictedDone { get; set; }
        public string WbsPercent { get; set; }
        public DateTime? Start { get; set; }
        public DateTime? Baseline { get; set; }
        public DateTime? TrendDate { get; set; }
        public string EdtWbs { get; set; }
        public string ProjetoCodigo { get; set; }
        public decimal? TrabalhoMpp { get; set; }
        public bool EhTarefaResumo { get; set; }
        public string? Disciplina { get; set; }
        public string? AnalyticsJson { get; set; }
        public bool CandidatoDocumento => !EhTarefaResumo && !string.IsNullOrWhiteSpace(DocNumber);
    }

    /// <summary>
    /// Uma alteração de célula percentual da LDP já validada e pronta — só é efetivamente aplicada na
    /// Fase B, depois do commit SQL. Uma proposta com Tipo == VALOR_ORIGEM_AUSENTE ou == SEM_ALTERACAO
    /// nunca deve ser aplicada (a célula existente é preservada).
    /// </summary>
    public sealed class AlteracaoLdpCronogramaPreparada
    {
        public int Linha { get; set; }
        public string Documento { get; set; }
        public string Campo { get; set; }
        public string ValorAtualExibido { get; set; }
        public decimal? ValorAtualInterno { get; set; }
        public object ValorBrutoMpp { get; set; }
        public string OrigemCampo { get; set; }
        public PercentualCronogramaNormalizado ValorNormalizado { get; set; }
        public decimal? ValorNovo { get; set; }
        public TipoAlteracaoPercentual TipoAlteracao { get; set; }
        public int ColunaLdp { get; set; }
        public bool CelulaFormatadaComoPercentual { get; set; }
        public string TaskUid { get; set; }

        /// <summary>
        /// True somente quando o campo REALIZADO inteiro foi comprovado mas retornou 100% zero no MPP
        /// (seção 6 do pedido) — a proposta continua registrada/logada (para auditoria), mas a célula da
        /// LDP é preservada até confirmação operacional, mesmo que o cálculo indicasse uma alteração.
        /// </summary>
        public bool ForcarNaoAplicar { get; set; }

        public bool DeveSerAplicada =>
            !ForcarNaoAplicar &&
            TipoAlteracao != TipoAlteracaoPercentual.SEM_ALTERACAO &&
            TipoAlteracao != TipoAlteracaoPercentual.VALOR_ORIGEM_AUSENTE;
    }

    /// <summary>
    /// Resultado completo da Fase A (preparação/validação) do Cronograma Tipo 1 — ver seção 7 da
    /// manutenção técnica. Nenhuma célula é alterada e nenhum SQL é gravado enquanto este plano está
    /// sendo construído; PodePersistir só é true quando TUDO validou.
    /// </summary>
    public sealed class PlanoSincronizacaoCronogramaTipo1
    {
        public int TarefasLidas { get; set; }
        public int TarefasComDocumento { get; set; }
        public int TarefasValidas { get; set; }
        public int TarefasInvalidas { get; set; }

        /// <summary>Documentos únicos indexados para casar com a LDP (seção 10) — preenchido depois da resolução de duplicidades.</summary>
        public int DocumentosUnicos { get; set; }

        /// <summary>Linhas da LDP cujo código de documento casou com alguma tarefa do cronograma (seção 10).</summary>
        public int LinhasLdpCorrespondentes { get; set; }

        /// <summary>
        /// True quando o campo REALIZADO foi comprovado estruturalmente, mas a distribuição completa
        /// (todas as tarefas com documento, não só a amostra) não tem nenhum valor não-zero (seção 6).
        /// Quando true, nenhuma proposta de %PHYSICAL COMPLETED é aplicada nesta execução — a LDP é
        /// preservada até confirmação operacional.
        /// </summary>
        public bool RealizadoTodosValoresZero { get; set; }

        /// <summary>Documentos com mais de uma tarefa-folha e valores de REALIZADO conflitantes (seção 9) — bloqueiam a persistência inteira.</summary>
        public List<string> DocumentosDuplicadosConflitantes { get; } = new List<string>();

        /// <summary>
        /// Conflitos entre tarefas-folha SEM NA DOCUMENT NUMBER (chave veio do nome da tarefa, ex.:
        /// "PREPARATION"). Não bloqueiam por si — só são promovidos a DocumentosDuplicadosConflitantes
        /// se a chave casar com um N&amp;A DOCUMENT NUMBER real da LDP.
        /// </summary>
        public Dictionary<string, string> ConflitosNomeTarefaSemNaDocumentNumber { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<RegistroCronogramaTipo1Preparado> RegistrosSqlPreparados { get; } = new List<RegistroCronogramaTipo1Preparado>();
        public List<AlteracaoLdpCronogramaPreparada> AlteracoesLdpPreparadas { get; } = new List<AlteracaoLdpCronogramaPreparada>();

        public int AlteracoesParaZero => AlteracoesLdpPreparadas.Count(a => a.TipoAlteracao == TipoAlteracaoPercentual.ALTERACAO_PARA_ZERO);

        // -------------------------------------------------------------------------------------------
        // Contadores separados de REALIZADO/PREVISTO/datas/WBS (seção 10 do pedido) — nunca reportar
        // sucesso genérico de "linhas sincronizadas" sem discriminar o que de fato mudou.
        // -------------------------------------------------------------------------------------------
        public int AtualizacoesRealizado => AlteracoesLdpPreparadas.Count(a => a.Campo == "%PHYSICAL COMPLETED" && a.DeveSerAplicada);
        public int AtualizacoesPrevisto => AlteracoesLdpPreparadas.Count(a => a.Campo == "%PLANNED PHYSICAL" && a.DeveSerAplicada);

        public int RealizadoComZero => AlteracoesLdpPreparadas.Count(a => a.Campo == "%PHYSICAL COMPLETED" && a.ValorNormalizado?.ClasseValor == ClasseValorPercentual.VALOR_ZERO_EXPLICITO);
        public int RealizadoComNaoZero => AlteracoesLdpPreparadas.Count(a => a.Campo == "%PHYSICAL COMPLETED" && a.ValorNormalizado?.ClasseValor == ClasseValorPercentual.VALOR_VALIDO);
        public int RealizadoPreservado => AlteracoesLdpPreparadas.Count(a => a.Campo == "%PHYSICAL COMPLETED" && (a.TipoAlteracao == TipoAlteracaoPercentual.SEM_ALTERACAO || a.ForcarNaoAplicar));
        public int RealizadoSemOrigemComprovada => AlteracoesLdpPreparadas.Count(a => a.Campo == "%PHYSICAL COMPLETED" && a.TipoAlteracao == TipoAlteracaoPercentual.VALOR_ORIGEM_AUSENTE);

        /// <summary>Propostas de REALIZADO com QUALQUER valor determinável (mesmo que igual ao já existente) — usado para distinguir "nada a fazer" (idempotente) de "nenhuma proposta válida" (seção 10).</summary>
        public int PropostasRealizadoComValor => AlteracoesLdpPreparadas.Count(a => a.Campo == "%PHYSICAL COMPLETED" && a.TipoAlteracao != TipoAlteracaoPercentual.VALOR_ORIGEM_AUSENTE);

        public int AtualizacoesDatas { get; set; }
        public int AtualizacoesWbs { get; set; }

        public double PercentualAlteracoesParaZero
        {
            get
            {
                int comAlteracaoRealProposta = AlteracoesLdpPreparadas.Count(a => a.DeveSerAplicada);
                return comAlteracaoRealProposta == 0 ? 0d : (double)AlteracoesParaZero / comAlteracaoRealProposta * 100d;
            }
        }

        public int ErrosAlias { get; set; }
        public int ErrosConversao { get; set; }
        public int ErrosValidacao { get; set; }

        public bool ZeramentoEmMassa { get; set; }

        public bool PodePersistir =>
            ErrosAlias == 0 && ErrosConversao == 0 && ErrosValidacao == 0 && TarefasInvalidas == 0 && !ZeramentoEmMassa;

        public string CodigoResultado { get; set; } = "OK";
        public string MensagemResumo { get; set; } = string.Empty;

        public override string ToString() => MensagemResumo;
    }
}
