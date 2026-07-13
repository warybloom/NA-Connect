using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Data;

public partial class TbHorasPlanejadasDocumentoT
{
    public string? Empresa { get; set; }

    public string? Projeto { get; set; }

    public int? CodDisciplina { get; set; }

    public string? Disciplina { get; set; }

    public string? Documento { get; set; }

    public string? DocumentoDescricao { get; set; }

    public decimal? Planejada { get; set; }
}
