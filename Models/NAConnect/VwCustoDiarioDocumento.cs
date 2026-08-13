using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class VwCustoDiarioDocumento
{
    public DateOnly? DataDate { get; set; }

    public DateOnly? InicioDaSemana { get; set; }

    public DateOnly? FimDaSemana { get; set; }

    public DateOnly? InicioDoMes { get; set; }

    public DateOnly? FimDoMes { get; set; }

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

    public string ChaveDiariaDoc { get; set; } = null!;

    public decimal? HhPrevistoDiario { get; set; }

    public decimal? HhRealizadoDiario { get; set; }

    public decimal? CustoPrevistoDiario { get; set; }

    public decimal? CustoRealizadoDiario { get; set; }

    public string? OrigemDataPrevisto { get; set; }
}
