using System;
using APS.WebApp.Services.OperationalAudit.Configuration;

namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>
    /// Checkpoint 4, seções 7/9; Checkpoint 4.1, seção 3: única fonte de verdade sobre
    /// <see cref="OperationalAuditOptions.PersistToDatabase"/> E <see cref="OperationalAuditOptions.Enabled"/>
    /// para o CLI — nunca o valor cru de <c>appsettings.json</c>/ambiente decide sozinho. O MODO explícito
    /// escolhido pelo usuário na linha de comando sempre vence: <c>--dry-run</c> e
    /// <c>--run --allow-live-read</c> (sem <c>--persist</c>) SEMPRE forçam <c>PersistToDatabase=false</c>,
    /// mesmo que o arquivo de configuração diga o contrário — só <c>--run --allow-live-read --persist</c>
    /// habilita persistência.
    ///
    /// <see cref="Aplicar"/> marca <c>Enabled=true</c> só na instância de <see cref="OperationalAuditOptions"/>
    /// do PRÓPRIO processo CLI (singleton do container de DI deste processo, construído a partir do
    /// <c>appsettings.json</c> do CLI) — nunca escreve em disco, nunca afeta o site (processo, config e
    /// container inteiramente separados).
    /// </summary>
    public static class CliModeOptionsApplier
    {
        public static void Aplicar(OperationalAuditOptions options, CliArguments argumentos)
        {
            options.Enabled = true; // o CLI só chega aqui se o usuário pediu explicitamente — nunca depende de OperationalAudit:Enabled do app principal.
            options.PersistToDatabase = argumentos.Mode == CliMode.LivePersist;
        }

        /// <summary>
        /// Checkpoint 4.1, seção 3 — defesa em profundidade (nunca a única barreira: a principal é
        /// estrutural, o site nunca chama o runner). Chamado imediatamente antes de
        /// <c>IOperationalAuditRunner.ExecutarAsync</c> em qualquer modo ao vivo; se <see cref="Aplicar"/>
        /// não tiver sido chamado antes por algum bug futuro, isso falha alto em vez de silenciosamente
        /// prosseguir com uma auditoria "desabilitada".
        /// </summary>
        public static void GarantirHabilitadoParaExecucao(OperationalAuditOptions options)
        {
            if (!options.Enabled)
            {
                throw new InvalidOperationException(
                    "[OperationalAudit.Cli] Options.Enabled=false ao tentar executar — isso indica que CliModeOptionsApplier.Aplicar não foi chamado antes. Execução recusada.");
            }
        }
    }
}
