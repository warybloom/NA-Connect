using System;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Uma linha (tarefa) do cronograma Tipo 2, pronta para persistência em dbo.Cronograma_Projetos.
    /// TODA tarefa do MPP vira um registro aqui — resumo, sem N&amp;A, sem fase, com fase inválida ou
    /// válida — nada é filtrado antes da persistência (ver seção 4 do pedido da Fase 4A). O que muda
    /// entre elas é apenas EhCandidatoDocumentoGate / StatusValidacaoDocumentoGate.
    /// </summary>
    public sealed class CronogramaTipo2Registro
    {
        public int? TaskId { get; set; }
        public int? TaskUniqueId { get; set; }

        public string NomeTarefa { get; set; }

        public bool EhTarefaResumo { get; set; }

        public byte TipoLdp { get; set; }

        public string NumeroDocumentoNaOriginal { get; set; }
        public string NumeroDocumentoNaNormalizado { get; set; }

        public string FaseOriginal { get; set; }
        public byte? GatePercentual { get; set; }

        public bool EhCandidatoDocumentoGate { get; set; }

        public string ChaveDocumentoGate { get; set; }

        public string StatusValidacaoDocumentoGate { get; set; }
        public string MensagemValidacaoDocumentoGate { get; set; }

        public decimal? PercentPrevistoLb { get; set; }
        public decimal? PercentPrevistoReprogramado { get; set; }
        public decimal? PercentRealizado { get; set; }

        public string Disciplina { get; set; }
        public string StatusDocumentoEngenharia { get; set; }

        public string CamposCustomizadosJson { get; set; }

        public DateTime DataSincronizacao { get; set; }
        public DateTime DataLeituraCronograma { get; set; }

        // ---- Fase 4B: Término/Término da Linha de Base (lidos do MPP) e status de matching com a LDP
        // (preenchido depois, pelo CronogramaTipo2LdpSyncService — nulo no momento da leitura/INSERT do
        // parser). Ver seção 7 do pedido da Fase 4B.
        public DateTime? TerminoLinhaBase { get; set; }
        public DateTime? Termino { get; set; }

        public string StatusMatchingLdpGate { get; set; }
        public string MensagemMatchingLdpGate { get; set; }

        public long? LdpGateIdMatch { get; set; }
        public long? LdpRegistroIdMatch { get; set; }

        public DateTime? DataMatchingLdpGate { get; set; }

        /// <summary>PK real de dbo.Cronograma_Projetos (ID_Cronograma) — preenchida ao ler o snapshot já
        /// persistido (ver CronogramaTipo2LdpSyncRepository), nunca no momento da leitura do MPP.</summary>
        public int? IdCronograma { get; set; }

        /// <summary>Texto de UID_Tarefa já persistido (TaskUniqueId/TaskId/"" — ver
        /// CronogramaTipo2Repository.InserirLoteAsync) — lido de volta do snapshot para rastreabilidade em
        /// Ldp_Documento_Gates.Cronograma_UID_Tarefa. Nulo quando o registro ainda não veio do banco.</summary>
        public string UidTarefaPersistido { get; set; }

        // ---- Colunas já existentes em dbo.Cronograma_Projetos (uso do Tipo 1) ----
        // Todas nullable na tabela real. Nesta fase (4A), a única preenchida pelo Tipo 2 é EdtWbs (dado
        // estrutural do MPXJ, sem ambiguidade de mapeamento). As demais ficam NULL: o Tipo 1 as preenche
        // através de uma busca por aliases DIFERENTE (palavras-chave amplas de "previsto/realizado/
        // tendência", não os 7 aliases exatos do Tipo 2) — reaproveitar aquela lógica aqui seria inventar
        // um mapeamento não validado para este contexto (ver seção 18 do pedido: não usar ainda datas,
        // WBS, HH, custos).
        public string CrOs { get; set; }
        public string EdtWbs { get; set; }
        public decimal? PercentPhysicalCompleted { get; set; }
        public decimal? PercentPredictedPhysical { get; set; }
        public string WbsPercentText { get; set; }
        public DateTime? PlannedDateStart { get; set; }
        public DateTime? PlannedDateBaseline { get; set; }
        public DateTime? ReplannedDate { get; set; }
        public decimal? Trabalho { get; set; }
    }
}
