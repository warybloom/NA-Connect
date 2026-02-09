using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Data;

public partial class TbContasApagar
{
    public int? Codigo { get; set; }

    public int? Titulo { get; set; }

    public DateTime? Vencimento { get; set; }

    public DateTime? Competencia { get; set; }

    public int? EapId { get; set; }

    public string? CentroResultado { get; set; }

    public string? CentroResultadoDescricao { get; set; }

    public string? EntidadeNome { get; set; }

    public string? Identificador { get; set; }

    public string? Observacao { get; set; }

    public short? Tipo { get; set; }

    public int? Contrato { get; set; }

    public DateTime? Previsao { get; set; }

    public short? TipoDocumento { get; set; }

    public string? DocumentoCodigo { get; set; }

    public int? Numero { get; set; }

    public decimal? ValorTitulo { get; set; }

    public decimal? Valor { get; set; }

    public decimal? ValorLiquido { get; set; }

    public int? Pedido { get; set; }

    public string? Parcela { get; set; }

    public string? ResgatarPrevisao { get; set; }

    public string? FormaPagamento { get; set; }

    public string? Banco { get; set; }

    public string? Agencia { get; set; }

    public string? ContaCorrete { get; set; }

    public string? GestorNome { get; set; }

    public bool? Aprovado { get; set; }

    public bool? Supervisado { get; set; }
}
