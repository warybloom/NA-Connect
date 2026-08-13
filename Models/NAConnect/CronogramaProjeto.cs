using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class CronogramaProjeto
{
    public int IdCronograma { get; set; }

    public string ProjectIdAcc { get; set; } = null!;

    public string UidTarefa { get; set; } = null!;

    public string? CrOs { get; set; }

    public string? EdtWbs { get; set; }

    public string? NaDocumentNumber { get; set; }

    public string? TaskName { get; set; }

    public decimal? PercentPhysicalCompleted { get; set; }

    public decimal? PercentPredictedPhysical { get; set; }

    public string? WbsPercent { get; set; }

    public DateTime? PlannedDateStart { get; set; }

    public DateTime? PlannedDataBaseline { get; set; }

    public DateTime? ReplannedDate { get; set; }

    public DateTime? DataSincronizacao { get; set; }
}
