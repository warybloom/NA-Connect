using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class MetadadosProjeto1
{
    public int MetadadoId { get; set; }

    public string ProjectIdAcc { get; set; } = null!;

    public string? NumeroNa { get; set; }

    public string? DescricaoCorporativa { get; set; }

    public DateTime? DataUltimaAtualizacao { get; set; }

    public string? LdpFileId { get; set; }

    public string? AccountIdAcc { get; set; }

    public string? NomeLdp { get; set; }

    public DateTime? DataUpdate { get; set; }

    public string? Idioma { get; set; }

    public string? PadraoNomenclatura { get; set; }

    public string? ColunaChave { get; set; }

    public virtual Projeto ProjectIdAccNavigation { get; set; } = null!;
}
