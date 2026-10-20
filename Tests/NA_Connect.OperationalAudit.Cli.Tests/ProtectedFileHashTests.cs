using System;
using System.Collections.Generic;
using System.IO;
using NA_Connect.OperationalAudit.Cli;

namespace NA_Connect.OperationalAudit.Cli.Tests
{
    /// <summary>
    /// Checkpoint 4.1, seção 1 — usa SOMENTE arquivos temporários fake (nunca os 13 arquivos protegidos
    /// reais) para provar o mecanismo MATCH/MISMATCH/MISSING_BASELINE.
    /// </summary>
    public class ProtectedFileHashTests : IDisposable
    {
        private readonly string _diretorioTemp = Path.Combine(Path.GetTempPath(), "NA_Connect_HashTests_" + Guid.NewGuid().ToString("N"));

        public ProtectedFileHashTests()
        {
            Directory.CreateDirectory(_diretorioTemp);
        }

        public void Dispose()
        {
            if (Directory.Exists(_diretorioTemp)) Directory.Delete(_diretorioTemp, recursive: true);
        }

        private string CriarArquivoFake(string nome, string conteudo)
        {
            string caminho = Path.Combine(_diretorioTemp, nome);
            File.WriteAllText(caminho, conteudo);
            return caminho;
        }

        [Fact]
        public void ComputeHash_MesmoConteudo_MesmoHash()
        {
            string arquivo1 = CriarArquivoFake("a.cs", "conteudo identico");
            string arquivo2 = CriarArquivoFake("b.cs", "conteudo identico");

            Assert.Equal(Sha256FileHasher.ComputeHash(arquivo1), Sha256FileHasher.ComputeHash(arquivo2));
        }

        [Fact]
        public void ComputeHash_ConteudoDiferente_HashDiferente()
        {
            string arquivo1 = CriarArquivoFake("a.cs", "conteudo A");
            string arquivo2 = CriarArquivoFake("b.cs", "conteudo B");

            Assert.NotEqual(Sha256FileHasher.ComputeHash(arquivo1), Sha256FileHasher.ComputeHash(arquivo2));
        }

        [Fact]
        public void ReadBaseline_ArquivoAusente_DevolveNull()
        {
            string caminhoInexistente = Path.Combine(_diretorioTemp, "nao_existe.txt");

            var baseline = Sha256FileHasher.ReadBaseline(caminhoInexistente);

            Assert.Null(baseline);
        }

        [Fact]
        public void WriteBaseline_DepoisReadBaseline_DevolveOMesmoConteudo()
        {
            string arquivoFake = CriarArquivoFake("FakeProtected.cs", "conteudo original protegido");
            string hash = Sha256FileHasher.ComputeHash(arquivoFake);
            string caminhoBaseline = Path.Combine(_diretorioTemp, "_validation", "baseline.txt");

            Sha256FileHasher.WriteBaseline(caminhoBaseline, new Dictionary<string, string> { ["Services/FakeProtected.cs"] = hash });
            var lido = Sha256FileHasher.ReadBaseline(caminhoBaseline);

            Assert.NotNull(lido);
            Assert.Equal(hash, lido!["Services/FakeProtected.cs"]);
        }

        [Fact]
        public void Compare_BaselineAusente_TodosViramMissingBaseline_NuncaMatch()
        {
            var atual = new Dictionary<string, string> { ["Services/FakeProtected.cs"] = "abc123" };

            var relatorio = CompareProtectedFileHashes.Compare(null, atual);

            Assert.False(relatorio.TudoIgual);
            Assert.All(relatorio.Checagens, c => Assert.Equal(ProtectedFileHashStatus.MissingBaseline, c.Status));
        }

        [Fact]
        public void Compare_ArquivoAlterado_Mismatch()
        {
            string arquivoFake = CriarArquivoFake("FakeProtected.cs", "conteudo original");
            string hashOriginal = Sha256FileHasher.ComputeHash(arquivoFake);
            var baseline = new Dictionary<string, string> { ["Services/FakeProtected.cs"] = hashOriginal };

            File.WriteAllText(arquivoFake, "conteudo ALTERADO — simulação de interferência");
            string hashAlterado = Sha256FileHasher.ComputeHash(arquivoFake);
            var atual = new Dictionary<string, string> { ["Services/FakeProtected.cs"] = hashAlterado };

            var relatorio = CompareProtectedFileHashes.Compare(baseline, atual);

            Assert.False(relatorio.TudoIgual);
            var checagem = Assert.Single(relatorio.Checagens);
            Assert.Equal(ProtectedFileHashStatus.Mismatch, checagem.Status);
        }

        [Fact]
        public void Compare_ArquivoIntacto_Match()
        {
            string arquivoFake = CriarArquivoFake("FakeProtected.cs", "conteudo que nunca muda");
            string hash = Sha256FileHasher.ComputeHash(arquivoFake);
            var baseline = new Dictionary<string, string> { ["Services/FakeProtected.cs"] = hash };
            var atual = new Dictionary<string, string> { ["Services/FakeProtected.cs"] = hash };

            var relatorio = CompareProtectedFileHashes.Compare(baseline, atual);

            Assert.True(relatorio.TudoIgual);
            var checagem = Assert.Single(relatorio.Checagens);
            Assert.Equal(ProtectedFileHashStatus.Match, checagem.Status);
        }

        [Fact]
        public void Compare_MisturaDeArquivos_CadaUmComSeuProprioStatus()
        {
            var baseline = new Dictionary<string, string>
            {
                ["A.cs"] = "hash-a",
                ["B.cs"] = "hash-b-original"
                // C.cs propositalmente ausente do baseline.
            };
            var atual = new Dictionary<string, string>
            {
                ["A.cs"] = "hash-a", // igual -> Match
                ["B.cs"] = "hash-b-MUDOU", // diferente -> Mismatch
                ["C.cs"] = "hash-c" // sem baseline -> MissingBaseline
            };

            var relatorio = CompareProtectedFileHashes.Compare(baseline, atual);

            Assert.False(relatorio.TudoIgual);
            Assert.Equal(ProtectedFileHashStatus.Match, Buscar(relatorio, "A.cs").Status);
            Assert.Equal(ProtectedFileHashStatus.Mismatch, Buscar(relatorio, "B.cs").Status);
            Assert.Equal(ProtectedFileHashStatus.MissingBaseline, Buscar(relatorio, "C.cs").Status);
        }

        [Fact]
        public void ListaDosTrezeArquivosProtegidos_TemExatamente13Entradas_NuncaMudaSemIntencao()
        {
            Assert.Equal(13, ProtectedFilesList.RelativePaths.Count);
            Assert.Contains("Services/GrdAutomationWorker.cs", ProtectedFilesList.RelativePaths);
            Assert.Contains("Services/LdpUpdateService.cs", ProtectedFilesList.RelativePaths);
            Assert.Contains("Services/TimesheetDumpService.cs", ProtectedFilesList.RelativePaths);
        }

        private static ProtectedFileHashCheck Buscar(ProtectedFileHashReport relatorio, string nome) =>
            System.Linq.Enumerable.Single(relatorio.Checagens, c => c.Nome == nome);
    }
}
