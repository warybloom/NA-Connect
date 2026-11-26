using System;
using System.Collections.Generic;
using System.Linq;

namespace APS.WebApp.Services
{
    /// <summary>
    /// Classificação das etapas operacionais do Worker (0-17) por compatibilidade com Tipo_LDP.
    ///
    /// Correção 16/09/2026 (fase 1): CHS001/Tipo2 pulava integralmente as 17 etapas por causa de um
    /// `continue` logo após o roteamento para LdpProcessingRouterService.
    /// Correção 16/09/2026 (fase 2): das 8 etapas que ficaram exclusivas do Tipo1 na fase 1, 7
    /// (00,01,02,05,06,09,11) ganharam um EQUIVALENTE Tipo2 real — reconhecido a partir do que o router
    /// Tipo2/Gestão à Vista JÁ executaram no mesmo ciclo (nunca uma segunda execução). Só a Etapa 10
    /// (Espelhamento) continuou pulada — decisão arquitetural explícita, aguardando redesenho.
    /// Correção 16/09/2026 (fase 3): a fase 2 estava errada em dois pontos:
    ///   - Etapa 02 não tem "equivalente Tipo2" nenhum — foi simplesmente DESLIGADA (decisão nossa,
    ///     temporária, independente de Tipo_LDP; implementação preservada, não removida). Passou para
    ///     sua própria lista (EtapasDesabilitadasTemporariamente), nunca "EhPuladaNoTipo2" (isso
    ///     sugeriria incompatibilidade Tipo1/Tipo2, que não é o caso).
    ///   - Etapa 05 não tem "variante Tipo1" e "variante Tipo2" — é UMA SÓ regra (ACC Reviews, N&A/
    ///     Cliente, Status_In_Flow), chamada uma única vez, incondicionalmente, para qualquer Tipo_LDP.
    ///     Passou para EtapasComuns.
    /// Correção 17/09/2026 (fase 4 — esta, regressão funcional corrigida): Etapa 01 também estava errada
    /// na fase 2 — "Gestão à Vista já executou o equivalente" era incorreto; Gestão à Vista NUNCA
    /// substitui a reconstrução física da aba Timesheet (TimesheetDumpService.ResetAndDumpTimesheetAsync,
    /// fonte tbTimeSheetRealizadoDocumento). Não existe "Etapa 01 Tipo1"/"Etapa 01 Tipo2" — é UMA SÓ
    /// regra, chamada uma única vez, incondicionalmente, para qualquer Tipo_LDP, ANTES de Gestão à Vista.
    /// Passou de EtapasComEquivalenteTipo2 para EtapasComuns.
    ///
    /// REGRA DE OURO: Tipo_LDP define SOMENTE parser/persistência da LDP, parser/persistência do
    /// Cronograma, matching Cronograma&lt;-&gt;LDP e regras de gate/físico — nunca as demais etapas, que
    /// são independentes da estrutura da LDP/Cronograma.
    /// </summary>
    public static class WorkerStageRouting
    {
        /// <summary>
        /// O espelho Tipo 2 possui fluxo próprio após GRD; nenhuma etapa é pulada por incompatibilidade.
        /// </summary>
        public static readonly IReadOnlyList<(string Id, string Nome)> EtapasPuladasNoTipo2 = Array.Empty<(string Id, string Nome)>();

        /// <summary>
        /// Etapas desligadas por decisão nossa, temporariamente, independente de Tipo_LDP — nunca
        /// "TIPO_INCOMPATIVEL". A implementação original continua preservada e intacta no código; só a
        /// CHAMADA foi comentada/protegida. Motivo (Etapa 02): revisão/horas ainda será revisitada
        /// arquiteturalmente antes de reativar.
        /// </summary>
        public static readonly IReadOnlyList<(string Id, string Nome)> EtapasDesabilitadasTemporariamente = new[]
        {
            ("02", "Horas e Revisão"),
        };

        /// <summary>
        /// Etapas cujo mecanismo ORIGINAL (posição fixa de coluna na aba "LDP", layout Tipo1) continua
        /// exclusivo do Tipo1, mas que ganharam um equivalente Tipo2 real, reconhecido a partir de
        /// trabalho já executado no mesmo ciclo (router Tipo2 / Gestão à Vista) — nunca uma segunda
        /// execução, nunca um resultado fingido. Ver GrdAutomationWorker para a lógica de reconhecimento
        /// de cada uma:
        ///   00 (DNA)              — confirma LDP Tipo2 persistida (router) + Project_Language existente.
        ///   06 (Custo)            — Gestão à Vista (gestaoVistaResultado.CustoReal/CustoPrevisto).
        ///   09 (Cronograma->LDP)  — CronogramaTipo2Service + CronogramaTipo2LdpSyncService (router).
        ///   11 (Persistência LDP) — ILdpTipo2Service (router).
        /// </summary>
        public static readonly IReadOnlyList<(string Id, string Nome)> EtapasComEquivalenteTipo2 = new[]
        {
            ("00", "DNA do Projeto"),
            ("06", "Custo"),
            ("09", "Cronograma (legado, Tipo1)"),
            ("10", "Espelhamento Tipo 2 (após GRD)"),
            ("11", "Persistência LDP Unificada"),
        };

        /// <summary>
        /// Etapas independentes de Tipo_LDP — rodam exatamente uma vez, com a MESMA regra, para
        /// qualquer projeto. Etapa 05 (Workflow/Status_In_Flow) entrou aqui na fase 3, Etapa 01
        /// (Timesheet) na fase 4: nenhuma das duas tinha motivo real para depender do layout físico da
        /// LDP — só o ACESSO FÍSICO a colunas específicas varia internamente (ver
        /// LdpUpdateService.LocalizarColunaStatusInFlow e LdpHeaderLookup), a regra de negócio de cada
        /// uma é uma autoridade única.
        /// </summary>
        public static readonly IReadOnlyList<(string Id, string Nome)> EtapasComuns = new[]
        {
            ("01", "Timesheet"),
            ("04", "GRDs"),
            ("05", "Workflow / Status_In_Flow"),
            ("05B", "Snapshot ACC Reviews"),
            ("07", "Atributos ACC"),
            ("12", "External Action Log"),
            ("13", "Internal Action Log"),
            ("14", "Scope Deviation"),
            ("15", "Weekly Engineering Schedule"),
            ("16", "Hold Control"),
            ("17", "Qualidade"),
        };

        public static bool EhPuladaNoTipo2(string etapaId) =>
            EtapasPuladasNoTipo2.Any(e => string.Equals(e.Id, etapaId, StringComparison.OrdinalIgnoreCase));

        public static bool EhDesabilitadaTemporariamente(string etapaId) =>
            EtapasDesabilitadasTemporariamente.Any(e => string.Equals(e.Id, etapaId, StringComparison.OrdinalIgnoreCase));

        public static bool TemEquivalenteTipo2(string etapaId) =>
            EtapasComEquivalenteTipo2.Any(e => string.Equals(e.Id, etapaId, StringComparison.OrdinalIgnoreCase));

        public static bool EhComum(string etapaId) =>
            EtapasComuns.Any(e => string.Equals(e.Id, etapaId, StringComparison.OrdinalIgnoreCase));
    }
}
