using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>Checkpoint 4, seções 7/8/16 — combinações permitidas/negadas de flags.</summary>
    public class ArgumentParserTests
    {
        [Fact]
        public void SemArgumentos_DevolveModoAjuda()
        {
            var resultado = ArgumentParser.Parse(System.Array.Empty<string>());

            Assert.True(resultado.Sucesso);
            Assert.Equal(CliMode.Help, resultado.Argumentos!.Mode);
        }

        [Fact]
        public void FlagAjudaExplicita_DevolveModoAjuda()
        {
            var resultado = ArgumentParser.Parse(new[] { "--help" });

            Assert.True(resultado.Sucesso);
            Assert.Equal(CliMode.Help, resultado.Argumentos!.Mode);
        }

        [Fact]
        public void ValidateConfig_NaoExigeProjeto()
        {
            var resultado = ArgumentParser.Parse(new[] { "--validate-config" });

            Assert.True(resultado.Sucesso);
            Assert.Equal(CliMode.ValidateConfig, resultado.Argumentos!.Mode);
        }

        [Fact]
        public void Run_Sozinho_ERecusado()
        {
            var resultado = ArgumentParser.Parse(new[] { "--run", "--project-id", "PRJ-1" });

            Assert.False(resultado.Sucesso);
            Assert.Contains("--allow-live-read", resultado.MotivoRecusa);
        }

        [Fact]
        public void Persist_SemAllowLiveRead_ERecusado()
        {
            var resultado = ArgumentParser.Parse(new[] { "--run", "--persist", "--project-id", "PRJ-1" });

            Assert.False(resultado.Sucesso);
        }

        [Fact]
        public void Run_ComAllowLiveRead_SemPersist_PermiteModoLiveNoPersist()
        {
            var resultado = ArgumentParser.Parse(new[] { "--run", "--allow-live-read", "--project-id", "PRJ-1" });

            Assert.True(resultado.Sucesso);
            Assert.Equal(CliMode.LiveNoPersist, resultado.Argumentos!.Mode);
        }

        [Fact]
        public void Run_ComAllowLiveReadEPersist_PermiteModoLivePersist()
        {
            var resultado = ArgumentParser.Parse(new[] { "--run", "--allow-live-read", "--persist", "--project-id", "PRJ-1" });

            Assert.True(resultado.Sucesso);
            Assert.Equal(CliMode.LivePersist, resultado.Argumentos!.Mode);
        }

        [Fact]
        public void AllProjects_SemConfirmAllProjects_ERecusado()
        {
            var resultado = ArgumentParser.Parse(new[] { "--dry-run", "--allow-live-read", "--all-projects" });

            Assert.False(resultado.Sucesso);
            Assert.Contains("--confirm-all-projects", resultado.MotivoRecusa);
        }

        [Fact]
        public void AllProjects_ComConfirmAllProjects_EAceito()
        {
            var resultado = ArgumentParser.Parse(new[] { "--dry-run", "--allow-live-read", "--all-projects", "--confirm-all-projects" });

            Assert.True(resultado.Sucesso);
            Assert.True(resultado.Argumentos!.AllProjects);
        }

        [Fact]
        public void ProjectIdExplicito_SelecionaUmUnicoProjeto()
        {
            var resultado = ArgumentParser.Parse(new[] { "--dry-run", "--allow-live-read", "--project-id", "PRJ-XYZ" });

            Assert.True(resultado.Sucesso);
            Assert.Equal("PRJ-XYZ", resultado.Argumentos!.ProjectId);
            Assert.Single(resultado.Argumentos.ProjectIds());
            Assert.Equal("PRJ-XYZ", resultado.Argumentos.ProjectIds()[0]);
        }

        [Fact]
        public void DryRun_SemNenhumProjeto_ERecusado()
        {
            var resultado = ArgumentParser.Parse(new[] { "--dry-run", "--allow-live-read" });

            Assert.False(resultado.Sucesso);
        }

        [Fact]
        public void ProjectIdEAllProjects_SaoMutuamenteExclusivos()
        {
            var resultado = ArgumentParser.Parse(new[] { "--dry-run", "--allow-live-read", "--project-id", "PRJ-1", "--all-projects", "--confirm-all-projects" });

            Assert.False(resultado.Sucesso);
        }

        [Fact]
        public void MaisDeUmModo_ERecusado()
        {
            var resultado = ArgumentParser.Parse(new[] { "--dry-run", "--validate-config" });

            Assert.False(resultado.Sucesso);
        }

        [Fact]
        public void NenhumProjetoHardcoded_ProjectIdVemSempreDoArgumento()
        {
            var resultado1 = ArgumentParser.Parse(new[] { "--dry-run", "--allow-live-read", "--project-id", "PROJETO-A" });
            var resultado2 = ArgumentParser.Parse(new[] { "--dry-run", "--allow-live-read", "--project-id", "PROJETO-B" });

            Assert.Equal("PROJETO-A", resultado1.Argumentos!.ProjectId);
            Assert.Equal("PROJETO-B", resultado2.Argumentos!.ProjectId);
            Assert.NotEqual(resultado1.Argumentos.ProjectId, resultado2.Argumentos.ProjectId);
        }

        [Fact]
        public void DryRun_NuncaAceitaPersist()
        {
            var resultado = ArgumentParser.Parse(new[] { "--dry-run", "--allow-live-read", "--persist", "--project-id", "PRJ-1" });

            Assert.False(resultado.Sucesso);
        }

        // =========================================================================================
        // Checkpoint 4.1, seção 5 — --dry-run realiza leitura REAL de ACC/SQL, então exige
        // --allow-live-read exatamente como --run (nunca abre produção silenciosamente).
        // =========================================================================================

        [Fact]
        public void DryRun_Sozinho_ERecusado()
        {
            var resultado = ArgumentParser.Parse(new[] { "--dry-run", "--project-id", "PRJ-1" });

            Assert.False(resultado.Sucesso);
            Assert.Contains("--allow-live-read", resultado.MotivoRecusa);
        }

        [Fact]
        public void DryRun_ComAllowLiveRead_EPermitido()
        {
            var resultado = ArgumentParser.Parse(new[] { "--dry-run", "--allow-live-read", "--project-id", "PRJ-1" });

            Assert.True(resultado.Sucesso);
            Assert.Equal(CliMode.DryRun, resultado.Argumentos!.Mode);
        }

        [Fact]
        public void AllowLiveRead_SemRunNemDryRun_ERecusado()
        {
            var resultado = ArgumentParser.Parse(new[] { "--validate-config", "--allow-live-read" });

            Assert.False(resultado.Sucesso);
        }
    }
}
