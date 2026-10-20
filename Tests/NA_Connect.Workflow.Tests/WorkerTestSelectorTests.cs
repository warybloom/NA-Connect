using APS.WebApp.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre o seletor genérico de etapas do WorkerTest (seção 16/20 do pedido de correção da Etapa 7) —
    /// lógica pura, sem IConfiguration/ACC/SQL, para poder ser testada em isolamento.
    /// </summary>
    public class WorkerTestSelectorTests
    {
        // 1/2 — Enabled=true e Etapas=["7"] executa somente a Etapa 7, nenhuma outra etapa é iniciada.
        [Fact]
        public void EnabledComEtapas7_ExecutaSomenteEtapa7()
        {
            var config = new WorkerTestConfig { Enabled = true, ProjectId = "b.projeto", Etapas = new List<string> { "7" } };

            Assert.True(WorkerTestSelector.DeveExecutarEtapa(config, "7"));
            Assert.False(WorkerTestSelector.DeveExecutarEtapa(config, "0"));
            Assert.False(WorkerTestSelector.DeveExecutarEtapa(config, "11"));
            Assert.False(WorkerTestSelector.DeveExecutarEtapa(config, "12"));
            Assert.False(WorkerTestSelector.DeveExecutarEtapa(config, "17"));
        }

        [Fact]
        public void DeveExecutarEtapa_ComparacaoIgnoraCaixaEAceitaIdAlfanumerico()
        {
            var config = new WorkerTestConfig { Enabled = true, ProjectId = "b.projeto", Etapas = new List<string> { "5B" } };

            Assert.True(WorkerTestSelector.DeveExecutarEtapa(config, "5b"));
            Assert.False(WorkerTestSelector.DeveExecutarEtapa(config, "7"));
        }

        // 3 — ProjectId vazio NÃO aborta mais (correção 17/09/2026): passou a significar "todos os
        // projetos Is_Ativo=1", restringindo só as Etapas. Validar só depende de haver ao menos uma
        // Etapa quando Enabled=true.
        [Fact]
        public void Validar_EnabledComProjectIdVazio_NaoAborta_SignificaTodosOsProjetosAtivos()
        {
            var config = new WorkerTestConfig { Enabled = true, ProjectId = "", Etapas = new List<string> { "01", "03" } };
            var (valido, erro) = WorkerTestSelector.Validar(config);

            Assert.True(valido);
            Assert.Null(erro);
        }

        [Fact]
        public void Validar_EnabledComProjectIdNulo_NaoAborta()
        {
            var config = new WorkerTestConfig { Enabled = true, ProjectId = null, Etapas = new List<string> { "01", "03" } };
            var (valido, erro) = WorkerTestSelector.Validar(config);

            Assert.True(valido);
            Assert.Null(erro);
        }

        // 4 — Etapas vazia aborta o teste.
        [Fact]
        public void Validar_EnabledSemEtapas_Aborta()
        {
            var config = new WorkerTestConfig { Enabled = true, ProjectId = "b.projeto", Etapas = new List<string>() };
            var (valido, erro) = WorkerTestSelector.Validar(config);

            Assert.False(valido);
            Assert.NotNull(erro);
        }

        // 5 — Enabled=false executa o fluxo normal (todas as etapas, WorkerTest:Etapas ignorada).
        [Fact]
        public void Disabled_ExecutaTodasAsEtapas_IgnorandoEtapasConfiguradas()
        {
            var config = new WorkerTestConfig { Enabled = false, Etapas = new List<string> { "7" } };

            Assert.True(WorkerTestSelector.DeveExecutarEtapa(config, "0"));
            Assert.True(WorkerTestSelector.DeveExecutarEtapa(config, "11"));
            Assert.True(WorkerTestSelector.DeveExecutarEtapa(config, "17"));

            var (valido, erro) = WorkerTestSelector.Validar(config);
            Assert.True(valido);
            Assert.Null(erro);
        }

        // 6 — ExecutarUmaVez=true impede repetição no mesmo processo.
        [Fact]
        public void ExecutarUmaVez_ImpedeRepeticaoNoMesmoProcesso()
        {
            var config = new WorkerTestConfig { Enabled = true, ProjectId = "b.projeto", Etapas = new List<string> { "7" }, ExecutarUmaVez = true };

            Assert.False(WorkerTestSelector.DeveBloquearRepeticao(config, jaExecutouNesteProcesso: false));
            Assert.True(WorkerTestSelector.DeveBloquearRepeticao(config, jaExecutouNesteProcesso: true));
        }

        [Fact]
        public void ExecutarUmaVezFalso_NuncaBloqueiaRepeticao()
        {
            var config = new WorkerTestConfig { Enabled = true, ProjectId = "b.projeto", Etapas = new List<string> { "7" }, ExecutarUmaVez = false };

            Assert.False(WorkerTestSelector.DeveBloquearRepeticao(config, jaExecutouNesteProcesso: true));
        }

        // 7 — não foi criada flag SomenteEtapa7: WorkerTestConfig só expõe Enabled/ProjectId/Etapas/ExecutarUmaVez.
        [Fact]
        public void WorkerTestConfig_SoExpoeAsQuatroPropriedadesEsperadas()
        {
            var propriedades = typeof(WorkerTestConfig).GetProperties().Select(p => p.Name).OrderBy(n => n).ToList();
            Assert.Equal(new[] { "Enabled", "Etapas", "ExecutarUmaVez", "ProjectId" }, propriedades);
        }
    }
}
