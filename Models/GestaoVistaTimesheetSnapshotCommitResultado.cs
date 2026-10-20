using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Resultado de UMA tentativa de substituir o snapshot atual do Timesheet de um projeto
    /// (IGestaoVistaTimesheetSnapshotRepository.SubstituirSnapshotAsync) — DELETE+INSERT transacional,
    /// nunca parcial: Sucesso=false significa que a transação foi revertida e o snapshot anterior
    /// (LinhasAntes/HHAntes) continua vigente, intacto.
    /// </summary>
    public sealed class GestaoVistaTimesheetSnapshotCommitResultado
    {
        public bool Sucesso { get; set; }
        public string Motivo { get; set; }
        public List<string> Erros { get; set; } = new();

        public int LinhasAntes { get; set; }
        public decimal HHAntes { get; set; }

        public int Removidos { get; set; }
        public int LinhasInseridas { get; set; }

        /// <summary>Só preenchido quando Sucesso=true (lido de volta do banco, após o commit).</summary>
        public int? LinhasDepois { get; set; }
        public decimal? HHDepois { get; set; }
        /// <summary>SUM(CustoCalculado) lido de volta do banco após o commit (só quando Sucesso=true).</summary>
        public decimal? CustoDepois { get; set; }

        public decimal? DiferencaExcelBanco { get; set; }
    }
}
