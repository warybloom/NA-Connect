using APS.WebApp.Models;
using APS.WebApp.Services;
using NA_Connect.Cronograma.Tests.TestHelpers;
using net.sf.mpxj;
using OfficeOpenXml;

namespace NA_Connect.Cronograma.Tests
{
    /// <summary>
    /// Etapa 09 (Cronograma Tipo1 -> LDP): identidade documental = NA DOCUMENT NUMBER (cronograma, TEXT1)
    /// ↔ N&amp;A DOCUMENT NUMBER (LDP). Nenhum arquivo físico do ACC participa: o serviço é construído sem
    /// ACC alcançável e o pipeline FASE A (PrepararRegistrosSql -> PrepararAlteracoesLdp) roda inteiro em
    /// memória. Reproduz o MPP real do BML011.016: subtarefas de atividade SEM NA DOCUMENT NUMBER
    /// ("Preparation", "Issue"...) repetidas sob cada documento com valores diferentes — antes viravam o
    /// pseudo-documento "PREPARATION" e bloqueavam a Etapa 09 inteira com
    /// CRONOGRAMA_DOCUMENTO_DUPLICADO_REALIZADO_CONFLITANTE.
    /// </summary>
    public class CronogramaLdpMatchSemArquivoFisicoTests
    {
        private const string Doc3dm001 = "BML011.016-212-3DM-3D-001";
        private const string Doc3dm002 = "BML011.016-212-3DM-3D-002";
        private const string DocPl0 = "BML011.016-212-PL0-LD-001";

        private static readonly FieldType CampoPrevisto = TaskField.TEXT2;
        private static readonly FieldType CampoRealizado = TaskField.TEXT3;

        private static int _proximoId = 1;

        private static void AdicionarFolha(ProjectFile p, string? naDocumentNumber, string nome, string previsto, string realizado)
        {
            var t = p.addTask();
            t.setID(java.lang.Integer.valueOf(_proximoId++));
            t.setName(nome);
            if (naDocumentNumber != null) t.set(TaskField.TEXT1, naDocumentNumber);
            t.set(CampoPrevisto, previsto);
            t.set(CampoRealizado, realizado);
        }

        private static ProjectFile CronogramaReal()
        {
            var p = ProjectFileTestHelper.NovoProjeto();
            AdicionarFolha(p, DocPl0, "Lista de documentos", "100%", "100%");
            AdicionarFolha(p, Doc3dm001, "Modelo 3D 001", "100%", "100%");
            AdicionarFolha(p, Doc3dm002, "Modelo 3D 002", "100%", "100%");
            // Documento só do cronograma (não existe na LDP) — SEM_MATCH_LDP, nunca inventa linha.
            AdicionarFolha(p, "BML011.016-999-XXX-XX-999", "Fora da LDP", "50%", "40%");
            // Subtarefas de atividade sem NA DOCUMENT NUMBER, valores conflitantes entre si.
            AdicionarFolha(p, null, " Preparation", "100%", "100%");
            AdicionarFolha(p, null, " Preparation", "100%", "30%");
            AdicionarFolha(p, null, " Issue", "100%", "100%");
            AdicionarFolha(p, null, " Issue", "80%", "50%");
            return p;
        }

        private static ExcelPackage LdpComPercentuaisVazios(params string[] documentos)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            var pkg = new ExcelPackage();
            var ws = pkg.Workbook.Worksheets.Add("LDP");
            ws.Cells[7, 3].Value = "CLIENT DOCUMENT NUMBER";
            ws.Cells[7, 4].Value = "N&A DOCUMENT NUMBER";
            ws.Cells[7, 20].Value = "%PLANNED PHYSICAL";
            ws.Cells[7, 22].Value = "%PHYSICAL COMPLETED";
            int linha = 8;
            foreach (var doc in documentos)
            {
                ws.Cells[linha, 3].Value = "212-R-M3D-" + linha.ToString("0000"); // padrão de cliente diferente
                ws.Cells[linha, 4].Value = doc;
                linha++;
            }
            return pkg;
        }

        private static PlanoSincronizacaoCronogramaTipo1 ExecutarFaseA(ExcelWorksheet ws)
        {
            var service = ServiceTestHelper.NovoAccScheduleSyncService();
            var plano = new PlanoSincronizacaoCronogramaTipo1();
            var dados = new Dictionary<string, AccScheduleSyncService.MppScheduleRowData>(StringComparer.OrdinalIgnoreCase);

            service.PrepararRegistrosSql("b.teste", CronogramaReal(), "teste.mpp", CampoRealizado, CampoPrevisto, TaskField.FINISH,
                new Dictionary<string, NA_Connect.Models.ColunaEsquemaSql>(), plano, dados);
            plano.ErrosValidacao += plano.DocumentosDuplicadosConflitantes.Count; // mesmo passo do chamador real

            var ldpCols = service.MapLdpHeaders(ws);
            int colRealizado = service.ResolverColunaLdpPhysicalCompleted(ws, ldpCols, "b.teste");
            service.PrepararAlteracoesLdp(ws, ldpCols, colRealizado, dados, plano,
                new List<(int Linha, int Coluna, string Valor, string Campo)>(), CampoRealizado.ToString());
            return plano;
        }

