using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class VwNAConnectWeeklyControl
{
    public int IdRegistro { get; set; }

    public string? ProjectIdAcc { get; set; }

    public string? NaDocumentNumber { get; set; }

    public string? DocumentTitle { get; set; }

    public DateTime? PlannedDateBaseline { get; set; }

    public decimal? WbsPercent { get; set; }

    public decimal? PercentPlannedPhysical { get; set; }

    public decimal? PercentPhysicalCompleted { get; set; }

    public string? CrOs { get; set; }

    public string? Area { get; set; }

    public string? DisciplineActivity { get; set; }

    public DateOnly? DataInicioSemana { get; set; }

    public DateOnly? DataFimSemana { get; set; }

    public string PeriodoSemanalLabel { get; set; } = null!;

    public DateTime? DataSincronizacao { get; set; }
}
