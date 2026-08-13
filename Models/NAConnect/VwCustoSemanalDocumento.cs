using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class VwCustoSemanalDocumento
{
    public DateOnly? InicioDaSemana { get; set; }

    public DateOnly? FimDaSemana { get; set; }

    public string PeriodoSemanalLabel { get; set; } = null!;

    public string? ProjectIdAcc { get; set; }

    public string? NaDocumentNumber { get; set; }

    public string? DocumentTitle { get; set; }

    public string? Discipline { get; set; }

    public string? Doc1 { get; set; }

    public string? Doc2 { get; set; }

    public string? Doc3 { get; set; }

    public string? Doc4 { get; set; }

    public string? Doc5 { get; set; }

    public string ChaveSemanalDoc { get; set; } = null!;

    public decimal? HhPrevistoSemanal { get; set; }

    public decimal? HhRealizadoSemanal { get; set; }

    public decimal? CustoPrevistoSemanal { get; set; }

    public decimal? CustoRealizadoSemanal { get; set; }
}
