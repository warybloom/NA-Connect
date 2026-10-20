using System;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Dedup;

namespace NA_Connect.OperationalAudit.Tests
{
    public class DedupHashCalculatorTests
    {
        private static OperationalFinding NovoFinding(
            string projectIdAcc = "PRJ-1",
            string etapa = "7",
            string codigoProblema = "ATR-CUSTOM-001",
            string? documentoNa = "DOC-1",
            string? documentoCliente = "CLI-1",
            string? nomeArquivo = "arquivo.pdf",
            string? pastaEncontrada = "/pasta",
            string? abaEncontrada = null,
            int? linha = null,
            string? campo = null,
            string? customAttributeName = null,
            string? motivoNormalizado = "MOTIVO",
            DateTime? dataHoraUtc = null) =>
            new OperationalFinding
            {
                ExecutionId = Guid.NewGuid(),
                DataHoraUtc = dataHoraUtc ?? DateTime.UtcNow,
                ProjectIdAcc = projectIdAcc,
                Etapa = etapa,
                CodigoProblema = codigoProblema,
                Titulo = "Titulo",
                DocumentoNa = documentoNa,
                DocumentoCliente = documentoCliente,
                NomeArquivo = nomeArquivo,
                PastaEncontrada = pastaEncontrada,
                AbaEncontrada = abaEncontrada,
                Linha = linha,
                Campo = campo,
                CustomAttributeName = customAttributeName,
                MotivoNormalizado = motivoNormalizado
            };

        [Fact]
        public void CalcularDedupHash_MesmosCamposDeIdentidade_ProduzMesmoHash_MesmoEmExecucoesDiferentes()
        {
            var f1 = NovoFinding(dataHoraUtc: new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
            var f2 = NovoFinding(dataHoraUtc: new DateTime(2026, 6, 1, 23, 59, 0, DateTimeKind.Utc));

            string h1 = DedupHashCalculator.CalcularDedupHash(f1);
            string h2 = DedupHashCalculator.CalcularDedupHash(f2);

            Assert.Equal(h1, h2);
        }

        [Theory]
        [InlineData("projectIdAcc")]
        [InlineData("codigoProblema")]
        [InlineData("documentoNa")]
        [InlineData("campo")]
        public void CalcularDedupHash_MudancaEmCampoDeIdentidade_ProduzHashDiferente(string campoAlterado)
        {
            var original = NovoFinding();
            var alterado = campoAlterado switch
            {
                "projectIdAcc" => NovoFinding(projectIdAcc: "PRJ-2"),
                "codigoProblema" => NovoFinding(codigoProblema: "ATR-CUSTOM-002"),
                "documentoNa" => NovoFinding(documentoNa: "DOC-2"),
                "campo" => NovoFinding(campo: "OutroCampo"),
                _ => throw new InvalidOperationException()
            };

            Assert.NotEqual(DedupHashCalculator.CalcularDedupHash(original), DedupHashCalculator.CalcularDedupHash(alterado));
        }

        [Fact]
        public void CalcularOccurrenceHash_ExecutionIdDiferente_ProduzHashDiferente_MesmoComMesmoFinding()
        {
            var finding = NovoFinding();
            finding.DedupHash = DedupHashCalculator.CalcularDedupHash(finding);

            string ocorrencia1 = DedupHashCalculator.CalcularOccurrenceHash(finding, Guid.NewGuid());
            string ocorrencia2 = DedupHashCalculator.CalcularOccurrenceHash(finding, Guid.NewGuid());

            Assert.NotEqual(ocorrencia1, ocorrencia2);
        }

        [Fact]
        public void CalcularOccurrenceHash_MesmaExecucaoEMesmaEvidencia_ProduzMesmoHash()
        {
            var finding = NovoFinding();
            finding.DedupHash = DedupHashCalculator.CalcularDedupHash(finding);
            var executionId = Guid.NewGuid();

            string h1 = DedupHashCalculator.CalcularOccurrenceHash(finding, executionId);
            string h2 = DedupHashCalculator.CalcularOccurrenceHash(finding, executionId);

            Assert.Equal(h1, h2);
        }

        [Fact]
        public void CalcularOccurrenceHash_SemDedupHashCalculado_Lanca()
        {
            var finding = NovoFinding();
            Assert.Null(finding.DedupHash);

            Assert.Throws<InvalidOperationException>(() => DedupHashCalculator.CalcularOccurrenceHash(finding, Guid.NewGuid()));
        }

        [Fact]
        public void CalcularDedupHash_FindingNulo_Lanca()
        {
            Assert.Throws<ArgumentNullException>(() => DedupHashCalculator.CalcularDedupHash(null!));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ConstruirRepresentacaoCanonica_NuloVazioEEspacosEmBranco_NormalizamParaMesmaRepresentacao(string? valorAusente)
        {
            string comNulo = DedupHashCalculator.ConstruirRepresentacaoCanonica(
                "P", "7", "COD", null, null, null, null, null, null, null, null, null);
            string comValorAusente = DedupHashCalculator.ConstruirRepresentacaoCanonica(
                "P", "7", "COD", valorAusente, null, null, null, null, null, null, null, null);

            Assert.Equal(comNulo, comValorAusente);
        }

        [Fact]
        public void ConstruirRepresentacaoCanonica_Normalizacao_IgnoraCaixaEEspacosNasBordas()
        {
            string a = DedupHashCalculator.ConstruirRepresentacaoCanonica(
                "prj-1", "7", "cod", "  doc-na  ", null, null, null, null, null, null, null, null);
            string b = DedupHashCalculator.ConstruirRepresentacaoCanonica(
                "PRJ-1", "7", "COD", "DOC-NA", null, null, null, null, null, null, null, null);

            Assert.Equal(a, b);
        }

        [Fact]
        public void ConstruirRepresentacaoCanonica_ConcatenacaoDeBordas_NaoColide()
        {
            // "AB" + "C" nao pode produzir a mesma representacao que "A" + "BC" so por concatenacao direta —
            // e exatamente o que o separador (US, 0x1F) existe para evitar.
            string ab_c = DedupHashCalculator.ConstruirRepresentacaoCanonica(
                "AB", "C", null, null, null, null, null, null, null, null, null, null);
            string a_bc = DedupHashCalculator.ConstruirRepresentacaoCanonica(
                "A", "BC", null, null, null, null, null, null, null, null, null, null);

            Assert.NotEqual(ab_c, a_bc);
        }
    }
}
