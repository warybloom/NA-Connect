using APS.WebApp.Services;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre a classificação pura das etapas 0-17 por compatibilidade com Tipo_LDP.
    /// Correção 16/09/2026 (fase 3): Etapa 02 não tem "equivalente Tipo2" — está simplesmente
    /// desabilitada temporariamente, independente de Tipo_LDP (EtapasDesabilitadasTemporariamente,
    /// nunca EhPuladaNoTipo2). Etapa 05 não tem variante Tipo1/Tipo2 — é uma regra única, comum a
    /// qualquer Tipo_LDP (EtapasComuns). Só a Etapa 10 continua pulada especificamente no Tipo2 (layout
    /// incompatível, redesenho pendente). Sem IConfiguration/ACC/SQL, para poder ser testada em
    /// isolamento (mesmo padrão de WorkerTestSelector).
    /// </summary>
    public class WorkerStageRoutingTests
    {
        [Fact]
        public void NenhumaEtapaEPuladaPorIncompatibilidadeTipo2()
        {
            Assert.Empty(WorkerStageRouting.EtapasPuladasNoTipo2);
        }

        [Fact]
        public void ApenasEtapa02EstaDesabilitadaTemporariamente()
        {
            var id = Assert.Single(WorkerStageRouting.EtapasDesabilitadasTemporariamente).Id;
            Assert.Equal("02", id);
        }

        [Fact]
        public void Etapa02EstaDesabilitada_NuncaClassificadaComoPuladaNoTipo2OuComEquivalente()
        {
            // Etapa 02 está desligada por decisão nossa, independente de Tipo_LDP — não é
            // incompatibilidade Tipo1/Tipo2 (EhPuladaNoTipo2) nem tem um "equivalente" Tipo2
            // (TemEquivalenteTipo2): simplesmente não roda para ninguém agora.
            Assert.True(WorkerStageRouting.EhDesabilitadaTemporariamente("02"));
            Assert.False(WorkerStageRouting.EhPuladaNoTipo2("02"));
            Assert.False(WorkerStageRouting.TemEquivalenteTipo2("02"));
            Assert.False(WorkerStageRouting.EhComum("02"));
        }

        [Theory]
        [InlineData("00")]
        [InlineData("06")]
        [InlineData("09")]
        [InlineData("10")]
        [InlineData("11")]
        public void EtapasComMecanismoOriginalTipo1TemEquivalenteTipo2Reconhecido(string etapaId)
        {
            Assert.True(WorkerStageRouting.TemEquivalenteTipo2(etapaId));
            Assert.False(WorkerStageRouting.EhPuladaNoTipo2(etapaId));
            Assert.False(WorkerStageRouting.EhDesabilitadaTemporariamente(etapaId));
            Assert.False(WorkerStageRouting.EhComum(etapaId));
        }

        [Fact]
        public void Etapa10TemImplementacaoPropriaTipo2()
        {
            Assert.False(WorkerStageRouting.EhPuladaNoTipo2("10"));
            Assert.True(WorkerStageRouting.TemEquivalenteTipo2("10"));
            Assert.False(WorkerStageRouting.EhComum("10"));
        }

        [Theory]
        [InlineData("01")]
        [InlineData("04")]
        [InlineData("05")]
        [InlineData("05B")]
        [InlineData("07")]
        [InlineData("12")]
        [InlineData("13")]
        [InlineData("14")]
        [InlineData("15")]
        [InlineData("16")]
        [InlineData("17")]
        public void EtapasIndependentesDaEstruturaDaLdpSaoComuns(string etapaId)
        {
            Assert.True(WorkerStageRouting.EhComum(etapaId));
            Assert.False(WorkerStageRouting.TemEquivalenteTipo2(etapaId));
            Assert.False(WorkerStageRouting.EhPuladaNoTipo2(etapaId));
            Assert.False(WorkerStageRouting.EhDesabilitadaTemporariamente(etapaId));
        }

        [Fact]
        public void Etapa05WorkflowEstaClassificadaComoComum_NuncaTemVarianteTipo1OuTipo2()
        {
            // Correção 16/09/2026 (fase 3): não existe "Etapa 05 Tipo1" e "Etapa 05 Tipo2" — é uma só
            // regra (UpdateWorkflowStatusAsync), rodando incondicionalmente para qualquer Tipo_LDP.
            Assert.True(WorkerStageRouting.EhComum("05"));
            Assert.False(WorkerStageRouting.TemEquivalenteTipo2("05"));
        }

        [Fact]
        public void Etapa01TimesheetEstaClassificadaComoComum_GestaoAVistaNuncaASubstitui()
        {
            // Correção 17/09/2026 (fase 4, regressão funcional corrigida): não existe "Etapa 01 Tipo1" e
            // "Etapa 01 Tipo2" — é uma só regra (TimesheetDumpService.ResetAndDumpTimesheetAsync), rodando
            // incondicionalmente para qualquer Tipo_LDP. Gestão à Vista nunca é seu "equivalente".
            Assert.True(WorkerStageRouting.EhComum("01"));
            Assert.False(WorkerStageRouting.TemEquivalenteTipo2("01"));
        }

        [Fact]
        public void Etapa15WeeklyEstaClassificadaComoComum()
        {
            // Prioridade explícita do pedido: Tipo2 (CHS001) precisa alcançar
            // SincronizarWeeklyScheduleAsync — o roteamento Tipo2 nunca pode impedir a Etapa 15.
            Assert.True(WorkerStageRouting.EhComum("15"));
        }

        [Fact]
        public void Etapa9CronogramaLegadoTemEquivalenteTipo2_NuncaDuplicaCronogramaTipo2LdpSyncService()
        {
            // Etapa 9 (scheduleService.SyncScheduleToLdpAsync) faz parsing MPXJ + matching Cronograma<->LDP
            // do Tipo1 e NUNCA roda para Tipo2 — o equivalente é reconhecer que
            // CronogramaTipo2LdpSyncService (já no router) fez o mesmo trabalho conceitual.
            Assert.True(WorkerStageRouting.TemEquivalenteTipo2("09"));
        }

        [Fact]
        public void ComparacaoIgnoraCaixa()
        {
            Assert.True(WorkerStageRouting.EhComum("05b"));
            Assert.True(WorkerStageRouting.EhComum("05B"));
            Assert.True(WorkerStageRouting.TemEquivalenteTipo2("00"));
            Assert.True(WorkerStageRouting.EhDesabilitadaTemporariamente("02"));
        }

        [Fact]
        public void NenhumaEtapaApareceEmMaisDeUmaLista()
        {
            var todasComEquivalente = WorkerStageRouting.EtapasComEquivalenteTipo2.Select(e => e.Id).ToArray();
            var todasPuladas = WorkerStageRouting.EtapasPuladasNoTipo2.Select(e => e.Id).ToArray();
            var todasDesabilitadas = WorkerStageRouting.EtapasDesabilitadasTemporariamente.Select(e => e.Id).ToArray();
            var todasComuns = WorkerStageRouting.EtapasComuns.Select(e => e.Id).ToArray();

            Assert.Empty(todasComEquivalente.Intersect(todasPuladas, StringComparer.OrdinalIgnoreCase));
            Assert.Empty(todasComEquivalente.Intersect(todasComuns, StringComparer.OrdinalIgnoreCase));
            Assert.Empty(todasComEquivalente.Intersect(todasDesabilitadas, StringComparer.OrdinalIgnoreCase));
            Assert.Empty(todasPuladas.Intersect(todasComuns, StringComparer.OrdinalIgnoreCase));
            Assert.Empty(todasPuladas.Intersect(todasDesabilitadas, StringComparer.OrdinalIgnoreCase));
            Assert.Empty(todasDesabilitadas.Intersect(todasComuns, StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public void ListasCobremExatamenteAsDezesseteEtapasMapeadas()
        {
            var todasAsEtapas = WorkerStageRouting.EtapasComEquivalenteTipo2.Select(e => e.Id.ToUpperInvariant())
                .Concat(WorkerStageRouting.EtapasPuladasNoTipo2.Select(e => e.Id.ToUpperInvariant()))
                .Concat(WorkerStageRouting.EtapasDesabilitadasTemporariamente.Select(e => e.Id.ToUpperInvariant()))
                .Concat(WorkerStageRouting.EtapasComuns.Select(e => e.Id.ToUpperInvariant()))
                .OrderBy(x => x)
                .ToArray();

            Assert.Equal(new[] { "00", "01", "02", "04", "05", "05B", "06", "07", "09", "10", "11", "12", "13", "14", "15", "16", "17" }, todasAsEtapas);
        }
    }
}
