using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Data;

public partial class TbComercial
{
    public string? Tipo { get; set; }

    public string? Empresa { get; set; }

    public string? Gestor { get; set; }

    public int? Status { get; set; }

    public int? Prioridade { get; set; }

    public string? SegmentoNegocio { get; set; }

    public string? SegmentoMercado { get; set; }

    public string? Cliente { get; set; }

    public string? Empreendimento { get; set; }

    public decimal? Investimento { get; set; }

    public string? Pais { get; set; }

    public string? Estado { get; set; }

    public DateTime? Inicio { get; set; }

    public int? PrazoEstimado { get; set; }

    public string? Escopo { get; set; }
}
