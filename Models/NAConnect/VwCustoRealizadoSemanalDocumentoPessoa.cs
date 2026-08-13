using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class VwCustoRealizadoSemanalDocumentoPessoa
{
    public DateOnly? InicioDaSemana { get; set; }

    public DateOnly? FimDaSemana { get; set; }

    public DateOnly? InicioDoMes { get; set; }

    public DateOnly? FimDoMes { get; set; }

    public string? PeriodoSemanalLabel { get; set; }

    public string? ProjectIdAcc { get; set; }

    public DateTime? DataSincronizacao { get; set; }

    public string? ProjetoCodigoCusto { get; set; }

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

    public string ChaveSemanalDoc { get; set; } = null!;

    public string ChaveSemanalDocPessoa { get; set; } = null!;

    public decimal? HhRealizadoSemanalPessoa { get; set; }

    public decimal? CustoRealizadoSemanalPessoa { get; set; }

    public bool? SemCadastroCusto { get; set; }
}
