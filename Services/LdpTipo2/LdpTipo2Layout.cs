using System.Collections.Generic;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Layout de UM gate (30%, 60% ou 90%), com todas as colunas já resolvidas dinamicamente a partir
    /// da linha 7 — nunca posições fixas. Resolvido uma única vez por arquivo (ver
    /// LdpTipo2HeaderResolver.ResolverLayout); as linhas de documento apenas leem essas colunas.
    /// </summary>
    public sealed class LdpTipo2GateLayout
    {
        public byte Percentual { get; set; }

        public int ColunaPlannedPhysical { get; set; }
        public int ColunaPhysicalCompleted { get; set; }

        public int ColunaFaseManual { get; set; }
        public int ColunaDocumentStatus { get; set; }
        public int ColunaSituation { get; set; }
        public int ColunaPlannedBaseline { get; set; }
        public int ColunaReplannedDate { get; set; }

        public int ColunaIndicadorAplicabilidade { get; set; }
    }

    /// <summary>
    /// Layout completo da aba LDP (Tipo 2) resolvido a partir da linha 7 — inclui as colunas comuns e
    /// os três layouts de gate. Uma única resolução por arquivo (ver LdpTipo2HeaderResolver).
    /// </summary>
    public sealed class LdpTipo2Layout
    {
        public int LinhaCabecalho { get; set; }
        public int PrimeiraLinhaDocumento { get; set; }

        public IReadOnlyDictionary<string, int> ColunasComuns { get; set; }

        public LdpTipo2GateLayout Gate30 { get; set; }
        public LdpTipo2GateLayout Gate60 { get; set; }
        public LdpTipo2GateLayout Gate90 { get; set; }
    }
}
