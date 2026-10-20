namespace APS.WebApp.Models
{
    /// <summary>
    /// Contadores de diagnóstico de UMA fase da importação (Custo Real, Custo Previsto ou Timesheet) —
    /// nunca inclui dado financeiro pessoal (remuneração/salário) em texto — só contagens e chaves
    /// técnicas, conforme seção 13 do pedido (dados financeiros pessoais são sensíveis).
    /// </summary>
    public sealed class GestaoVistaFaseResultado
    {
        public int LinhasLidas { get; set; }

        public int LinhasValidas { get; set; }

        public int LinhasInvalidasIgnoradas { get; set; }

        public int Inseridos { get; set; }

        /// <summary>Custo Real: novas versões SCD2 (fechou a anterior + inseriu nova). Custo Previsto/
        /// Timesheet: sempre 0 (não têm histórico versionado).</summary>
        public int NovasVersoes { get; set; }

        /// <summary>Linha já existia com o mesmo RowHash — nenhuma alteração feita (idempotência).</summary>
        public int IgnoradosIdempotentes { get; set; }
    }
}
