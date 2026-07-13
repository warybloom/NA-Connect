using System;
using System.Collections.Generic;

namespace AccIntegration.WebApp.Data;

public partial class TbTimeSheet
{
    public DateTime? Data { get; set; }

    public string? Nome { get; set; }

    public string? SemanaDesk { get; set; }

    public DateTime? Entrada { get; set; }

    public DateTime? Saida { get; set; }

    public decimal? Intervalo { get; set; }

    public decimal? Trabalhadas { get; set; }

    public string? Afe { get; set; }

    public string? Descricao { get; set; }

    public string? Projeto { get; set; }

    public string? Funcao { get; set; }

    public string? Disciplina { get; set; }
}
