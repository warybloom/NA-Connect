using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class VwCustoRealizadoSemanalDocumentoBml011013
{
    public DateOnly? InicioDaSemana { get; set; }

    public DateOnly? FimDaSemana { get; set; }

    public string PeriodoSemanalLabel { get; set; } = null!;

    public string? Projeto { get; set; }

    public string? DocumentoCodigo { get; set; }

    public string? Documento { get; set; }

    public string? Descricao { get; set; }

    public string? Doc1 { get; set; }

    public string? Doc2 { get; set; }

    public string? Doc3 { get; set; }

    public string? Doc4 { get; set; }

    public string? Doc5 { get; set; }

    public string ChaveSemanalDoc { get; set; } = null!;

    public decimal? HhRealizadoSemanal { get; set; }

    public decimal? CustoRealizadoSemanal { get; set; }

    public bool? TemPessoaSemCadastroCusto { get; set; }
}
