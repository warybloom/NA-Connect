using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>Resultado final da ETAPA 03 (snapshot do Timesheet para a Gestão à Vista/PMO) — o que
    /// GrdAutomationWorker usa para decidir o log [ETAPA 03][FIM].</summary>
    public sealed class GestaoVistaTimesheetSnapshotResultado
    {
        public bool Sucesso { get; set; }
        public string Motivo { get; set; }
        public List<string> Erros { get; set; } = new();

        public int LinhasExcel { get; set; }
        public decimal HHExcel { get; set; }

        public int LinhasBancoAntes { get; set; }
        public decimal HHBancoAntes { get; set; }

        public int Removidos { get; set; }
        public int Inseridos { get; set; }

        public int LinhasBancoDepois { get; set; }
        public decimal HHBancoDepois { get; set; }

        public decimal Diferenca { get; set; }
    }
}
