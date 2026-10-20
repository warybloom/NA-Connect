using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Resultado PURO (sem tocar banco) da leitura do workbook da LDP pelo extrator de Gestão à Vista —
    /// enumera todas as worksheets e extrai Timesheet/Custo Real/Custo profissionais em memória. Nunca
    /// falha o processo por uma aba opcional ausente/com layout inesperado (seção 6 do modelo de dados) —
    /// registra em Avisos; só Erros críticos (workbook ilegível) impedem a persistência subsequente.
    /// </summary>
    public sealed class GestaoVistaExtracaoResultado
    {
        public string ProjectIdAcc { get; set; }

        public string NomeArquivo { get; set; }

        public List<GestaoVistaWorksheetDiagnostico> Worksheets { get; set; } = new();

        public List<GestaoVistaCustoRealExtraido> CustoReal { get; set; } = new();

        public List<GestaoVistaCustoPrevistoExtraido> CustoPrevisto { get; set; } = new();

        public List<GestaoVistaTimesheetLancamentoExtraido> Timesheet { get; set; } = new();

        public List<string> Avisos { get; set; } = new();

        public List<string> Erros { get; set; } = new();
    }
}
