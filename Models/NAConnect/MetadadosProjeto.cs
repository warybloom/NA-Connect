using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class MetadadosProjeto
{
    public int MetadataId { get; set; }

    public int? ProjetoId { get; set; }

    public string? NumeroNaPt { get; set; }

    public string? NumeroNaEn { get; set; }

    public string? HubProjetoId { get; set; }

    public virtual Projeto? Projeto { get; set; }
}
