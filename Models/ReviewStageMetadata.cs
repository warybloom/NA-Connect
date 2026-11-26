using System;
using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Metadados da etapa atual de UM ACC Review — construído em AccReviewWorkflowService a partir do
    /// mesmo payload de detalhe do Review já lido por BuildWorkflowResultAsync (nunca uma segunda
    /// varredura). Aditivo ao mapeamento de Status_In_Flow: nunca influencia WorkflowStatusEntry.Status
    /// nem a decisão de DecidirStatusDaVersao.
    /// </summary>
    public sealed class ReviewStageMetadata
    {
        public string ReviewId { get; set; }
        public string ReviewNumber { get; set; }
        public string ReviewName { get; set; }
        public string ReviewStatus { get; set; }

        public string WorkflowId { get; set; }
        public string CurrentStepId { get; set; }
        public string CurrentStepName { get; set; }

        public List<string> ResponsibleNames { get; set; } = new List<string>();
        public List<string> ResponsibleIds { get; set; } = new List<string>();
        public string ResponsibleSource { get; set; }

        public string DueDateRaw { get; set; }
        public DateTimeOffset? DueDateOriginal { get; set; }
        public DateOnly? DueDateLocal { get; set; }
        public string DueDateSource { get; set; }

        public DateTime? ReviewUpdatedAt { get; set; }
    }
}
