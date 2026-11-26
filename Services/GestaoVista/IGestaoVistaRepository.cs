using System;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>Resultado de UMA tentativa de persistir um registro de Custo Real (SCD tipo 2).</summary>
    public enum GestaoVistaCustoRealOutcome
    {
        /// <summary>Nenhuma versão anterior para esta pessoa/projeto — primeira versão inserida.</summary>
        PrimeiraVersao,
        /// <summary>Já existia versão vigente com o MESMO RowHash — nada foi alterado (idempotente).</summary>
        MesmaVersao,
        /// <summary>Versão vigente tinha RowHash diferente — anterior fechada (VigenciaFim) e nova inserida.</summary>
        NovaVersao,
    }

    /// <summary>
    /// Persistência da Gestão à Vista em [gestao] (schema/tabelas criados manualmente pelas migrations
    /// 01/05/06/07 — NUNCA por este repositório, que nunca executa DDL). Todo método aqui assume que o
    /// chamador (LdpGestaoVistaSyncService) já confirmou SchemaDisponivelAsync()==true antes de tentar
    /// qualquer INSERT/UPDATE — nenhum método de escrita cria schema/tabela.
    /// </summary>
    public interface IGestaoVistaRepository
    {
        /// <summary>Detecta, via sys.schemas/sys.tables (nunca por tentativa de INSERT/catch), se o
        /// schema [gestao] e as 3 tabelas existem neste banco. Nunca lança exceção.</summary>
        Task<bool> SchemaDisponivelAsync(SqlConnection connection, CancellationToken cancellationToken = default);

        Task<GestaoVistaCustoRealOutcome> PersistirCustoRealAsync(
            SqlConnection connection, SqlTransaction transaction, string projectIdAcc,
            GestaoVistaCustoRealExtraido registro, DateTime dataSincronizacao, CancellationToken cancellationToken = default);

        /// <summary>true = inserido agora; false = já existia (mesmo RowHash) — idempotente.</summary>
        Task<bool> PersistirCustoPrevistoAsync(
            SqlConnection connection, SqlTransaction transaction, string projectIdAcc,
            GestaoVistaCustoPrevistoExtraido registro, DateTime dataSincronizacao, CancellationToken cancellationToken = default);

        /// <summary>Custo/hora vigente para a pessoa na data de referência (DataLancamento do lançamento,
        /// ou a própria DataSincronizacao quando o lançamento não tem data) — null quando não houver
        /// nenhuma versão de Custo Real cobrindo essa data (nunca inventa um valor).</summary>
        Task<decimal?> ObterCustoHoraVigenteAsync(
            SqlConnection connection, SqlTransaction transaction, string projectIdAcc,
            string nomeProfissional, DateTime dataReferencia, CancellationToken cancellationToken = default);

        /// <summary>true = inserido agora; false = já existia (mesmo RowHash) — idempotente.</summary>
        Task<bool> PersistirTimesheetAsync(
            SqlConnection connection, SqlTransaction transaction, string projectIdAcc,
            GestaoVistaTimesheetLancamentoExtraido registro, decimal? custoCalculado, DateTime dataSincronizacao,
            CancellationToken cancellationToken = default);
    }
}
