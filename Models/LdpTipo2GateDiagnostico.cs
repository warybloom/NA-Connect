using System;

namespace APS.WebApp.Models
{
    /// <summary>
    /// DTO somente-leitura para diagnóstico de UM gate de dbo.Ldp_Documento_Gates — inclui, além dos
    /// campos já espelhados por LdpTipo2Gate, as colunas de rastreamento de sincronização com o
    /// cronograma (gravadas via SQL cru por CronogramaTipo2LdpSyncRepository.AtualizarGateAsync, mas
    /// nunca lidas de volta por LdpTipo2GateRepository.ObterPorProjetoESincronizacaoAsync). Deliberadamente
    /// SEPARADO de LdpTipo2Gate (o model usado no INSERT principal) para não arriscar um campo de
    /// rastreamento vazar para o INSERT por engano — este DTO nunca é usado para persistir.
    /// </summary>
    public sealed class LdpTipo2GateDiagnostico
    {
        public long Id { get; set; }
        public long LdpRegistroId { get; set; }
        public string ProjectIdAcc { get; set; }
        public string NumeroDocumentoNa { get; set; }
        public string NumeroDocumentoCliente { get; set; }
        public byte GatePercentual { get; set; }
        public bool GateAtivo { get; set; }
        public decimal? PlannedPhysical { get; set; }
        public decimal? PhysicalCompleted { get; set; }
        public DateTime? PlannedBaseline { get; set; }
        public DateTime? ReplannedDate { get; set; }
        public DateTime DataSincronizacao { get; set; }

        // ---- Rastreamento de sincronização com o Cronograma Tipo 2 (Mensagem_Sincronizacao_Cronograma
        // carrega o prefixo [MATCH_NA]/[MATCH_CLIENT] — ver CronogramaTipo2LdpSyncService) ----
        public string CronogramaUidTarefa { get; set; }
        public DateTime? CronogramaDataSincronizacao { get; set; }
        public DateTime? DataAtualizacaoCronograma { get; set; }
        public string StatusSincronizacaoCronograma { get; set; }
        public string MensagemSincronizacaoCronograma { get; set; }
    }
}
