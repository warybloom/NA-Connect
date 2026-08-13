using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class VwLdpHhPlanejadoVsRealizado
{
    public DateOnly? DataDate { get; set; }

    public DateOnly? InicioDaSemana { get; set; }

    public DateOnly? FimDaSemana { get; set; }

    public DateOnly? InicioDoMes { get; set; }

    public DateOnly? FimDoMes { get; set; }

    public string PeriodoSemanalLabel { get; set; } = null!;

    public string? Doc1 { get; set; }

    public string? Doc2 { get; set; }

    public string? Doc3 { get; set; }

    public string? Doc4 { get; set; }

    public string? Doc5 { get; set; }

    public string? NaDocumentNumber { get; set; }

    public decimal? HhPrevistoDiario { get; set; }

    public decimal HhRealizadoDiario { get; set; }
}
