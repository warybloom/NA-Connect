using System;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Espelha uma linha de dbo.Ldp_Documento_Gates (tabela já criada manualmente, fora deste código).
    /// Representa o estado de UM gate (30%, 60% ou 90%) de UM documento da LDP Tipo 2, numa
    /// sincronização específica. A ordem final das colunas de origem na planilha Tipo 2 ainda será
    /// informada — este modelo só descreve o destino no SQL.
    /// </summary>
    public sealed class LdpTipo2Gate
    {
        public long? Id { get; set; }

        /// <summary>
        /// Referência obrigatória à linha principal da LDP (Ldp_Registro_Id). Nenhum gate pode ser
        /// persistido sem esse valor — preenchido pela Fase 3 (LdpTipo2PersistenceService) com o
        /// ID_Registro real devolvido por LdpTipo2Repository.InserirDocumentoPrincipalAsync, sempre
        /// depois do INSERT bem-sucedido da linha principal em dbo.Ldp_Unificada.
        /// </summary>
        public long? LdpRegistroId { get; set; }

        public string ProjectIdAcc { get; set; }

        public string NumeroDocumentoNa { get; set; }
        public string NumeroDocumentoCliente { get; set; }

        public byte GatePercentual { get; set; }

        public bool GateAtivo { get; set; }

        public string Fase { get; set; }

        public string DocumentStatus { get; set; }

        public string Situation { get; set; }

        public DateTime? PlannedBaseline { get; set; }

        public DateTime? ReplannedDate { get; set; }

        public decimal? PlannedPhysical { get; set; }

        public decimal? PhysicalCompleted { get; set; }

        public DateTime DataSincronizacao { get; set; }

        /// <summary>
        /// Único conjunto de percentuais de gate aceito nesta fase. Nunca usar números mágicos
        /// espalhados no código — sempre validar através deste método antes de gravar ou aceitar um
        /// LdpTipo2Gate.
        /// </summary>
        public static bool GateValido(byte percentual)
        {
            return percentual == 30
                || percentual == 60
                || percentual == 90;
        }
    }
}
