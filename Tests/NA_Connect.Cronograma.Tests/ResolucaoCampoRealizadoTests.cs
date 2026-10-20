using net.sf.mpxj;
using NA_Connect.Cronograma.Tests.TestHelpers;

namespace NA_Connect.Cronograma.Tests
{
    /// <summary>
    /// Cobre a resolução estrutural do campo REALIZADO (seção 2 do pedido): alias exato controlado tem
    /// prioridade; um campo percentual nativo (ex.: Physical % Complete) NUNCA é aceito só por ser
    /// percentual, nem por amostra de valores — precisa de alias controlado OU título de coluna de Table
    /// comprovado.
    /// </summary>
    public class ResolucaoCampoRealizadoTests
    {
        [Fact]
        public void AliasExatoRealizado_EhEncontradoEPreferidoAoTitulo()
        {
            var project = ProjectFileTestHelper.NovoProjeto();
            ProjectFileTestHelper.DefinirAlias(project, TaskField.TEXT2, "REALIZADO");
            ProjectFileTestHelper.AdicionarTarefa(project, "DOC-001");

            var service = ServiceTestHelper.NovoAccScheduleSyncService();
            var resultado = service.ResolverConceitoPorAliasControlado(
                project, "REALIZADO", new[] { "% REALIZADO", "REALIZADO", "PERCENTUAL REALIZADO", "PHYSICAL COMPLETED", "% PHYSICAL COMPLETED", "PERCENT PHYSICAL COMPLETED" },
                "CRONOGRAMA_ALIAS_REALIZADO_NAO_ENCONTRADO", out FieldType? tipo);

            Assert.True(resultado.Encontrado);
            Assert.Equal(TaskField.TEXT2, tipo);
            Assert.Equal("REALIZADO", resultado.AliasOriginal);
        }

        [Fact]
        public void PhysicalPercentComplete_ComSomenteZeros_NaoEhEscolhidoSemAliasOuTitulo()
        {
            // Reprodução do incidente real: um campo nativo percentual (aqui simulado por TEXT5, já que
            // PHYSICAL_PERCENT_COMPLETE é nativo do MPXJ e não pode receber alias custom no teste) com
            // valores — nenhum alias controlado, nenhum título de coluna de Table — nunca deve ser aceito
            // como REALIZADO só por existir.
            var project = ProjectFileTestHelper.NovoProjeto();
            var task = ProjectFileTestHelper.AdicionarTarefa(project, "DOC-002");
            task.set(TaskField.TEXT5, "0"); // campo percentual "candidato" sem nenhuma prova estrutural

            var service = ServiceTestHelper.NovoAccScheduleSyncService();
            var resolucao = service.ResolverRealizadoComFallbackNativo(project, tipoPrevisto: null, out FieldType? tipoResolvido);

            Assert.False(resolucao.Encontrado);
            Assert.Equal("CRONOGRAMA_CAMPO_REALIZADO_NAO_COMPROVADO", resolucao.CodigoErro);
            Assert.Null(tipoResolvido);
        }

        [Fact]
        public void OrigemNaoComprovada_PreservaALdp_NuncaGrava()
        {
            // Mesma reprodução do teste acima, mas verificando explicitamente o código de erro que o
            // chamador (SyncScheduleToLdpAsync) usa para abortar ANTES de tocar na LDP.
            var project = ProjectFileTestHelper.NovoProjeto();
            ProjectFileTestHelper.AdicionarTarefa(project, "DOC-003");

            var service = ServiceTestHelper.NovoAccScheduleSyncService();
            var resolucao = service.ResolverRealizadoComFallbackNativo(project, tipoPrevisto: null, out FieldType? tipoResolvido);

            Assert.False(resolucao.Encontrado);
            Assert.Equal(APS.WebApp.Models.OrigemResolucaoCampoCronograma.NAO_ENCONTRADO, resolucao.OrigemResolucao);
        }

        [Fact]
        public void TituloDeColunaDaTable_ComprovaCampoNativoSemAlias()
        {
            // Reproduz exatamente o caso real do MPP de teste (P-0602.1-000-PM-SC-0001.mpp): TEXT3 sem
            // nenhum CustomField.Alias, mas com uma coluna de título "%Realizado" numa Table do projeto.
            var project = ProjectFileTestHelper.NovoProjeto();
            ProjectFileTestHelper.DefinirTituloColuna(project, "Entry", TaskField.TEXT3, "%Realizado");
            ProjectFileTestHelper.AdicionarTarefa(project, "DOC-004");

            var service = ServiceTestHelper.NovoAccScheduleSyncService();
            var resolucao = service.ResolverRealizadoComFallbackNativo(project, tipoPrevisto: null, out FieldType? tipoResolvido);

            Assert.True(resolucao.Encontrado);
            Assert.Equal(TaskField.TEXT3, tipoResolvido);
            Assert.Equal(APS.WebApp.Models.OrigemResolucaoCampoCronograma.TITULO_COLUNA_TABELA_COMPROVADO, resolucao.OrigemResolucao);
        }

        [Fact]
        public void DuasColunasDeTituloEquivalente_ComFieldTypesDiferentes_GeraErroAmbiguo()
        {
            var project = ProjectFileTestHelper.NovoProjeto();
            ProjectFileTestHelper.DefinirTituloColuna(project, "Entry", TaskField.TEXT3, "%Realizado");
            ProjectFileTestHelper.DefinirTituloColuna(project, "Gantt", TaskField.TEXT5, "REALIZADO");
            ProjectFileTestHelper.AdicionarTarefa(project, "DOC-005");

            var service = ServiceTestHelper.NovoAccScheduleSyncService();
            var resolucao = service.ResolverRealizadoViaTituloColunaTabela(project, tipoPrevisto: null, out FieldType? tipoResolvido);

            Assert.False(resolucao.Encontrado);
            Assert.Equal("CRONOGRAMA_TITULO_COLUNA_MULTIPLO", resolucao.CodigoErro);
            Assert.Null(tipoResolvido);
        }
    }
}
