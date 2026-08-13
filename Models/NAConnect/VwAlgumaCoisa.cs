using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class VwAlgumaCoisa
{
    public int MetadadoId { get; set; }

    public string ProjectIdAcc { get; set; } = null!;

    public string? NumeroNa { get; set; }

    public string? DescricaoCorporativa { get; set; }
}
