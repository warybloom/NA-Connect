using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Data;

public partial class TbControleCusto
{
    public int? Id { get; set; }

    public int? PlanoContaId { get; set; }

    public string? PlanoContaCodigo { get; set; }

    public string? PlanoContaDescricao { get; set; }

    public short? PlanoContaNatureza { get; set; }

    public short? PlanoContaAnalitico { get; set; }

    public short? PlanoContaNivel { get; set; }

    public short? NivelPlanoContaOrdem { get; set; }

    public decimal? ValorOrcado { get; set; }

    public decimal? ValorPlanejado { get; set; }

    public decimal? PlanejadoData { get; set; }

    public decimal? ValorCompromissado { get; set; }

    public decimal? ValorRealizado { get; set; }

    public decimal? ValorIncorrido { get; set; }

    public bool? Inativo { get; set; }

    public string? CentroResultado { get; set; }
}
