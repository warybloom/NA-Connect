using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Data;

public partial class TbHorasPlanejadasT
{
    public string? Empresa { get; set; }

    public string? Projeto { get; set; }

    public int? CodDisciplina { get; set; }

    public string? Disciplina { get; set; }

    public decimal? Planejada { get; set; }
}
