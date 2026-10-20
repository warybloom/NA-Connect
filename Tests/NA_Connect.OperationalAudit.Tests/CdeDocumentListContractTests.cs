using System;
using System.Linq;
using System.Text.RegularExpressions;
using APS.WebApp.Services.Reports;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// A Lista de Documentos do CDE replica a visão dos geradores de Espelho SEM alterá-los. Estes contratos
    /// leem o TEXTO-FONTE real dos geradores: se a regra de colunas do espelho mudar, o teste falha e a
    /// definição em CdeDocumentListDefinitions precisa ser revista junto. Também garante SQL só-leitura e
    /// escopo por projeto em toda instrução.
    /// </summary>
    public class CdeDocumentListContractTests
    {
        [Fact]
        public void EspelhoTipo1_ContinuaMantendoAsColunas1a15()
        {
            var fonte = RepoFileLocator.LerArquivoDaRaiz("Services", "LdpUpdateService.cs");
            int inicio = fonte.IndexOf("public async Task<string> MirrorLdpControlToProjectAsync", StringComparison.Ordinal);
            Assert.True(inicio >= 0);
            var corpo = fonte.Substring(inicio, Math.Min(4000, fonte.Length - inicio));
            Assert.Contains("if (lastCol > 15) ws.DeleteColumn(16, lastCol - 15);", corpo);
            Assert.Equal(15, CdeDocumentListDefinitions.Tipo1.Count);
        }

        [Fact]
        public void EspelhoTipo2_ContinuaOcultandoAsColunas10a17()
        {
            var fonte = RepoFileLocator.LerArquivoDaRaiz("Services", "LdpTipo2", "LdpTipo2EspelhoService.cs");
            Assert.Contains("ws.Column(col).Hidden = col >= 10 && col <= 17;", fonte);
            Assert.Contains("for (int col = 1; col <= 23; col++)", fonte);
            Assert.Equal(23 - 8, CdeDocumentListDefinitions.Tipo2.Count);
        }

        [Fact]
        public void Frontend_NaoTemColunasFixasDaLista()
        {
            var fonte = RepoFileLocator.LerArquivoDaRaiz("ClientApp", "src", "pages", "cde", "CdeDocumentList.tsx");
            Assert.Contains("/api/reports/cde/document-list", fonte);
            Assert.DoesNotContain("naDocumentNumber", fonte);
            Assert.DoesNotContain("LdpDetailsSection", fonte);
        }

        [Fact]
        public void Sql_SomenteLeituraEComEscopoDeProjeto()
        {
            var sqls = new[] { CdeDocumentListRepository.TipoQuery, CdeDocumentListRepository.Query("Sequencia ASC"), CdeDocumentListRepository.GatesQuery };
            foreach (var sql in sqls)
            {
                Assert.False(Regex.IsMatch(sql, @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE)\b", RegexOptions.IgnoreCase));
                foreach (var instrucao in sql.Split(';', StringSplitOptions.RemoveEmptyEntries).Where(s => !string.IsNullOrWhiteSpace(s)))
                    Assert.Contains("Project_ID_ACC = @projectId", instrucao);
            }
        }
    }
}
