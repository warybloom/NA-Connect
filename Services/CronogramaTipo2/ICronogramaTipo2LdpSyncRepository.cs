using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using APS.WebApp.Models;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>
    /// Acesso a dados da Fase 4B: carrega o snapshot mais recente do Cronograma Tipo 2 e a última versão
    /// dos gates da LDP Tipo 2 (sempre pelas views *_Ultima_Versao — nunca soma/mistura sincronizações
    /// antigas), e aplica os UPDATEs de matching sempre dentro de uma transação já aberta pelo chamador
    /// (CronogramaTipo2LdpSyncService). Nunca abre conexão/transação própria, nunca executa DDL.
    /// </summary>
    public interface ICronogramaTipo2LdpSyncRepository
    {
        /// <summary>
        /// Snapshot mais recente do Cronograma Tipo 2 (dbo.VW_Cronograma_Tipo2_Ultima_Versao), restrito a
        /// Tipo_LDP = 2 e Status_Validacao_Documento_Gate = 'VALIDO' — apenas candidatos estruturalmente
        /// válidos (não resumo, não sem N&amp;A, não sem fase, não fase inválida, não duplicado)
        /// participam do matching com a LDP.
        /// </summary>
        Task<IReadOnlyList<CronogramaTipo2Registro>> ObterUltimoSnapshotCronogramaAsync(
            string projectIdAcc,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Última versão de todos os gates da LDP Tipo 2 do projeto (dbo.VW_Ldp_Documento_Gates_Ultima_Versao),
        /// ativos e inativos — a matriz de decisão (seção 13 do pedido) precisa dos dois grupos.
        /// </summary>
        Task<IReadOnlyList<GateLdpParaSincronizacao>> ObterGatesLdpUltimaVersaoAsync(
            string projectIdAcc,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// UPDATE de UM gate em dbo.Ldp_Documento_Gates, restrito ao Id real (nunca por texto) e validado
        /// também por Project_ID_ACC + Ldp_Registro_Id + Gate_Percentual (seção 14). Só deve ser chamado
        /// para MATCH_ENCONTRADO com Gate_Ativo = 1.
        /// </summary>
        Task<int> AtualizarGateAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            long gateId,
            string projectIdAcc,
            long ldpRegistroId,
            byte gatePercentual,
            DateTime? plannedBaseline,
            DateTime? replannedDate,
            decimal? plannedPhysical,
            decimal? physicalCompleted,
            string cronogramaUidTarefa,
            DateTime cronogramaDataSincronizacao,
            DateTime dataAtualizacao,
            string statusSincronizacao,
            string mensagemSincronizacao,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// UPDATE do status de matching em UMA linha de dbo.Cronograma_Projetos, restrito ao ID_Cronograma
        /// real (PK) — nunca sobrescreve Status_Validacao_Documento_Gate (seção 16).
        /// </summary>
        Task<int> AtualizarStatusMatchingCronogramaAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int idCronograma,
            string statusMatchingLdpGate,
            string mensagemMatchingLdpGate,
            long? ldpGateIdMatch,
            long? ldpRegistroIdMatch,
            DateTime dataMatchingLdpGate,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Projeção de dbo.VW_Ldp_Documento_Gates_Ultima_Versao com os campos necessários para o
    /// UPDATE seguro por Id (diferente de GateLdpParaMatching, usado apenas no matching SOMENTE LEITURA
    /// da Fase 4A — aqui é preciso o Id real do gate e o Ldp_Registro_Id para o UPDATE da seção 14).</summary>
    public sealed class GateLdpParaSincronizacao
    {
        public long Id { get; set; }
        public long LdpRegistroId { get; set; }
        public string ProjectIdAcc { get; set; }
        public string NumeroDocumentoNa { get; set; }

        /// <summary>
        /// Identidade alternativa da mesma linha da LDP — usada como FALLBACK de matching quando nenhuma
        /// tarefa do cronograma bate com NumeroDocumentoNa (ver
        /// CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching). Nunca é a chave de persistência —
        /// só serve para localizar a linha; a partir daí NumeroDocumentoNa (desta mesma instância) já é a
        /// identidade canônica.
        /// </summary>
        public string NumeroDocumentoCliente { get; set; }

        public byte GatePercentual { get; set; }
        public bool GateAtivo { get; set; }
    }
}