        private static void AssertPercentuaisAplicados(PlanoSincronizacaoCronogramaTipo1 plano, string documento)
        {
            var realizado = Assert.Single(plano.AlteracoesLdpPreparadas, a => a.Documento == documento && a.Campo == "%PHYSICAL COMPLETED");
            var previsto = Assert.Single(plano.AlteracoesLdpPreparadas, a => a.Documento == documento && a.Campo == "%PLANNED PHYSICAL");

            Assert.Equal(100m, realizado.ValorNovo);
            Assert.True(realizado.DeveSerAplicada);
            Assert.Equal(100m, previsto.ValorNovo);
            Assert.True(previsto.DeveSerAplicada);
        }

        [Fact]
        public void DocumentoExisteNoCronogramaELdp_SemArquivoFisicoAcc_DeveAtualizarPercentual()
        {
            using var ldp = LdpComPercentuaisVazios(Doc3dm001);
            var plano = ExecutarFaseA(ldp.Workbook.Worksheets[0]);

            AssertPercentuaisAplicados(plano, Doc3dm001);
            Assert.Empty(plano.DocumentosDuplicadosConflitantes);
            Assert.True(plano.PodePersistir, "A proposta não pode ser bloqueada — nenhum arquivo físico participa do matching.");
        }

        [Fact]
        public void DocumentoExisteNoCronogramaELdp_SemArquivoFisicoAcc_DeveAtualizarPercentual_3dm002()
        {
            using var ldp = LdpComPercentuaisVazios(Doc3dm002);
            var plano = ExecutarFaseA(ldp.Workbook.Worksheets[0]);

            AssertPercentuaisAplicados(plano, Doc3dm002);
            Assert.True(plano.PodePersistir);
        }

        [Fact]
        public void TresDocumentosReais_SemArquivoFisicoAcc_TodosAtualizados_EtapaNaoBloqueada()
        {
            using var ldp = LdpComPercentuaisVazios(DocPl0, Doc3dm001, Doc3dm002);
            var plano = ExecutarFaseA(ldp.Workbook.Worksheets[0]);

            AssertPercentuaisAplicados(plano, DocPl0);
            AssertPercentuaisAplicados(plano, Doc3dm001);
            AssertPercentuaisAplicados(plano, Doc3dm002);
            Assert.Equal(3, plano.LinhasLdpCorrespondentes);
            Assert.Empty(plano.DocumentosDuplicadosConflitantes);
            Assert.Equal(0, plano.ErrosValidacao);
            Assert.True(plano.PodePersistir);
        }

        [Fact]
        public void DocumentoDoCronogramaAusenteNaLdp_NaoInventaLinha()
        {
            using var ldp = LdpComPercentuaisVazios(Doc3dm001);
            var plano = ExecutarFaseA(ldp.Workbook.Worksheets[0]);

            Assert.DoesNotContain(plano.AlteracoesLdpPreparadas, a => a.Documento.Contains("999"));
            Assert.Equal(1, plano.LinhasLdpCorrespondentes);
        }

        [Fact]
        public void SubtarefaSemNaDocumentNumber_QueCasaComNaDaLdp_ContinuaBloqueando()
        {
            // Proteção preservada: se a chave de nome conflitante for de fato um N&A da LDP, é erro real
            // de matching Cronograma ↔ LDP.
            using var ldp = LdpComPercentuaisVazios(Doc3dm001, "Preparation");
            var plano = ExecutarFaseA(ldp.Workbook.Worksheets[0]);

            Assert.Single(plano.DocumentosDuplicadosConflitantes);
            Assert.False(plano.PodePersistir);
        }

        [Fact]
        public void DuplicataRealComNaDocumentNumber_ContinuaBloqueando()
        {
            var service = ServiceTestHelper.NovoAccScheduleSyncService();
            var p = ProjectFileTestHelper.NovoProjeto();
            AdicionarFolha(p, Doc3dm001, "A", "100%", "100%");
            AdicionarFolha(p, Doc3dm001, "B", "100%", "40%");
            var plano = new PlanoSincronizacaoCronogramaTipo1();
            var dados = new Dictionary<string, AccScheduleSyncService.MppScheduleRowData>(StringComparer.OrdinalIgnoreCase);

            service.PrepararRegistrosSql("b.teste", p, "teste.mpp", CampoRealizado, CampoPrevisto, TaskField.FINISH,
                new Dictionary<string, NA_Connect.Models.ColunaEsquemaSql>(), plano, dados);

            Assert.Single(plano.DocumentosDuplicadosConflitantes);
            Assert.Empty(plano.ConflitosNomeTarefaSemNaDocumentNumber);
        }

        [Fact]
        public void ColunaChave_EhNaDocumentNumber_MesmoComClientDocumentNumberAntes()
        {
            var cols = new Dictionary<int, string> { [3] = "CLIENT DOCUMENT NUMBER", [4] = "N&A DOCUMENT NUMBER" };
            Assert.Equal(4, AccScheduleSyncService.ResolverColunaChaveLdp(cols));
        }
    }
}
