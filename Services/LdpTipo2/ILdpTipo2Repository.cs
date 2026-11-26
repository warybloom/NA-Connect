using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Repositório da linha principal da LDP Tipo 2 em dbo.Ldp_Unificada (mesma tabela histórica usada
    /// pelo Tipo 1 — nunca criada/recriada/alterada aqui). Isolado de DatabaseService.cs: usa a mesma
    /// connection string ("LocalConnection"), mas em classe própria, sem alterar DatabaseService.cs.
    ///
    /// Todo método aqui recebe uma SqlConnection/SqlTransaction JÁ ABERTAS pelo chamador
    /// (LdpTipo2PersistenceService) — nunca abre conexão ou transação própria, e nunca chama
    /// Commit/Rollback: quem decide o destino da transação inteira (que também cobre os gates em
    /// dbo.Ldp_Documento_Gates) é sempre o chamador.
    /// </summary>
    public interface ILdpTipo2Repository
    {
        /// <summary>
        /// Insere uma linha principal em dbo.Ldp_Unificada para o documento informado e devolve o valor
        /// REAL da chave primária gerada pelo banco (ID_Registro, int IDENTITY) via
        /// OUTPUT INSERTED.ID_Registro — nunca SCOPE_IDENTITY()/SELECT MAX ou qualquer busca por
        /// NumeroDocumentoNa/DataSincronizacao/ProjectIdAcc.
        /// </summary>
        Task<long> InserirDocumentoPrincipalAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            LdpTipo2Documento documento,
            string projectIdAcc,
            DateTime dataSincronizacao,
            CancellationToken cancellationToken = default);
    }
}
