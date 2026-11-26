using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace APS.WebApp.Services.OperationalAudit.Safety
{
    /// <summary>
    /// <see cref="DelegatingHandler"/> que bloqueia qualquer verbo HTTP diferente de GET/HEAD/OPTIONS
    /// (seção 18: POST/PUT/PATCH/DELETE nunca podem sair de um <see cref="HttpClient"/> da auditoria).
    /// Deve ser o handler mais externo de QUALQUER <see cref="HttpClient"/> criado para adaptadores
    /// somente-leitura do ACC (Checkpoint 2+) — nenhum adaptador de leitura da auditoria deve construir um
    /// <see cref="HttpClient"/> sem este handler.
    /// </summary>
    public sealed class AuditHttpWriteGuardHandler : DelegatingHandler
    {
        public AuditHttpWriteGuardHandler() { }

        public AuditHttpWriteGuardHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head && request.Method != HttpMethod.Options)
            {
                throw new ForbiddenOperationException(
                    $"[OperationalAudit] Método HTTP '{request.Method}' bloqueado — a auditoria só pode fazer GET/HEAD/OPTIONS. URL: {request.RequestUri}");
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}
