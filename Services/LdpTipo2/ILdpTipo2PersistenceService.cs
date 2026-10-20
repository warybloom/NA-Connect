using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Persistência transacional da Fase 3 da LDP Tipo 2: recebe o LdpTipo2Resultado já produzido pelo
    /// parser (ILdpTipo2Parser/LdpTipo2Service) e grava, numa ÚNICA transação por arquivo/projeto, uma
    /// linha principal em dbo.Ldp_Unificada por documento e exatamente três linhas (gates 30/60/90) em
    /// dbo.Ldp_Documento_Gates por documento. Nunca faz upload de planilha, nunca altera a LDP no ACC,
    /// nunca é chamado pelo Worker automático nesta fase — uso isolado/manual.
    /// </summary>
    public interface ILdpTipo2PersistenceService
    {
        Task<LdpTipo2PersistenceResult> PersistirAsync(
            LdpTipo2Resultado resultadoLeitura,
            CancellationToken cancellationToken = default);
    }
}
