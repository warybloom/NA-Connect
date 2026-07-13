using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Data;

public partial class TbFluxoCaixaReal
{
    public DateTime? DataInicioDiario { get; set; }

    public DateTime? DataInicioMensal { get; set; }

    public DateTime? DataInicioSemanal { get; set; }

    public short? TipoApresentacao { get; set; }

    public string? Apresentacao { get; set; }

    public string? Data { get; set; }

    public string? Cr { get; set; }

    public int? Empresa { get; set; }

    public decimal? ValorCredito { get; set; }

    public decimal? ValorDebito { get; set; }

    public DateTime? DataCalc { get; set; }
}
