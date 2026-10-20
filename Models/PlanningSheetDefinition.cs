using System.Collections.Generic;
using System.Linq;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Configuração explícita de um processo de Planning Control (Etapas 12/13/14/16 — seção 6 do
    /// pedido). Uma instância descreve tudo que é específico do processo (pasta, aba aceita, tabela de
    /// destino, campos); a descoberta de cabeçalho/colunas em si (PlanningHeaderResolver) é genérica e
    /// compartilhada entre todos os processos.
    /// </summary>
    public sealed class PlanningSheetDefinition
    {
        public string ProcessName { get; set; }
        public string FolderName { get; set; }

        /// <summary>Referência histórica para exibição; descoberta usa somente conteúdo, nunca esta lista.</summary>
        public string[] AcceptedWorksheetNames { get; set; }

        public string DestinationTable { get; set; }

        public int HeaderSearchStartRow { get; set; } = 1;
        public int HeaderSearchEndRow { get; set; } = 10;

        public List<PlanningColumnDefinition> Columns { get; set; } = new List<PlanningColumnDefinition>();

        /// <summary>
        /// Campos exigidos na linha de cabeçalho para ela ser candidata (seção 4/7) — RequiredValue permite separar presença de coluna de preenchimento obrigatório.
        /// </summary>
        public string[] CamposMinimosCabecalho => Columns.Where(c => c.Required).Select(c => c.LogicalName).ToArray();

        public string[] CamposObrigatoriosNaLinha => Columns.Where(c => c.RequiredValue ?? c.Required).Select(c => c.LogicalName).ToArray();

        /// <summary>
        /// Campos identificadores de linha real (seção 11) — ex.: ID/DESCRIPTION/STATUS para ACTION ITEM
        /// LIST, ITEM/Nº DO DESVIO para Scope, ITEM/Nº HOLD/Nº N&amp;A DO DOCUMENTO para Hold.
        /// </summary>
        public string[] CamposIdentificadoresLinha => Columns.Where(c => c.IsRowIdentifier).Select(c => c.LogicalName).ToArray();

        public string[] CamposData => Columns.Where(c => c.DataType == PlanningColumnDataType.Data).Select(c => c.LogicalName).ToArray();

        public string[] CamposInteiros => Columns.Where(c => c.DataType == PlanningColumnDataType.Inteiro).Select(c => c.LogicalName).ToArray();
    }
}
