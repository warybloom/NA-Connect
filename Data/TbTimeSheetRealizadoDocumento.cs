using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Data;

public partial class TbTimeSheetRealizadoDocumento
{
    public DateTime? Data { get; set; } // D Maiúsculo
    public string? Nome { get; set; }
    public decimal? Planejadas { get; set; }
    public decimal? Trabalhadas { get; set; }
    public string? Projeto { get; set; }
    public string? Funcao { get; set; }
    public string? Disciplina { get; set; }
    public string? DocumentoCodigo { get; set; }
    public string? Documento { get; set; }
    public string? Descricao { get; set; }
    public string? Doc1 { get; set; } // Alterado para D maiúsculo
    public string? Doc2 { get; set; } // Alterado para D maiúsculo
    public string? Doc3 { get; set; }
    public string? Doc4 { get; set; }
    public string? Doc5 { get; set; }
}
