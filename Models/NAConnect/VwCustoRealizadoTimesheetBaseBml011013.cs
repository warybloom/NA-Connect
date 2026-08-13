using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class VwCustoRealizadoTimesheetBaseBml011013
{
    public DateOnly? DataDate { get; set; }

    public DateOnly? InicioDaSemana { get; set; }

    public DateOnly? FimDaSemana { get; set; }

    public DateOnly? InicioDoMes { get; set; }

    public DateOnly? FimDoMes { get; set; }

    public string PeriodoSemanalLabel { get; set; } = null!;

    public string? Nome { get; set; }

    public string? NomeProfissionalMapeado { get; set; }

    public string? Projeto { get; set; }

    public string? Funcao { get; set; }

    public string? Disciplina { get; set; }

    public string? DocumentoCodigo { get; set; }

    public string? Documento { get; set; }

    public string? Descricao { get; set; }

    public string? Doc1 { get; set; }

    public string? Doc2 { get; set; }

    public string? Doc3 { get; set; }

    public string? Doc4 { get; set; }

    public string? Doc5 { get; set; }

    public string ChaveDiariaDoc { get; set; } = null!;

    public string ChaveDiariaDocPessoa { get; set; } = null!;

    public decimal? HhRealizadoDiarioPessoa { get; set; }

    public decimal? CustoHora { get; set; }

    public decimal? CustoRealizadoDiarioPessoa { get; set; }

    public bool? SemCadastroCusto { get; set; }
}
