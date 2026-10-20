using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Repositório de dbo.Ldp_Documento_Gates (tabela já criada manualmente, fora deste código).
    /// Isolado de DatabaseService.cs — usa a mesma connection string ("LocalConnection"), mas em
    /// classe própria, sem alterar DatabaseService.cs.
    /// </summary>
    public interface ILdpTipo2GateRepository
    {
        Task<IReadOnlyList<LdpTipo2Gate>> ObterPorProjetoESincronizacaoAsync(
            string projectIdAcc,
            DateTime dataSincronizacao,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Leitura somente-diagnóstico: todos os gates do projeto (qualquer Data_Sincronizacao),
        /// incluindo Numero_Documento_Cliente e as colunas de rastreamento de sincronização com o
        /// Cronograma Tipo 2 — nunca usado no caminho de persistência principal (ver
        /// LdpTipo2GateDiagnostico).
        /// </summary>
        Task<IReadOnlyList<LdpTipo2GateDiagnostico>> ObterDiagnosticoPorProjetoAsync(
            string projectIdAcc,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Insere um lote de gates em uma transação PRÓPRIA (abre e fecha sua própria SqlConnection).
        /// Recusa o lote inteiro (nenhum INSERT é executado) se qualquer gate não tiver um
        /// LdpRegistroId válido (> 0). Mantido para outros usos isolados dos gates; a persistência da
        /// Fase 3 (documento + gates na MESMA transação) usa InserirLoteNaTransacaoAsync, não este método.
        /// </summary>
        Task SalvarLoteAsync(
            IReadOnlyCollection<LdpTipo2Gate> gates,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Insere um lote de gates participando de uma SqlConnection/SqlTransaction JÁ ABERTAS pelo
        /// chamador — nunca abre conexão própria, nunca chama Commit/Rollback (quem decide o destino da
        /// transação inteira, que também cobre a linha principal em Ldp_Unificada, é o chamador:
        /// LdpTipo2PersistenceService). Usa a mesma validação e o mesmo SQL parametrizado de
        /// SalvarLoteAsync.
        /// </summary>
        Task InserirLoteNaTransacaoAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            IReadOnlyCollection<LdpTipo2Gate> gates,
            CancellationToken cancellationToken = default);
    }
}
