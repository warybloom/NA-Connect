using System.Collections.Generic;
using APS.WebApp.Models;

namespace APS.WebApp.Services.Planning
{
    /// <summary>Schemas oficiais de CAP, Desvio e HOLD. CAPs idênticos são roteados pelo contexto da pasta/processo. Tipos validam o Excel; destinos legados permanecem texto.</summary>
    public static class PlanningDefinicoes
    {
        private static List<PlanningColumnDefinition> ColunasActionItemList() => new List<PlanningColumnDefinition>
        {
            new PlanningColumnDefinition { LogicalName = "Id", SqlColumnName = "ID", Required = true, IsRowIdentifier = true,
                AcceptedHeaders = new[] { "ID" } },
            new PlanningColumnDefinition { LogicalName = "Date", StoreAsText = true, DataType = PlanningColumnDataType.Data, SqlColumnName = "Date", Required = true,
                AcceptedHeaders = new[] { "DATE", "DATA" } },
            new PlanningColumnDefinition { LogicalName = "Name", SqlColumnName = "Name",
                AcceptedHeaders = new[] { "NAME", "NOME" } },
            new PlanningColumnDefinition { LogicalName = "Company", SqlColumnName = "Company",
                AcceptedHeaders = new[] { "COMPANY", "EMPRESA" } },
            new PlanningColumnDefinition { LogicalName = "Cycle", SqlColumnName = "CYCLE",
                AcceptedHeaders = new[] { "CYCLE" } },
            new PlanningColumnDefinition { LogicalName = "FrontOfServices", SqlColumnName = "FRONT_OF_SERVICES",
                AcceptedHeaders = new[] { "FRONT OF SERVICES" } },
            new PlanningColumnDefinition { LogicalName = "Afe", SqlColumnName = "AFE",
                AcceptedHeaders = new[] { "AFE" } },
            new PlanningColumnDefinition { LogicalName = "Description", SqlColumnName = "Description", Required = true, IsRowIdentifier = true,
                AcceptedHeaders = new[] { "DESCRIPTION", "DESCRIÇÃO", "DESCRICAO" } },
            // Name2 e Company3 são headers distintos comprovados no template oficial.
            new PlanningColumnDefinition { LogicalName = "Name2", SqlColumnName = "Name2",
                AcceptedHeaders = new[] { "Name2" } },
            new PlanningColumnDefinition { LogicalName = "Company3", SqlColumnName = "Company3",
                AcceptedHeaders = new[] { "Company3" } },
            new PlanningColumnDefinition { LogicalName = "Schedule", StoreAsText = true, DataType = PlanningColumnDataType.Data, SqlColumnName = "Schedule",
                AcceptedHeaders = new[] { "SCHEDULE", "PRAZO", "DATA PREVISTA" } },
            new PlanningColumnDefinition { LogicalName = "Reschedule", StoreAsText = true, DataType = PlanningColumnDataType.Data, SqlColumnName = "Reschedule",
                AcceptedHeaders = new[] { "RESCHEDULE", "REPROGRAMADA", "REPROGRAMADO" } },
            new PlanningColumnDefinition { LogicalName = "Concl", StoreAsText = true, DataType = PlanningColumnDataType.Data, SqlColumnName = "Concl",
                AcceptedHeaders = new[] { "CONCL" } },
            new PlanningColumnDefinition { LogicalName = "Critical", SqlColumnName = "Critical",
                AcceptedHeaders = new[] { "CRITICAL" } },
            new PlanningColumnDefinition { LogicalName = "Status", SqlColumnName = "Status", Required = true, IsRowIdentifier = true,
                AcceptedHeaders = new[] { "STATUS", "SITUAÇÃO", "SITUACAO" } },
            new PlanningColumnDefinition { LogicalName = "Comments", SqlColumnName = "COMMENTS",
                AcceptedHeaders = new[] { "COMMENTS", "COMENTÁRIOS", "COMENTARIOS", "OBSERVAÇÕES", "OBSERVACOES" } },
        };

        public static PlanningSheetDefinition External() => new PlanningSheetDefinition
        {
            ProcessName = "Etapa 12 - External Action",
            FolderName = "01.08 - EXTERNAL ACTION & PENDING ITEMS LOG",
            AcceptedWorksheetNames = new[] { "ACTION ITEM LIST" },
            DestinationTable = "[dbo].[tb_ExternalActionLog]",
            Columns = ColunasActionItemList()
        };

        public static PlanningSheetDefinition Internal() => new PlanningSheetDefinition
        {
            ProcessName = "Etapa 13 - Internal Action",
            FolderName = "01.09 - INTERNAL ACTION & PENDING ITEMS LOG",
            AcceptedWorksheetNames = new[] { "ACTION ITEM LIST" },
            DestinationTable = "[dbo].[tb_InternalActionLog]",
            Columns = ColunasActionItemList()
        };

