using APS.WebApp.Services;
using Newtonsoft.Json.Linq;

namespace NA_Connect.Workflow.Tests
{
    /// <summary>
    /// Cobre a extração aditiva de etapa/responsável/vencimento (funcionalidade "ACC_REVIEW_STAGE") —
    /// AccReviewWorkflowService.ExtrairResponsavelEtapa / ExtrairVencimentoEtapa / ConstruirMetadadosEtapaReview.
    /// Os payloads reproduzem os JSON Paths comprovados contra o Review 603 real (PACOTE EMISSÃO P&ID
    /// AREA 900, projeto BML011.013): currentStepId=Lane_4I2MAOQouW resolvido como "CHECKER",
    /// nextActionBy.candidates.users=[Roberto Andrei], nextActionBy.claimedBy=[] (vazio em 100% dos 134
    /// Reviews reais do projeto), currentStepDueDate="2026-07-31T17:41:51.453Z" (= 2026-07-31 14:41 em
    /// America/Sao_Paulo). Nunca testa/altera Status_In_Flow.
    /// </summary>
    public class AccReviewStageMetadataTests
    {
        private static JObject NextActionBy(JArray? claimedBy, JArray? candidateUsers)
        {
            return new JObject
            {
                ["nextActionBy"] = new JObject
                {
                    ["claimedBy"] = claimedBy ?? new JArray(),
                    ["candidates"] = new JObject
                    {
                        ["roles"] = new JArray(),
                        ["users"] = candidateUsers ?? new JArray(),
                        ["companies"] = new JArray()
                    }
                }
            };
        }

        private static JObject Usuario(string nome, string autodeskId) =>
            new JObject { ["name"] = nome, ["autodeskId"] = autodeskId };

        // -----------------------------------------------------------------------------------------
        // ExtrairResponsavelEtapa
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void SomenteCandidato_RobertoAndrei_OrigemCandidatoEtapa()
        {
            var detail = NextActionBy(claimedBy: null, candidateUsers: new JArray(Usuario("Roberto Andrei", "VD62UMSYJHSWN944")));

            var (nomes, ids, origem) = AccReviewWorkflowService.ExtrairResponsavelEtapa(detail);

            Assert.Equal("CANDIDATO_ETAPA", origem);
            Assert.Equal(new[] { "Roberto Andrei" }, nomes);
            Assert.Equal(new[] { "VD62UMSYJHSWN944" }, ids);
        }

        [Fact]
        public void ClaimedByPreenchido_VenceCandidato_OrigemAtribuido()
        {
            var detail = NextActionBy(
                claimedBy: new JArray(Usuario("Elisane Barros", "89V29ZGQNVDBPU49")),
                candidateUsers: new JArray(Usuario("Roberto Andrei", "VD62UMSYJHSWN944")));

            var (nomes, ids, origem) = AccReviewWorkflowService.ExtrairResponsavelEtapa(detail);

            Assert.Equal("RESPONSAVEL_ATRIBUIDO", origem);
            Assert.Equal(new[] { "Elisane Barros" }, nomes);
            Assert.Equal(new[] { "89V29ZGQNVDBPU49" }, ids);
        }

        [Fact]
        public void MultiplosCandidatos_TodosRetornados_NomesEIdsPareadosEmOrdemEstavel()
        {
            // Reproduz o Review real seq=602 (2 candidatos: tiago adan e Roberto Andrei).
            var detail = NextActionBy(
                claimedBy: null,
                candidateUsers: new JArray(
                    Usuario("tiago adan", "LKS2QB5PNBYLMVBV"),
                    Usuario("Roberto Andrei", "VD62UMSYJHSWN944")));

            var (nomes, ids, origem) = AccReviewWorkflowService.ExtrairResponsavelEtapa(detail);

            Assert.Equal("CANDIDATO_ETAPA", origem);
            Assert.Equal(2, nomes.Count);
            Assert.Equal(new[] { "Roberto Andrei", "tiago adan" }, nomes);
            Assert.Equal(new[] { "VD62UMSYJHSWN944", "LKS2QB5PNBYLMVBV" }, ids);
        }

