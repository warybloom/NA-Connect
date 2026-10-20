using System;
using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Documento da LDP Tipo 2, lido de uma linha da planilha (aba "LDP", cabeçalho na linha 7 —
    /// ver Services/LdpTipo2/LdpTipo2HeaderResolver). Modelo próprio do Tipo 2: não reaproveita nem
    /// é reaproveitado pelo modelo da LDP Tipo 1.
    /// </summary>
    public sealed class LdpTipo2Documento
    {
        public int LinhaExcel { get; set; }

        public string Item { get; set; }
        public string ScheduleId { get; set; }
        public string Disciplina { get; set; }

        public string NumeroDocumentoNa { get; set; }
        public string NumeroDocumentoCliente { get; set; }

        public string TituloDocumento { get; set; }

        public string Formato { get; set; }
        public int? NumeroPaginas { get; set; }

        public DateTime? PlannedBaseline { get; set; }
        public DateTime? ReplannedDate { get; set; }
        public DateTime? EmissionDate { get; set; }

        public string TransmittalNumber { get; set; }
        public string IssuedPurpose { get; set; }
        public string Revision { get; set; }
        public string DocumentStatus { get; set; }

        public decimal? WbsPercent { get; set; }

        public string StatusInFlow { get; set; }
        public string Situation { get; set; }

        public DateTime? DateCommentsReceived { get; set; }
        public string CommentStatus { get; set; }

        public DateTime? InclusionDate { get; set; }

        public string Cr { get; set; }
        public string CrOs { get; set; }
        public string Area { get; set; }
        public string DisciplineActivity { get; set; }
        public string DocTypeCode { get; set; }
        public string Seq { get; set; }

        public decimal? PlannedHh { get; set; }
        public decimal? ActualHh { get; set; }
        public decimal? RemainingHh { get; set; }

        public decimal? PlannedCost { get; set; }
        public decimal? ActualCost { get; set; }
        public decimal? RemainingCost { get; set; }

        public List<LdpTipo2Gate> Gates { get; set; } = new List<LdpTipo2Gate>();
    }
}
