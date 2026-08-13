using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class VwCustoRealizadoSemanalDocumentoPessoaBml011013
{
    public DateOnly? InicioDaSemana { get; set; }

    public DateOnly? FimDaSemana { get; set; }

    public string PeriodoSemanalLabel { get; set; } = null!;

    public string? Projeto { get; set; }

    public string? Nome { get; set; }

    public string? NomeProfissionalCusto { get; set; }

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

    public string ChaveSemanalDocPessoa { get; set; } = null!;

    public decimal? HhRealizadoSemanalPessoa { get; set; }

    public decimal? CustoRealizadoSemanalPessoa { get; set; }

    public bool? SemCadastroCusto { get; set; }
}
