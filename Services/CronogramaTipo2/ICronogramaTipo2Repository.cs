using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using APS.WebApp.Models;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>
    /// Repositório de dbo.Cronograma_Projetos para o cronograma Tipo 2. Isolado de DatabaseService.cs
    /// (mesma connection string "LocalConnection", classe própria) — nunca reaproveita nem altera
    /// DatabaseService.SalvarTarefaCronograma (que faz upsert por UID_Tarefa para o Tipo 1). Aqui é
    /// sempre INSERT puro: cada execução válida gera um novo snapshot completo, nunca UPDATE/DELETE em
    /// linhas antigas.
    /// </summary>
    public interface ICronogramaTipo2Repository
    {
        /// <summary>
        /// Insere todos os registros (já classificados pelo parser) numa SqlConnection/SqlTransaction
        /// JÁ ABERTAS pelo chamador — nunca abre conexão/transação própria, nunca chama Commit/Rollback.
        /// </summary>
        Task<int> InserirLoteAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string projectIdAcc,
            IReadOnlyCollection<CronogramaTipo2Registro> registros,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Consulta SOMENTE LEITURA da última versão dos gates da LDP para este projeto (usada apenas
        /// para o matching de teste da seção 17 — nunca gera UPDATE).
        /// </summary>
        Task<IReadOnlyList<GateLdpParaMatching>> ObterGatesLdpParaMatchingAsync(
            string projectIdAcc,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Projeção mínima de dbo.VW_Ldp_Documento_Gates_Ultima_Versao usada só para matching.</summary>
    public sealed class GateLdpParaMatching
    {
        public string NumeroDocumentoNa { get; set; }

        /// <summary>Identidade alternativa da mesma linha — fallback de matching (ver
        /// CronogramaTipo2Service.ClassificarMatch), mesma regra do matching real em
        /// CronogramaTipo2LdpSyncService.</summary>
        public string NumeroDocumentoCliente { get; set; }

        public byte GatePercentual { get; set; }
        public bool GateAtivo { get; set; }
    }
}