        public static PlanningSheetDefinition ScopeDeviation() => new PlanningSheetDefinition
        {
            ProcessName = "Etapa 14 - Scope Deviation",
            FolderName = "01.12 - SCOPE DEVIATION",
            AcceptedWorksheetNames = new[] { "CONTROLE" },
            DestinationTable = "[dbo].[tb_ScopeDeviation]",
            Columns = new List<PlanningColumnDefinition>
            {
                new PlanningColumnDefinition { LogicalName = "Item", SqlColumnName = "ITEM", Required = true, IsRowIdentifier = true,
                    AcceptedHeaders = new[] { "ITEM" } },
                new PlanningColumnDefinition { LogicalName = "Descricao", SqlColumnName = "DESCRICAO", Required = true,
                    AcceptedHeaders = new[] { "DESCRIÇÃO", "DESCRICAO", "DESCRIPTION" } },
                new PlanningColumnDefinition { LogicalName = "Disciplina", SqlColumnName = "DISCIPLINA",
                    AcceptedHeaders = new[] { "DISCIPLINA", "DISCIPLINE" } },
                new PlanningColumnDefinition { LogicalName = "NumeroDesvio", SqlColumnName = "N_DO_DESVIO", Required = true, IsRowIdentifier = true,
                    AcceptedHeaders = new[] { "Nº DO DESVIO", "N° DO DESVIO", "NUMERO DO DESVIO", "NÚMERO DO DESVIO", "DEVIATION NUMBER", "DEVIATION NO." } },
                new PlanningColumnDefinition { LogicalName = "Data", StoreAsText = true, DataType = PlanningColumnDataType.Data, SqlColumnName = "DATA", Required = true,
                    AcceptedHeaders = new[] { "DATA", "DATE" } },
                new PlanningColumnDefinition { LogicalName = "QuantidadeHH", StoreAsText = true, DataType = PlanningColumnDataType.Decimal, SqlColumnName = "QUANT_HH",
                    AcceptedHeaders = new[] { "QTY. HH", "QUANT. HH", "QUANT HH", "QUANTIDADE HH", "QUANTITY OF HOURS", "HOURS" } },
                new PlanningColumnDefinition { LogicalName = "Status", SqlColumnName = "STATUS", Required = true,
                    AcceptedHeaders = new[] { "STATUS", "SITUAÇÃO", "SITUACAO" } },
                new PlanningColumnDefinition { LogicalName = "DesvioInterno", SqlColumnName = "DESVIO_INTERNO",
                    AcceptedHeaders = new[] { "DESVIO INTERNO", "INTERNAL DEVIATION" } },
                new PlanningColumnDefinition { LogicalName = "ChangeOrder", SqlColumnName = "CHANGE_ORDER",
                    AcceptedHeaders = new[] { "CHANGE ORDER", "ORDEM DE MUDANÇA", "ORDEM DE MUDANCA" } },
            }
        };

        public static PlanningSheetDefinition HoldControl() => new PlanningSheetDefinition
        {
            ProcessName = "Etapa 16 - Hold Control",
            FolderName = "01.15 - HOLD CONTROL",
            AcceptedWorksheetNames = new[] { "CONTROLE" },
            DestinationTable = "[dbo].[tb_HoldControl]",
            Columns = new List<PlanningColumnDefinition>
            {
                new PlanningColumnDefinition { LogicalName = "Item", SqlColumnName = "ITEM", Required = true, IsRowIdentifier = true,
                    AcceptedHeaders = new[] { "ITEM" } },
                new PlanningColumnDefinition { LogicalName = "NumeroHold", SqlColumnName = "N_HOLD", Required = true, IsRowIdentifier = true,
                    AcceptedHeaders = new[] { "Nº HOLD", "N° HOLD", "NUMERO HOLD", "NÚMERO HOLD", "HOLD NUMBER", "HOLD No." } },
                new PlanningColumnDefinition { LogicalName = "NumeroDocumentoNA", SqlColumnName = "N_NA_DOCUMENTO", Required = true, IsRowIdentifier = true,
                    AcceptedHeaders = new[] { "Nº N&A DO DOCUMENTO", "N° N&A DO DOCUMENTO", "NUMERO N&A DO DOCUMENTO", "NÚMERO N&A DO DOCUMENTO", "N&A DOCUMENT NUMBER", "NA DOCUMENT NUMBER", "DOCUMENT N&A No." } },
                new PlanningColumnDefinition { LogicalName = "Disciplina", SqlColumnName = "DISCIPLINA",
                    AcceptedHeaders = new[] { "DISCIPLINA", "DISCIPLINE" } },
                new PlanningColumnDefinition { LogicalName = "TituloDocumento", SqlColumnName = "TITULO_DOCUMENTO", Required = true,
                    AcceptedHeaders = new[] { "TÍTULO DO DOCUMENTO", "TITULO DO DOCUMENTO", "DOCUMENT TITLE" } },
                new PlanningColumnDefinition { LogicalName = "Revisao", SqlColumnName = "REV",
                    AcceptedHeaders = new[] { "REV", "REVISÃO", "REVISAO", "REVISION" } },
                new PlanningColumnDefinition { LogicalName = "MotivoHold", SqlColumnName = "MOTIVO_HOLD",
                    AcceptedHeaders = new[] { "MOTIVO DO HOLD", "HOLD REASON", "REASON FOR HOLD" } },
                new PlanningColumnDefinition { LogicalName = "Status", SqlColumnName = "STATUS", Required = true,
                    AcceptedHeaders = new[] { "STATUS", "SITUAÇÃO", "SITUACAO" } },
                new PlanningColumnDefinition { LogicalName = "Responsavel", SqlColumnName = "RESPONSAVEL",
                    AcceptedHeaders = new[] { "RESPONSÁVEL", "RESPONSAVEL", "RESPONSIBLE", "OWNER" } },
            }
        };
    }
}
