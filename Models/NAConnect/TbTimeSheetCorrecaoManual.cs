using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class TbTimeSheetCorrecaoManual
{
    public int Id { get; set; }

    public string? Afe { get; set; }

    public string? Discipline { get; set; }

    public string DocCode { get; set; } = null!;

    public string? DocDescription { get; set; }

    public string Name { get; set; } = null!;

    public decimal? Wh { get; set; }

    public string Activity { get; set; } = null!;

    public DateOnly DataLancamento { get; set; }

    public TimeOnly? HoraInicio { get; set; }

    public TimeOnly? HoraFim { get; set; }

    public bool Ativo { get; set; }

    public DateTime DataCadastro { get; set; }
}
