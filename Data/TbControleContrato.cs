using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Data;

public partial class TbControleContrato
{
    public int? Id { get; set; }

    public int? EntidadeCodigo { get; set; }

    public string? EntidadeNome { get; set; }

    public DateTime? CadastroData { get; set; }

    public int? EapIp { get; set; }

    public string? EapCentroResultadoCodigo { get; set; }

    public string? EapCentroResultadoDescricao { get; set; }

    public string? EapitemPlanoContaCodigo { get; set; }

    public string? EapitemPlanoContaDescricao { get; set; }

    public bool? PlanoContaAcessoRestrito { get; set; }

    public decimal? ValorCompromisso { get; set; }

    public decimal? ValorFaturar { get; set; }

    public decimal? ValorLiquidado { get; set; }

    public decimal? SaldoMovimentos { get; set; }

    public string? Origem { get; set; }
}
