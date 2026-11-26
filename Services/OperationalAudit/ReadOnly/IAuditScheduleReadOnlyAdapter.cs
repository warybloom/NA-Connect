using System.Collections.Generic;
using System.IO;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    /// <summary>Uma tarefa do MPP, já com os campos candidatos a REALIZADO/PREVISTO resolvidos por alias — nunca por amostragem de valores (a mesma regra estrutural do operacional real).</summary>
    public sealed class AuditScheduleTaskRef
    {
        public string Uid { get; init; } = string.Empty;
        public string Nome { get; init; } = string.Empty;
        public string? NumeroDocumentoNA { get; init; }

        /// <summary>Nome do campo MPXJ (ex.: "Number1") efetivamente resolvido para REALIZADO — null se não resolvido.</summary>
        public string? CampoRealizadoResolvido { get; init; }
        public double? ValorRealizado { get; init; }

        public string? CampoPrevistoResolvido { get; init; }
        public double? ValorPrevisto { get; init; }
    }

    public sealed class AuditScheduleSnapshot
    {
        public IReadOnlyList<AuditScheduleTaskRef> Tarefas { get; init; } = System.Array.Empty<AuditScheduleTaskRef>();

        /// <summary>true quando REALIZADO e PREVISTO resolveram para o MESMO campo MPXJ (CRN-005/ambiguidade estrutural) — nunca decidido por amostra de valor.</summary>
        public bool RealizadoEPrevistoColidem { get; init; }
    }

    /// <summary>
    /// Lê um arquivo MPP SOMENTE LEITURA via MPXJ.Net — independente de <c>AccScheduleSyncService</c>
    /// (subsistema paralelo, seção 1/12 do Checkpoint 2). Nunca grava, nunca faz upload.
    /// </summary>
    public interface IAuditScheduleReadOnlyAdapter
    {
        AuditScheduleSnapshot LerSomenteLeitura(Stream conteudoMpp);
    }
}
