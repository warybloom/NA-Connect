using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Data;

public partial class TbParcelasAfaturar
{
    public int? Id { get; set; }

    public int? EntidadeCodigo { get; set; }

    public string? EntidadeNome { get; set; }

    public int? EapId { get; set; }

    public string? CentroResultado { get; set; }

    public string? Observacao { get; set; }

    public decimal? Valor { get; set; }

    public string? Periodo { get; set; }

    public int? Empresa { get; set; }

    public string? RazaoSocial { get; set; }

    public string? Responsavel { get; set; }
}
