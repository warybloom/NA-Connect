using System;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Uma linha PRONTA para persistir em gestao.TimesheetSnapshotAtual — já com CustoCalculado
    /// resolvido (Etapa03TimesheetSnapshotService calcula, IGestaoVistaTimesheetSnapshotRepository só
    /// grava). Espelha 1:1 uma linha lida da aba TIMESHEET da LDP (via GestaoVistaTimesheetLancamentoExtraido)
    /// mais a identidade do projeto. Nunca inventa DataLancamento: fica null quando a fonte não tinha data.
    /// </summary>
    public sealed class GestaoVistaTimesheetSnapshotLinha
    {
        public string ProjectIdAcc { get; set; }
        public string NomeProfissional { get; set; }
        public decimal? HorasPlanejadas { get; set; }
        public decimal? HorasTrabalhadas { get; set; }
        public string Projeto { get; set; }
        public string Funcao { get; set; }
        public string Disciplina { get; set; }
        public string DocumentoCodigo { get; set; }
        public string Documento { get; set; }
        public string Descricao { get; set; }
        public string Doc1 { get; set; }
        public string Doc2 { get; set; }
        public string Doc3 { get; set; }
        public string Doc4 { get; set; }
        public string Doc5 { get; set; }
        public DateTime? DataLancamento { get; set; }
        public decimal? CustoCalculado { get; set; }
    }
}
