using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class Projeto
{
    public int ProjetoId { get; set; }

    public int? HubContaId { get; set; }

    public string? NomeProjeto { get; set; }

    public string ProjectIdAcc { get; set; } = null!;

    public bool IsAtivo { get; set; }

    public string? FileIdLdpAcc { get; set; }

    public string? NamingStandard { get; set; }

    public string? ProjectLanguage { get; set; }

    public virtual HubsContum? HubConta { get; set; }

    public virtual ICollection<MetadadosProjeto1> MetadadosProjeto1s { get; set; } = new List<MetadadosProjeto1>();

    public virtual ICollection<MetadadosProjeto> MetadadosProjetos { get; set; } = new List<MetadadosProjeto>();
}
