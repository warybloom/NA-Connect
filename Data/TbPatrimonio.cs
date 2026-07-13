using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Data;

public partial class TbPatrimonio
{
    public int Id { get; set; }

    public string? Descricao { get; set; }

    public int? Etiqueta { get; set; }

    public string? NotaFiscal { get; set; }

    public DateTime? Data { get; set; }

    public string? Tipo { get; set; }

    public decimal? Valor { get; set; }

    public string? Crreferencia { get; set; }

    public string? Responsavel { get; set; }

    public string? Alugado { get; set; }

    public string? Faixa { get; set; }

    public string? Disponivel { get; set; }
}
