using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class VwResumoHhLdpCompletum
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

    public string DescricoesAgrupadas { get; set; } = null!;

    public decimal HhPrevisto { get; set; }

    public decimal HhRealizadoTotal { get; set; }
}
