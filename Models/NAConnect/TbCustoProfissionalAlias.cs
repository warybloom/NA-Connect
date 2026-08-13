using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class TbCustoProfissionalAlias
{
    public int Id { get; set; }

    public string ProjetoCodigo { get; set; } = null!;

    public string NomeAlias { get; set; } = null!;

    public string NomeProfissional { get; set; } = null!;

    public bool Ativo { get; set; }

    public string? Observacao { get; set; }

    public DateTime DataCadastro { get; set; }
}
