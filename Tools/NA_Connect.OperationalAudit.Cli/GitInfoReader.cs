using System;
using System.Diagnostics;

namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>Leitura best-effort de commit/estado do git para a metadata da execução (Checkpoint 4, seção 13) — nunca lança, nunca chama nenhum comando de escrita do git.</summary>
    public static class GitInfoReader
    {
        public static (string? Commit, bool? Dirty) Ler(string diretorioRepositorio)
        {
            string? commit = ExecutarGit(diretorioRepositorio, "rev-parse HEAD")?.Trim();
            string? status = ExecutarGit(diretorioRepositorio, "status --porcelain");
            bool? dirty = status == null ? (bool?)null : status.Trim().Length > 0;
            return (string.IsNullOrWhiteSpace(commit) ? null : commit, dirty);
        }

        private static string? ExecutarGit(string diretorioRepositorio, string argumentos)
        {
            try
            {
                var psi = new ProcessStartInfo("git", argumentos)
                {
                    WorkingDirectory = diretorioRepositorio,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var processo = Process.Start(psi);
                if (processo == null) return null;

                string saida = processo.StandardOutput.ReadToEnd();
                processo.WaitForExit(5000);
                return processo.ExitCode == 0 ? saida : null;
            }
            catch
            {
                return null; // git ausente/indisponível — nunca derruba o CLI por causa de metadata opcional.
            }
        }
    }
}
