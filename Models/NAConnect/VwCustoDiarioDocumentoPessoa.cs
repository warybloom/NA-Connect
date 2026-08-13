using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class VwCustoDiarioDocumentoPessoa
{
    public DateOnly? DataDate { get; set; }

    public DateOnly? InicioDaSemana { get; set; }

    public DateOnly? FimDaSemana { get; set; }

    public DateOnly? InicioDoMes { get; set; }

    public DateOnly? FimDoMes { get; set; }

    public string? PeriodoSemanalLabel { get; set; }

    public string? Nome { get; set; }

    public string? Projeto { get; set; }

    public string? Funcao { get; set; }

    public string? Disciplina { get; set; }

    public string? DocumentoCodigo { get; set; }

    public string? Documento { get; set; }

    public string? Descricao { get; set; }

    public string? NaDocumentNumber { get; set; }

    public string? Doc1 { get; set; }

    public string? Doc2 { get; set; }

    public string? Doc3 { get; set; }

    public string? Doc4 { get; set; }

    public string? Doc5 { get; set; }

    public string ChaveDiariaDoc { get; set; } = null!;

    public string ChaveDiariaDocPessoa { get; set; } = null!;

    public decimal? HhRealizadoDiarioPessoa { get; set; }

    public decimal? HhPlanejadoTimeSheetPessoa { get; set; }

    public decimal? HhPrevistoDiario { get; set; }

    public decimal? HhRealizadoDiario { get; set; }

    public decimal? CustoPrevistoDiario { get; set; }

    public decimal? CustoRealizadoDiario { get; set; }

    public string? OrigemDataPrevisto { get; set; }

    public decimal? CustoRealizadoDiarioPessoa { get; set; }

    public decimal? CustoPrevistoDiarioPessoa { get; set; }
}
