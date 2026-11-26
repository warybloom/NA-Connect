using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Persistência do SNAPSHOT ATUAL do Timesheet (gestao.TimesheetSnapshotAtual — tabela NOVA,
    /// distinta de gestao.TimesheetLancamento) usada pela ETAPA 03. Diferença deliberada de
    /// IGestaoVistaRepository: nenhum método aqui recebe SqlConnection/SqlTransaction — cada chamada
    /// gerencia sua própria conexão internamente, para que o orquestrador (Etapa03TimesheetSnapshotService)
    /// seja inteiramente testável com um fake em memória, sem qualquer dependência de SQL Server real.
    /// Nunca executa DDL — a tabela é criada manualmente por uma migration (Migrations/GestaoVista/08_*),
    /// mesmo padrão das 3 tabelas existentes.
    /// </summary>
    public interface IGestaoVistaTimesheetSnapshotRepository
    {
        /// <summary>Detecta, via sys.tables (nunca por tentativa de INSERT/catch), se
        /// gestao.TimesheetSnapshotAtual existe neste banco. Nunca lança exceção.</summary>
        Task<bool> TabelaDisponivelAsync(CancellationToken cancellationToken = default);

        /// <summary>Resumo do snapshot ATUALMENTE persistido para o projeto, ANTES de qualquer
        /// substituição desta execução — (0, 0) quando não há nenhum snapshot ainda.</summary>
        Task<(int Linhas, decimal HH)> ObterResumoAtualAsync(string projectIdAcc, CancellationToken cancellationToken = default);

        /// <summary>Custo/hora vigente do profissional na data de referência (mesma regra/fonte de
        /// IGestaoVistaRepository.ObterCustoHoraVigenteAsync — gestao.CustoProfissionalRealHistorico —
        /// reimplementada aqui apenas para não vazar SqlConnection/SqlTransaction na assinatura). Null
        /// quando não houver nenhuma versão de Custo Real cobrindo essa data.</summary>
        Task<decimal?> ObterCustoHoraVigenteAsync(
            string projectIdAcc, string nomeProfissional, DateTime dataReferencia, CancellationToken cancellationToken = default);

        /// <summary>
        /// Substitui, em UMA transação, o snapshot atual do projeto (remove só as linhas deste
        /// Project_ID_ACC, nunca de outro; insere todas as linhas informadas) — e só faz COMMIT se
        /// ABS(hhEsperadoExcel - somaHHInserida) &lt;= 0.01; caso contrário faz ROLLBACK (o snapshot
        /// anterior permanece intacto) e devolve Sucesso=false. Nunca lança exceção — falhas viram
        /// Sucesso=false/Erros.
        /// </summary>
        Task<GestaoVistaTimesheetSnapshotCommitResultado> SubstituirSnapshotAsync(
            string projectIdAcc, IReadOnlyList<GestaoVistaTimesheetSnapshotLinha> linhas, decimal hhEsperadoExcel,
            DateTime dataSincronizacao, CancellationToken cancellationToken = default);
    }
}
