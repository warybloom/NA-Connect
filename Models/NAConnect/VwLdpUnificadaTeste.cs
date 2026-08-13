using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class VwLdpUnificadaTeste
{
    public int IdRegistro { get; set; }

    public string? ProjectIdAcc { get; set; }

    public string? Item { get; set; }

    public string? ScheduleId { get; set; }

    public string? Discipline { get; set; }

    public string? NaDocumentNumber { get; set; }

    public string? ClientDocumentNumber { get; set; }

    public string? DocumentTitle { get; set; }

    public string? Format { get; set; }

    public int? NoPages { get; set; }

    public DateTime? PlannedDateBaseline { get; set; }

    public DateTime? ReplannedDate { get; set; }

    public DateTime? EmissionDate { get; set; }

    public string? TransmittalNumber { get; set; }

    public string? IssuedPurpose { get; set; }

    public string? Revision { get; set; }

    public string? DocumentStatus { get; set; }

    public decimal? WbsPercent { get; set; }

    public string? StatusInFlow { get; set; }

    public string? Situation { get; set; }

    public DateTime? DateCommentsReceived { get; set; }

    public string? CommentStatus { get; set; }

    public decimal? PercentPlannedPhysical { get; set; }

    public decimal? PercentPhysicalCompleted { get; set; }

    public DateTime? InclusionDate { get; set; }

    public string? Cr { get; set; }

    public string? CrOs { get; set; }

    public string? Area { get; set; }

    public string? DisciplineActivity { get; set; }

    public string? DocTypeCode { get; set; }

    public string? Seq { get; set; }

    public decimal? PlannedHh { get; set; }

    public decimal? ActualHh { get; set; }

    public decimal? RemainingHh { get; set; }

    public decimal? PlannedCost { get; set; }

    public decimal? ActualCost { get; set; }

    public decimal? RemainingCost { get; set; }

    public DateTime? DataSincronizacao { get; set; }

    public decimal HhTimeSheet { get; set; }
}