        [Fact]
        public void NenhumResponsavel_ClaimedByECandidatosVazios_NaoEncontrado()
        {
            var detail = NextActionBy(claimedBy: null, candidateUsers: null);

            var (nomes, ids, origem) = AccReviewWorkflowService.ExtrairResponsavelEtapa(detail);

            Assert.Equal("NAO_ENCONTRADO", origem);
            Assert.Empty(nomes);
            Assert.Empty(ids);
        }

        [Fact]
        public void NextActionByAusente_NaoEncontrado()
        {
            var detail = new JObject();

            var (nomes, ids, origem) = AccReviewWorkflowService.ExtrairResponsavelEtapa(detail);

            Assert.Equal("NAO_ENCONTRADO", origem);
            Assert.Empty(nomes);
            Assert.Empty(ids);
        }

        // -----------------------------------------------------------------------------------------
        // ExtrairVencimentoEtapa
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void VencimentoUtc_ConvertidoParaSaoPaulo_MesmoDia()
        {
            // Valor real do Review 603: 2026-07-31T17:41:51.453Z (UTC) = 2026-07-31 14:41 em
            // America/Sao_Paulo (UTC-3) — mesmo dia, mas a conversão de fuso é obrigatória mesmo assim.
            var detail = new JObject { ["currentStepDueDate"] = "2026-07-31T17:41:51.453Z" };

            var (original, local, origem) = AccReviewWorkflowService.ExtrairVencimentoEtapa(detail);

            Assert.Equal("CURRENT_STEP_DUE_DATE", origem);
            Assert.NotNull(original);
            Assert.Equal(TimeSpan.Zero, original!.Value.Offset);
            Assert.Equal(new DateOnly(2026, 7, 31), local);
        }

        [Fact]
        public void VencimentoUtc_PertoDaMeiaNoite_CruzaParaDiaAnterior()
        {
            // 2026-01-01T02:00:00Z em America/Sao_Paulo (UTC-3) é 2025-12-31T23:00:00 — um DateTime.Parse
            // ingênuo seguido de .Date erraria para 2026-01-01. Prova que a conversão de fuso acontece
            // ANTES de extrair a data (seção 6 — nunca DateTime.Parse(valor).Date direto sobre o UTC bruto).
            var detail = new JObject { ["currentStepDueDate"] = "2026-01-01T02:00:00Z" };

            var (_, local, _) = AccReviewWorkflowService.ExtrairVencimentoEtapa(detail);

            Assert.Equal(new DateOnly(2025, 12, 31), local);
        }

        [Fact]
        public void VencimentoComOffsetExplicito_ConvertidoCorretamenteParaSaoPaulo()
        {
            // 2026-06-15T20:00:00-05:00 -> 2026-06-16T01:00:00Z -> 2026-06-15T22:00:00 em America/Sao_Paulo
            // (UTC-3) — permanece no mesmo dia, mas via conversão real de offset (não Z).
            var detail = new JObject { ["currentStepDueDate"] = "2026-06-15T20:00:00-05:00" };

            var (original, local, origem) = AccReviewWorkflowService.ExtrairVencimentoEtapa(detail);

            Assert.Equal("CURRENT_STEP_DUE_DATE", origem);
            Assert.NotNull(original);
            Assert.Equal(new DateOnly(2026, 6, 15), local);
        }

        [Fact]
        public void VencimentoAusente_RetornaNulo()
        {
            var detail = new JObject();

            var (original, local, origem) = AccReviewWorkflowService.ExtrairVencimentoEtapa(detail);

            Assert.Equal("NAO_ENCONTRADO", origem);
            Assert.Null(original);
            Assert.Null(local);
        }

