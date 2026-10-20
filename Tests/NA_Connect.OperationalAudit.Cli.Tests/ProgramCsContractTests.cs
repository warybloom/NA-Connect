using System;
using System.IO;
using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>
    /// Checkpoint 4.1 (verificação final, seção 2): prova, contra o TEXTO-FONTE REAL de
    /// <c>Tools\NA_Connect.OperationalAudit.Cli\Program.cs</c> (nunca um teste unitário isolado dos
    /// métodos), que a cadeia ArgumentParser -&gt; CliModeOptionsApplier.Aplicar -&gt;
    /// CliModeOptionsApplier.GarantirHabilitadoParaExecucao -&gt; IOperationalAuditRunner.ExecutarAsync
    /// existe, em código ALCANÇÁVEL (nunca depois de um <c>return</c> anterior no mesmo bloco), e nessa
    /// ordem exata dentro do método que efetivamente lida com os modos ao vivo.
    /// </summary>
    public class ProgramCsContractTests
    {
        private static string LocalizarRaizDoRepositorio()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccIntegration.WebApp.sln")))
            {
                dir = dir.Parent;
            }
            if (dir == null) throw new InvalidOperationException("Raiz do repositório não encontrada.");
            return dir.FullName;
        }

        private static string LerProgramCsDoCli() =>
            File.ReadAllText(Path.Combine(LocalizarRaizDoRepositorio(), "Tools", "NA_Connect.OperationalAudit.Cli", "Program.cs"));

        private static string ExtrairCorpoDoMetodo(string codigoFonte, string assinaturaDoMetodo)
        {
            int inicio = codigoFonte.IndexOf(assinaturaDoMetodo, StringComparison.Ordinal);
            Assert.True(inicio >= 0, $"Assinatura '{assinaturaDoMetodo}' não encontrada em Program.cs.");

            int chaveAbre = codigoFonte.IndexOf('{', inicio);
            Assert.True(chaveAbre >= 0);

            int profundidade = 0;
            int i = chaveAbre;
            for (; i < codigoFonte.Length; i++)
            {
                if (codigoFonte[i] == '{') profundidade++;
                else if (codigoFonte[i] == '}')
                {
                    profundidade--;
                    if (profundidade == 0) break;
                }
            }
            Assert.True(profundidade == 0, "Chave de fechamento do método não encontrada (contagem desbalanceada).");
            return codigoFonte.Substring(chaveAbre, i - chaveAbre + 1);
        }

        [Fact]
        public void ExecutarAuditoriaAoVivoAsync_Existe_EEChamadoPelosTresModosAoVivo()
        {
            string codigoFonte = LerProgramCsDoCli();

            Assert.Contains("async Task<int> ExecutarAuditoriaAoVivoAsync(", codigoFonte);

            // Os três modos ao vivo (DryRun/LiveNoPersist/LivePersist) caem no MESMO case do switch que
            // chama ExecutarAuditoriaAoVivoAsync — nunca um caminho duplicado por modo.
            int indiceCaseDryRun = codigoFonte.IndexOf("case CliMode.DryRun:", StringComparison.Ordinal);
            int indiceChamada = codigoFonte.IndexOf("ExecutarAuditoriaAoVivoAsync(configuration, cliArgs, metadata, cts.Token)", StringComparison.Ordinal);
            Assert.True(indiceCaseDryRun >= 0);
            Assert.True(indiceChamada > indiceCaseDryRun);
        }

        [Fact]
        public void CadeiaReal_ApplicarAntesDeGarantirHabilitado_AntesDeExecutarAsync_NaOrdemCorreta()
        {
            string codigoFonte = LerProgramCsDoCli();
            string corpo = ExtrairCorpoDoMetodo(codigoFonte, "async Task<int> ExecutarAuditoriaAoVivoAsync(");

            int indiceAplicar = corpo.IndexOf("CliModeOptionsApplier.Aplicar(options, argumentos);", StringComparison.Ordinal);
            int indiceGarantir = corpo.IndexOf("CliModeOptionsApplier.GarantirHabilitadoParaExecucao(options);", StringComparison.Ordinal);
            int indiceExecutarAsync = corpo.IndexOf(".ExecutarAsync(argumentos.ProjectIds()", StringComparison.Ordinal);

            Assert.True(indiceAplicar >= 0, "Aplicar não encontrado dentro de ExecutarAuditoriaAoVivoAsync.");
            Assert.True(indiceGarantir >= 0, "GarantirHabilitadoParaExecucao não encontrado dentro de ExecutarAuditoriaAoVivoAsync.");
            Assert.True(indiceExecutarAsync >= 0, "ExecutarAsync não encontrado dentro de ExecutarAuditoriaAoVivoAsync.");

            Assert.True(indiceAplicar < indiceGarantir, "Aplicar deveria vir ANTES de GarantirHabilitadoParaExecucao.");
            Assert.True(indiceGarantir < indiceExecutarAsync, "GarantirHabilitadoParaExecucao deveria vir ANTES de ExecutarAsync.");
        }

        [Fact]
        public void CadeiaReal_NuncaEstaDepoisDeUmReturnAnterior_NoMesmoBloco()
        {
            // Garante que a cadeia não é código morto: nenhum "return" aparece ANTES dela dentro do mesmo
            // corpo de método (o que a tornaria inalcançável).
            string codigoFonte = LerProgramCsDoCli();
            string corpo = ExtrairCorpoDoMetodo(codigoFonte, "async Task<int> ExecutarAuditoriaAoVivoAsync(");

            int indiceAplicar = corpo.IndexOf("CliModeOptionsApplier.Aplicar(options, argumentos);", StringComparison.Ordinal);
            string antesDeAplicar = corpo.Substring(0, indiceAplicar);

            Assert.DoesNotContain("return", antesDeAplicar, StringComparison.Ordinal);
        }

        [Fact]
        public void NenhumDosTrezeArquivosProtegidosEReferenciadoEmProgramCs()
        {
            string codigoFonte = LerProgramCsDoCli();

            foreach (var caminho in ProtectedFilesList.RelativePaths)
            {
                string nomeDaClasse = Path.GetFileNameWithoutExtension(caminho);
                Assert.DoesNotContain(nomeDaClasse, codigoFonte);
            }
        }

        [Fact]
        public void ExecutarValidateConnectionsAsync_Existe_EEChamadoPeloCaseValidateConnections()
        {
            string codigoFonte = LerProgramCsDoCli();

            Assert.Contains("async Task<int> ExecutarValidateConnectionsAsync(", codigoFonte);

            int indiceCase = codigoFonte.IndexOf("case CliMode.ValidateConnections:", StringComparison.Ordinal);
            int indiceChamada = codigoFonte.IndexOf("ExecutarValidateConnectionsAsync(configuration, cliArgs, metadata, cts.Token)", StringComparison.Ordinal);
            Assert.True(indiceCase >= 0);
            Assert.True(indiceChamada > indiceCase, "ExecutarValidateConnectionsAsync deveria ser chamado dentro do case ValidateConnections.");
        }

        [Fact]
        public void ExecutarValidateConnectionsAsync_NuncaChamaOOperationalAuditRunner()
        {
            // Checkpoint 5, seção 2: "Nunca, em hipótese alguma, chamar OperationalAuditRunner dentro de
            // --validate-connections." Prova contra o texto-fonte real do método, não um teste isolado.
            string codigoFonte = LerProgramCsDoCli();
            string corpo = ExtrairCorpoDoMetodo(codigoFonte, "async Task<int> ExecutarValidateConnectionsAsync(");

            Assert.DoesNotContain("IOperationalAuditRunner", corpo);
            Assert.DoesNotContain(".ExecutarAsync(argumentos.ProjectIds()", corpo);
        }

        [Fact]
        public void ExecutarValidateConnectionsAsync_SeguePelaOrdemDaSecao2()
        {
            string codigoFonte = LerProgramCsDoCli();
            string corpo = ExtrairCorpoDoMetodo(codigoFonte, "async Task<int> ExecutarValidateConnectionsAsync(");

            int indiceAuditSource = corpo.IndexOf("GetConnectionString(\"AuditSourceConnection\")", StringComparison.Ordinal);
            int indiceAuditRepository = corpo.IndexOf("GetConnectionString(\"AuditRepositoryConnection\")", StringComparison.Ordinal);
            int indiceValidarTudo = corpo.IndexOf("ConnectionValidator.ValidarTudoAsync(", StringComparison.Ordinal);
            int indiceSchema = corpo.IndexOf("SchemaReadOnlyValidator.ValidarAsync(", StringComparison.Ordinal);
            int indicePermissoes = corpo.IndexOf("PermissionReadOnlyValidator.ValidarPermissoesDeBancoAsync(", StringComparison.Ordinal);

            Assert.True(indiceAuditSource >= 0);
            Assert.True(indiceAuditRepository >= 0);
            Assert.True(indiceValidarTudo >= 0);
            Assert.True(indiceSchema >= 0);
            Assert.True(indicePermissoes >= 0);

            Assert.True(indiceAuditSource < indiceValidarTudo, "AuditSourceConnection deveria ser lida antes de ConnectionValidator.ValidarTudoAsync.");
            Assert.True(indiceAuditRepository < indiceValidarTudo, "AuditRepositoryConnection deveria ser lida antes de ConnectionValidator.ValidarTudoAsync.");
            Assert.True(indiceValidarTudo < indiceSchema, "ValidarTudoAsync (as 4 checagens) deveria vir antes da validação de schema.");
            Assert.True(indiceSchema < indicePermissoes, "Validação de schema deveria vir antes da validação de permissões.");
        }

        [Fact]
        public void ExecutarValidateConnectionsAsync_NuncaImprimeConnectionStringCompleta()
        {
            string codigoFonte = LerProgramCsDoCli();
            string corpo = ExtrairCorpoDoMetodo(codigoFonte, "async Task<int> ExecutarValidateConnectionsAsync(");

            // Só Server/Database/Credentials=REDACTED via ImprimirResumoConexaoRedigido — nunca
            // Console.WriteLine(auditSourceConnectionString) nem interpolação bruta da connection string.
            Assert.DoesNotContain("Console.WriteLine(auditSourceConnectionString", corpo);
            Assert.DoesNotContain("Console.WriteLine(auditRepositoryConnectionString", corpo);
            Assert.DoesNotContain("Console.WriteLine($\"{auditSourceConnectionString", corpo);
            Assert.DoesNotContain("Console.WriteLine($\"{auditRepositoryConnectionString", corpo);
            Assert.Contains("ImprimirResumoConexaoRedigido(auditSourceConnectionString)", corpo);
            Assert.Contains("ImprimirResumoConexaoRedigido(auditRepositoryConnectionString)", corpo);
            Assert.Contains("Token = NUNCA EXIBIR", corpo);
        }

        [Fact]
        public void NenhumaChamadaAoDotnetRunComFlagsAoVivoFoiFeitaNesteRepositorio()
        {
            // Não existe log/histórico de execução real no repositório (nenhum arquivo de saída da
            // auditoria fora daqueles gerados por teste em diretório temporário) — verificação estrutural:
            // AuditReports só deve conter a subpasta _validation (hashes), nunca uma subpasta de execução
            // real de auditoria (que só ExecutarAuditoriaAoVivoAsync criaria, e nunca foi chamado).
            string raiz = LocalizarRaizDoRepositorio();
            string auditReports = Path.Combine(raiz, "AuditReports");
            if (!Directory.Exists(auditReports)) return; // nem a pasta existe ainda -> nada foi executado.

            var subpastas = Directory.GetDirectories(auditReports);
            Assert.All(subpastas, p => Assert.Equal("_validation", Path.GetFileName(p)));
        }
    }
}
