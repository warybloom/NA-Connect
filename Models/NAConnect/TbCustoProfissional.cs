using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class TbCustoProfissional
{
    public int Id { get; set; }

    public string ProjetoCodigo { get; set; } = null!;

    public string NomeProfissional { get; set; } = null!;

    public string? FuncaoCusto { get; set; }

    public string? DisciplinaCusto { get; set; }

    public decimal Remuneracao { get; set; }

    public decimal Encargos { get; set; }

    public decimal Softwares { get; set; }

    public decimal Computadores { get; set; }

    public decimal CustoTotalMensal { get; set; }

    public decimal CustoHora { get; set; }

    public bool Ativo { get; set; }

    public string? Origem { get; set; }

    public DateTime DataCadastro { get; set; }
}
