using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Data;

public partial class TbContasAnalitica
{
    public int? EmpresaId { get; set; }

    public string? EapitemPlanoContaCodigo { get; set; }

    public string? EapitemPlanoContaDescricao { get; set; }

    public short? EapitemPlanoContaNatureza { get; set; }

    public bool? Imposto { get; set; }

    public string? ClassificacaoImposto { get; set; }

    public string? EntidadeNome { get; set; }

    public int? ContratoId { get; set; }

    public string? EapCentroResultadoCodigo { get; set; }

    public string? EapCentroResultadoDescricao { get; set; }

    public short? EapCentroResultadoNatureza { get; set; }

    public string? Observacao { get; set; }

    public DateTime? Competencia { get; set; }

    public DateTime? DataRealizacao { get; set; }

    public decimal? ValorRealizado { get; set; }

    public string? DescricaoManual { get; set; }

    public bool? AcessoRestrito { get; set; }

    public int? SegmentoNegocioId { get; set; }

    public string? SegmentoNegocioDescricao { get; set; }
}
