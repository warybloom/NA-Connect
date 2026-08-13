using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class HubsContum
{
    public int HubContaId { get; set; }

    public string? NomeHub { get; set; }

    public string? AccountIdAcc { get; set; }

    public virtual ICollection<Projeto> Projetos { get; set; } = new List<Projeto>();
}
