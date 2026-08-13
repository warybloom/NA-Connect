using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class VwHhDistribuidoNoTempo
{
    public string? Projeto { get; set; }

    public string? NomeProjeto { get; set; }

    public string? Disciplina { get; set; }

    public string? DocumentoCodigo { get; set; }

    public string? Documento { get; set; }

    public string? Doc1 { get; set; }

    public string? Doc2 { get; set; }

    public string? Doc3 { get; set; }

    public string? Doc4 { get; set; }

    public string? Doc5 { get; set; }

    public DateTime? DataApontamento { get; set; }

    public DateTime? DataPlanejadaBaseline { get; set; }

    public string DescricoesAgrupadas { get; set; } = null!;

    public decimal HhPrevistoTotalEstatico { get; set; }

    public decimal? HhPrevistoProporcionalDia { get; set; }

    public decimal HhRealizadoDia { get; set; }
}
