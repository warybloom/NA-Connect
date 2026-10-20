using System;

namespace APS.WebApp.Services.OperationalAudit.Safety
{
    /// <summary>
    /// Lançada quando qualquer parte do subsistema de auditoria tenta uma operação de escrita proibida
    /// (seção 3/18 da correção da Fase 2) — SQL na conexão de origem, HTTP não-GET para o ACC, ou
    /// persistência de Excel. Nunca capturada silenciosamente por nenhum código da auditoria: deve sempre
    /// propagar e falhar a execução (ou o teste) de forma explícita.
    /// </summary>
    public sealed class ForbiddenOperationException : Exception
    {
        public ForbiddenOperationException(string message) : base(message) { }
        public ForbiddenOperationException(string message, Exception innerException) : base(message, innerException) { }
    }
}
