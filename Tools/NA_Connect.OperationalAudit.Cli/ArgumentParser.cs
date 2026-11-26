using System.Linq;

namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>
    /// Interpretação PURA (nenhuma I/O, nunca abre conexão/ACC) dos argumentos de linha de comando —
    /// Checkpoint 4, seções 7/8; corrigido no Checkpoint 4.1, seção 5. Toda combinação proibida é recusada
    /// AQUI, antes de qualquer código de execução ser alcançado:
    ///
    ///   --run sozinho                              → RECUSA
    ///   --dry-run sozinho                          → RECUSA (seção 5 do Checkpoint 4.1: --dry-run faz
    ///                                                 LEITURA REAL de ACC/SQL — nunca é "só fake/offline" —
    ///                                                 então exige --allow-live-read igual a --run; só
    ///                                                 PersistToDatabase continua sempre false neste modo)
    ///   --persist sem --allow-live-read             → RECUSA
    ///   --all-projects sem --confirm-all-projects   → RECUSA
    ///   nenhum modo de execução                     → RECUSA (ajuda)
    ///   mais de um modo de execução                 → RECUSA
    ///   nenhum projeto selecionado (fora ValidateConfig) → RECUSA
    ///
    /// Nenhuma flag combinação consegue habilitar escrita operacional — não existe combinação de flags
    /// que resulte em <see cref="CliMode.LivePersist"/> sem <c>--allow-live-read</c> explícito, e mesmo
    /// esse modo só permite persistência no schema <c>audit</c> (nunca em tabela operacional — isso é
    /// garantido por código mais abaixo na cadeia, nunca aqui).
    /// </summary>
    public static class ArgumentParser
    {
        public static ArgumentParseResult Parse(string[]? args)
        {
            args ??= System.Array.Empty<string>();

            if (args.Length == 0 || Contem(args, "--help", "-h", "-?"))
            {
                return ArgumentParseResult.Ok(new CliArguments { Mode = CliMode.Help });
            }

            bool validateConfig = Contem(args, "--validate-config");
            bool validateConnections = Contem(args, "--validate-connections");
            bool dryRun = Contem(args, "--dry-run");
            bool run = Contem(args, "--run");
            bool allowLiveRead = Contem(args, "--allow-live-read");
            bool persist = Contem(args, "--persist");
            bool allProjects = Contem(args, "--all-projects");
            bool confirmAllProjects = Contem(args, "--confirm-all-projects");
            string? projectId = ObterValor(args, "--project-id");

            int quantosModos = new[] { validateConfig, validateConnections, dryRun, run }.Count(x => x);
            if (quantosModos == 0)
            {
                return ArgumentParseResult.Recusar(
                    "Nenhum modo de execução informado. Use exatamente um: --validate-config, --validate-connections, --dry-run ou --run.");
            }
            if (quantosModos > 1)
            {
                return ArgumentParseResult.Recusar(
                    "Mais de um modo de execução informado — escolha exatamente um entre --validate-config, --validate-connections, --dry-run, --run.");
            }

            // --run e --dry-run realizam LEITURA REAL de ACC/SQL — os dois exigem --allow-live-read
            // explícito (Checkpoint 4.1, seção 5: nenhuma flag chamada "dry-run" pode abrir produção
            // silenciosamente). Só --validate-config/--validate-connections nunca precisam disso
            // (validate-connections também toca rede/SQL, mas com escopo de teste, não de auditoria).
            bool exigeAllowLiveRead = run || dryRun;
            if (exigeAllowLiveRead && !allowLiveRead)
            {
                string nomeFlag = run ? "--run" : "--dry-run";
                return ArgumentParseResult.Recusar(
                    $"{nomeFlag} sozinho é recusado — realiza leitura real de ACC/SQL. Use {nomeFlag} --allow-live-read.");
            }
            if (allowLiveRead && !run && !dryRun)
            {
                return ArgumentParseResult.Recusar("--allow-live-read só é válido junto de --run ou --dry-run.");
            }
            if (persist && !allowLiveRead)
            {
                return ArgumentParseResult.Recusar("--persist exige --allow-live-read explícito — nunca persistência sem leitura ao vivo confirmada.");
            }
            if (persist && !run)
            {
                return ArgumentParseResult.Recusar("--persist só é válido junto de --run --allow-live-read (nunca junto de --dry-run — dry-run nunca persiste, por definição).");
            }

            CliMode mode = validateConfig ? CliMode.ValidateConfig
                : validateConnections ? CliMode.ValidateConnections
                : dryRun ? CliMode.DryRun
                : persist ? CliMode.LivePersist
                : CliMode.LiveNoPersist;

            // Seleção de projeto — exigida para todo modo que efetivamente tocaria dados (nunca para
            // ValidateConfig, que só olha configuração). Nenhum projeto pode estar hardcoded (seção 8).
            if (mode != CliMode.ValidateConfig)
            {
                if (allProjects && !confirmAllProjects)
                {
                    return ArgumentParseResult.Recusar(
                        "--all-projects exige --confirm-all-projects explícito — nunca todos os projetos silenciosamente.");
                }
                if (allProjects && !string.IsNullOrWhiteSpace(projectId))
                {
                    return ArgumentParseResult.Recusar("--project-id e --all-projects são mutuamente exclusivos.");
                }
                if (!allProjects && string.IsNullOrWhiteSpace(projectId))
                {
                    return ArgumentParseResult.Recusar(
                        "Nenhum projeto selecionado. Informe --project-id <ProjectIdAcc>, ou --all-projects --confirm-all-projects para todos.");
                }
            }

            return ArgumentParseResult.Ok(new CliArguments
            {
                Mode = mode,
                ProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId,
                AllProjects = allProjects,
                ConfirmAllProjects = confirmAllProjects,
                AllowLiveRead = allowLiveRead,
                Persist = persist
            });
        }

        private static bool Contem(string[] args, params string[] candidatos) =>
            args.Any(a => candidatos.Any(c => string.Equals(a, c, System.StringComparison.OrdinalIgnoreCase)));

        private static string? ObterValor(string[] args, string nomeFlag)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], nomeFlag, System.StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }
    }
}