        // -----------------------------------------------------------------------------------------
        // Correção 16/09/2026 (seção 2): tolerância a formatos reais além do ISO-8601 padrão — nunca
        // DateTime.Parse dependente de cultura da máquina, sempre InvariantCulture/UTC explícito.
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void VencimentoComSufixoDeZonaEntreColchetes_EstiloJavaZonedDateTime_EhReconhecido()
        {
            // java.time.ZonedDateTime.toString() pode devolver "...-04:00[America/New_York]" — .NET não
            // reconhece o colchete; o offset real antes dele (-04:00) precisa ser respeitado ao calcular o
            // instante UTC (mesmo comportamento de AdjustToUniversal já usado para os demais formatos —
            // o offset original não é preservado na struct, mas o instante correto é).
            var detail = new JObject { ["currentStepDueDate"] = "2026-09-15T00:00:00-04:00[America/New_York]" };

            var (original, local, origem) = AccReviewWorkflowService.ExtrairVencimentoEtapa(detail);

            Assert.Equal("CURRENT_STEP_DUE_DATE", origem);
            Assert.NotNull(original);
            Assert.Equal(new DateTimeOffset(2026, 9, 15, 4, 0, 0, TimeSpan.Zero), original);
            Assert.NotNull(local);
        }

        [Fact]
        public void VencimentoComSufixoDeZonaEntreColchetes_ZuluComColchete_EhReconhecido()
        {
            var detail = new JObject { ["currentStepDueDate"] = "2026-09-15T12:00:00Z[UTC]" };

            var (original, local, origem) = AccReviewWorkflowService.ExtrairVencimentoEtapa(detail);

            Assert.Equal("CURRENT_STEP_DUE_DATE", origem);
            Assert.NotNull(original);
            Assert.Equal(new DateOnly(2026, 9, 15), DateOnly.FromDateTime(original!.Value.UtcDateTime));
        }

