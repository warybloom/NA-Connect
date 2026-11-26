using System.Collections.Generic;

namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>Os 13 arquivos operacionais protegidos (única lista, nunca reescrita em outro lugar do CLI).</summary>
    public static class ProtectedFilesList
    {
        public static readonly IReadOnlyList<string> RelativePaths = new[]
        {
            "Services/GrdAutomationWorker.cs",
            "Services/LdpUpdateService.cs",
            "Services/AccReviewWorkflowService.cs",
            "Services/AccReviewStageSnapshotService.cs",
            "Services/AccScheduleSyncService.cs",
            "Services/PlanningControlSyncService.cs",
            "Services/QualityControlSyncService.cs",
            "Services/DatabaseService.cs",
            "Services/AutodeskApiService.cs",
            "Services/DocumentMatchingService.cs",
            "Services/GrdToLdpSyncService.cs",
            "Services/LdpToCustoSyncService.cs",
            "Services/TimesheetDumpService.cs"
        };
    }
}
