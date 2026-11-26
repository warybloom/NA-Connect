using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace APS.WebApp.Services
{
    /// <summary>
    /// Configuração lida de "WorkerTest" no appsettings.json. Quatro chaves, nenhuma outra
    /// ("SomenteEtapa7" e afins não devem ser criadas — usar sempre Etapas):
    ///   Enabled          — false roda o fluxo normal (todos os projetos ativos, todas as etapas).
    ///   ProjectId        — quando Enabled=true, restringe a um único Project_ID_ACC; vazio/nulo
    ///                      significa "todos os projetos Is_Ativo=1" (mesma consulta do fluxo normal),
    ///                      mas ainda restringindo as etapas conforme Etapas (correção 17/09/2026,
    ///                      pedido "URGENTE — EXECUTAR ETAPA 01 + ETAPA 03 EM TODOS OS PROJETOS ATIVOS").
    ///   Etapas           — lista de strings (aceita IDs alfanuméricos, ex.: "7", "5B"); obrigatório
    ///                      ao menos um item quando Enabled=true; ignorada quando Enabled=false.
    ///   ExecutarUmaVez   — quando true, uma segunda chamada a ProcessarCicloCompleto no mesmo
    ///                      processo é bloqueada (ver WorkerTestSelector.DeveBloquearRepeticao).
    /// </summary>
    public sealed class WorkerTestConfig
    {
        public bool Enabled { get; set; }
        public string ProjectId { get; set; }
        public List<string> Etapas { get; set; } = new List<string>();
        public bool ExecutarUmaVez { get; set; } = true;
    }

    /// <summary>
    /// Seletor genérico de etapas do WorkerTest (seção 16 do pedido de correção da Etapa 7) — decisão
    /// central equivalente a "DeveExecutarEtapa('7')", usada por GrdAutomationWorker antes de CADA etapa
    /// numerada (0, 7, 11, 12, 13, 14, 15, 16, 17, ...) em vez das flags ad-hoc anteriores
    /// (SomenteEtapas12a17 / SomenteEtapa17Qualidade). Toda a lógica aqui é pura (sem IConfiguration,
    /// sem ACC, sem SQL) para ser testável em isolamento — só LerConfiguracao toca IConfiguration.
    /// </summary>
    public static class WorkerTestSelector
    {
        public static WorkerTestConfig LerConfiguracao(IConfiguration configuration)
        {
            var etapas = configuration.GetSection("WorkerTest:Etapas").Get<List<string>>() ?? new List<string>();
            return new WorkerTestConfig
            {
                Enabled = configuration.GetValue<bool>("WorkerTest:Enabled"),
                ProjectId = configuration["WorkerTest:ProjectId"],
                Etapas = etapas,
                ExecutarUmaVez = configuration.GetValue("WorkerTest:ExecutarUmaVez", true)
            };
        }

        /// <summary>
        /// Valida a configuração ANTES de processar qualquer projeto. Quando Enabled=true, exige ao
        /// menos uma Etapa — nunca cai acidentalmente para "todas as etapas" por configuração
        /// incompleta. ProjectId é opcional: vazio/nulo restringe as Etapas mas mantém o universo
        /// normal de projetos (Is_Ativo=1); preenchido restringe a um único projeto (comportamento
        /// original, inalterado).
        /// </summary>
        public static (bool Valido, string Erro) Validar(WorkerTestConfig config)
        {
            if (config == null) return (false, "Configuração WorkerTest ausente.");
            if (!config.Enabled) return (true, null);

            if (config.Etapas == null || config.Etapas.Count == 0)
                return (false, "WorkerTest:Enabled=true exige ao menos uma etapa em WorkerTest:Etapas.");

            return (true, null);
        }

        /// <summary>
        /// Decisão central: esta etapa deve rodar neste ciclo? Enabled=false roda todas (fluxo normal,
        /// nunca depende de WorkerTest:Etapas). Enabled=true só roda as etapas listadas (comparação
        /// sem diferenciar maiúsculas/minúsculas — "7" == "7", "5b" == "5B").
        /// </summary>
        public static bool DeveExecutarEtapa(WorkerTestConfig config, string etapaId)
        {
            if (config == null || !config.Enabled) return true;
            return config.Etapas != null && config.Etapas.Contains(etapaId, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// ExecutarUmaVez=true bloqueia uma segunda execução do ciclo dentro do MESMO processo — o
        /// chamador (GrdAutomationWorker) mantém o estado "já executou" e passa aqui a cada tentativa.
        /// </summary>
        public static bool DeveBloquearRepeticao(WorkerTestConfig config, bool jaExecutouNesteProcesso)
        {
            return config != null && config.ExecutarUmaVez && jaExecutouNesteProcesso;
        }
    }
}