        [Fact]
        public void VencimentoEpochMilissegundos_EhReconhecido()
        {
            // 1757937600000 ms = 2025-09-15T12:00:00Z
            var detail = new JObject { ["currentStepDueDate"] = "1757937600000" };

            var (original, _, origem) = AccReviewWorkflowService.ExtrairVencimentoEtapa(detail);

            Assert.Equal("CURRENT_STEP_DUE_DATE", origem);
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1757937600000L), original);
        }

        [Fact]
        public void VencimentoEpochSegundos_EhReconhecido()
        {
            // 1757937600 s = 2025-09-15T12:00:00Z
            var detail = new JObject { ["currentStepDueDate"] = "1757937600" };

            var (original, _, origem) = AccReviewWorkflowService.ExtrairVencimentoEtapa(detail);

            Assert.Equal("CURRENT_STEP_DUE_DATE", origem);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1757937600L), original);
        }

        [Theory]
        [InlineData("não é uma data")]
        [InlineData("15 de Setembro de 2026")]
        [InlineData("[]")]
        public void VencimentoRealmenteInvalido_ContinuaGerandoFormatoInvalido(string bruto)
        {
            var detail = new JObject { ["currentStepDueDate"] = bruto };

            var (original, local, origem) = AccReviewWorkflowService.ExtrairVencimentoEtapa(detail);

            Assert.Equal("FORMATO_INVALIDO", origem);
            Assert.Null(original);
            Assert.Null(local);
        }

        // -----------------------------------------------------------------------------------------
        // ConstruirMetadadosEtapaReview (Review inteiro — reproduz o Review 603 real e um Review CLOSED)
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void Review603Real_Open_Checker_RobertoAndreiCandidato_VencimentoResolvido()
        {
            var review = new JObject { ["sequenceId"] = 603, ["name"] = "PACOTE EMISSÃO P&ID AREA 900" };
            var detail = new JObject
            {
                ["status"] = "OPEN",
                ["currentStepDueDate"] = "2026-07-31T17:41:51.453Z",
                ["nextActionBy"] = new JObject
                {
                    ["claimedBy"] = new JArray(),
                    ["candidates"] = new JObject
                    {
                        ["roles"] = new JArray(),
                        ["users"] = new JArray(Usuario("Roberto Andrei", "VD62UMSYJHSWN944")),
                        ["companies"] = new JArray()
                    }
                }
            };

            var metadados = AccReviewWorkflowService.ConstruirMetadadosEtapaReview(
                review, detail, reviewId: "e0d04645-4d66-4b43-92c7-b1d177e126e8", workflowId: "6d8001be-d77a-43d3-8afe-8d3a494e0a74",
                currentStepId: "Lane_4I2MAOQouW", nomeEtapaResolvida: "CHECKER", reviewAberto: true, dataAtualizacaoReview: new DateTime(2026, 7, 30, 17, 42, 12), logger: null);

            Assert.Equal("603", metadados.ReviewNumber);
            Assert.Equal("PACOTE EMISSÃO P&ID AREA 900", metadados.ReviewName);
            Assert.Equal("OPEN", metadados.ReviewStatus);
            Assert.Equal("CHECKER", metadados.CurrentStepName);
            Assert.Equal("Lane_4I2MAOQouW", metadados.CurrentStepId);
            Assert.Equal(new[] { "Roberto Andrei" }, metadados.ResponsibleNames);
            Assert.Equal("CANDIDATO_ETAPA", metadados.ResponsibleSource);
            Assert.Equal(new DateOnly(2026, 7, 31), metadados.DueDateLocal);
            Assert.Equal("CURRENT_STEP_DUE_DATE", metadados.DueDateSource);
        }

        [Fact]
        public void ReviewClosed_NuncaGravaEtapaResponsavelOuVencimento()
        {
            var review = new JObject { ["sequenceId"] = 592, ["name"] = "QUALQUER PACOTE" };
            var detail = new JObject { ["status"] = "CLOSED" };

            var metadados = AccReviewWorkflowService.ConstruirMetadadosEtapaReview(
                review, detail, reviewId: "374f88f8-b4cc-4861-82ed-ea8ca5968566", workflowId: "wf-1",
                currentStepId: "Lane_KZ9gCjnZgJ", nomeEtapaResolvida: null, reviewAberto: false, dataAtualizacaoReview: null, logger: null);

            Assert.Equal("CLOSED", metadados.ReviewStatus);
            Assert.Null(metadados.CurrentStepId);
            Assert.Null(metadados.CurrentStepName);
            Assert.Empty(metadados.ResponsibleNames);
            Assert.Equal("REVIEW_NAO_ABERTO", metadados.ResponsibleSource);
            Assert.Null(metadados.DueDateLocal);
            Assert.Equal("REVIEW_NAO_ABERTO", metadados.DueDateSource);
        }

        [Fact]
        public void ReviewVoid_TratadoComoNaoAberto_MesmasRegrasDoClosed()
        {
            // ReviewEstaAberto só reconhece OPEN/INITIATED — VOID chega aqui com reviewAberto=false,
            // igual a CLOSED (seção 9: "VOID não é etapa ativa").
            var review = new JObject { ["sequenceId"] = 595, ["name"] = "PACOTE ANTIGO" };
            var detail = new JObject { ["status"] = "VOID" };

            var metadados = AccReviewWorkflowService.ConstruirMetadadosEtapaReview(
                review, detail, reviewId: "08c203c5-dd8e-4f49-9a94-0361acfad927", workflowId: "wf-1",
                currentStepId: "Lane_xRkdCwuXCu", nomeEtapaResolvida: null, reviewAberto: false, dataAtualizacaoReview: null, logger: null);

            Assert.Equal("VOID", metadados.ReviewStatus);
            Assert.Null(metadados.CurrentStepName);
            Assert.Empty(metadados.ResponsibleNames);
            Assert.Null(metadados.DueDateLocal);
        }
    }
}
