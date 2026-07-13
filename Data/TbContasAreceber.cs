using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Data;

public partial class TbContasAreceber
{
    public int? Id { get; set; }

    public int? Codigo { get; set; }

    public short? Tipo { get; set; }

    public DateTime? Previsao { get; set; }

    public DateTime? Vencimento { get; set; }

    public DateTime? Competencia { get; set; }

    public DateTime? Emissao { get; set; }

    public int? EapId { get; set; }

    public string? EapCentroResultadoCodigo { get; set; }

    public string? EapCentroResultadoDescricao { get; set; }

    public string? Observacao { get; set; }

    public int? ContratoId { get; set; }

    public string? TipoDocumentoCodigo { get; set; }

    public string? TipoDocumentoDescricao { get; set; }

    public decimal? Valor { get; set; }

    public decimal? ValorLiquido { get; set; }

    public int? EntidadeId { get; set; }

    public int? EntidadeCodigo { get; set; }

    public string? EntidadeNome { get; set; }

    public string? Parcela { get; set; }

    public short? Vencido { get; set; }

    public string? NotaFiscal { get; set; }

    public string? ResgatarPrevisao { get; set; }

    public int? EmpresaId { get; set; }

    public string? EmpresaNome { get; set; }

    public string? MoedaSigla { get; set; }

    public int? PedidoId { get; set; }

    public string? ObservacaoParcela { get; set; }

    public string? GroupBy { get; set; }

    public decimal? TotalBrutoEmpresa { get; set; }

    public decimal? TotalLiquidoEmpresa { get; set; }

    public DateTime? DataEmissao { get; set; }
}
