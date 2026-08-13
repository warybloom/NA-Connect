using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class TbOrcamentoDisciplina
{
    public string ProjectIdAcc { get; set; } = null!;

    public string Discipline { get; set; } = null!;

    public decimal? TotalHhOrcado { get; set; }
}
