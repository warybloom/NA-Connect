using APS.WebApp.Services.OperationalAudit.Configuration;
using Newtonsoft.Json.Linq;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Correção pós-Checkpoint 3 (WF-007): prova que <see cref="WorkflowStepNameResolver"/> reproduz os
    /// QUATRO níveis reais de <c>AccReviewWorkflowService.ResolverNomeEtapaAtualAsync</c> — nunca só
    /// correspondência direta em <c>steps[]</c>. Cada teste usa a MESMA estrutura de campos que o método
    /// real varre: <c>CamposIdEtapa = {"id","stepId","laneId","key","nodeId"}</c>,
    /// <c>CamposNomeEtapa = {"name","displayName","label","title"}</c>.
    /// </summary>
    public class WorkflowStepNameResolverTests
    {
        [Fact]
        public void StepDireto_EmStepsArray_Resolvido()
        {
            var definicao = new JObject
            {
                ["steps"] = new JArray(new JObject { ["id"] = "STEP1", ["name"] = "Revisão Técnica" })
            };

            var (nome, resultado) = WorkflowStepNameResolver.Resolver(definicao, "STEP1");

            Assert.Equal("Revisão Técnica", nome);
            Assert.Equal(WorkflowStepResolutionOutcome.EtapaResolvida, resultado);
        }

        [Fact]
        public void LaneDireto_ForaDeStepsArray_ResolvidoPorCampoLaneId()
        {
            // Nó fora de "steps[]", identificado só por "laneId" (não "id") — cobre CamposIdEtapa[2].
            var definicao = new JObject
            {
                ["workflow"] = new JObject
                {
                    ["lanes"] = new JArray(new JObject { ["laneId"] = "LANE-9", ["displayName"] = "Aprovação Financeira" })
                }
            };

            var (nome, resultado) = WorkflowStepNameResolver.Resolver(definicao, "LANE-9");

            Assert.Equal("Aprovação Financeira", nome);
            Assert.Equal(WorkflowStepResolutionOutcome.EtapaResolvida, resultado);
        }

        [Fact]
        public void NoAninhado_NomeNoAncestral_ResolvidoSubindoAteSeisNiveis()
        {
            // O nó com o ID não tem nenhum campo de nome — o nome só existe no ANCESTRAL (ExtrairNomeOriginalEtapaAncestral).
            var definicao = new JObject
            {
                ["graph"] = new JObject
                {
                    ["name"] = "Etapa Pai Com Nome",
                    ["children"] = new JArray(
                        new JObject
                        {
                            ["nested"] = new JObject { ["nodeId"] = "NODE-DEEP" } // sem campo de nome — só o ancestral "graph" tem.
                        })
                }
            };

            var (nome, resultado) = WorkflowStepNameResolver.Resolver(definicao, "NODE-DEEP");

            Assert.Equal("Etapa Pai Com Nome", nome);
            Assert.Equal(WorkflowStepResolutionOutcome.EtapaResolvida, resultado);
        }

        [Fact]
        public void JsonEmbutido_StepIdDentroDeStringJson_Resolvido()
        {
            string jsonInterno = "{\"nodes\":[{\"key\":\"K-EMBUTIDO\",\"label\":\"Etapa Serializada\"}]}";
            var definicao = new JObject
            {
                ["metadata"] = new JObject { ["rawConfig"] = jsonInterno }
            };

            var (nome, resultado) = WorkflowStepNameResolver.Resolver(definicao, "K-EMBUTIDO");

            Assert.Equal("Etapa Serializada", nome);
            Assert.Equal(WorkflowStepResolutionOutcome.EtapaResolvida, resultado);
        }

        [Fact]
        public void XmlEmbutido_StepIdDentroDeStringXml_Resolvido()
        {
            string xmlInterno = "<workflow><step id=\"X-1\" title=\"Etapa XML\">X-1</step></workflow>";
            var definicao = new JObject
            {
                ["legacyDefinition"] = xmlInterno
            };

            var (nome, resultado) = WorkflowStepNameResolver.Resolver(definicao, "X-1");

            Assert.Equal("Etapa XML", nome);
            Assert.Equal(WorkflowStepResolutionOutcome.EtapaResolvida, resultado);
        }

        [Fact]
        public void DefinicaoIndisponivel_NuncaAfirmaNaoResolvido()
        {
            var (nome, resultado) = WorkflowStepNameResolver.Resolver(null, "QUALQUER-STEP");

            Assert.Null(nome);
            Assert.Equal(WorkflowStepResolutionOutcome.DefinicaoIndisponivel, resultado);
        }

        [Fact]
        public void DefinicaoParcial_NoEncontradoSemNomeEmLugarNenhum_EtapaPresenteSemNome()
        {
            // O nó com o ID existe (encontrado por "id"), mas nem ele nem nenhum ancestral têm
            // name/displayName/label/title — LANE_ID_PRESENTE_SEM_NOME no método real.
            var definicao = new JObject
            {
                ["steps"] = new JArray(new JObject { ["id"] = "STEP-SEM-NOME", ["order"] = 3 })
            };

            var (nome, resultado) = WorkflowStepNameResolver.Resolver(definicao, "STEP-SEM-NOME");

            Assert.Null(nome);
            Assert.Equal(WorkflowStepResolutionOutcome.EtapaPresenteSemNome, resultado);
        }

        [Fact]
        public void DefinicaoCompletaComStepRealmenteInexistente_EtapaAusenteNaDefinicao()
        {
            // Definição totalmente carregada, com vários steps reais e nomeados — mas o currentStepId
            // pesquisado não é nenhum deles, nem aparece em nenhuma string embutida.
            var definicao = new JObject
            {
                ["steps"] = new JArray(
                    new JObject { ["id"] = "STEP1", ["name"] = "Revisão Técnica" },
                    new JObject { ["id"] = "STEP2", ["name"] = "Aprovação Final" })
            };

            var (nome, resultado) = WorkflowStepNameResolver.Resolver(definicao, "STEP-INEXISTENTE");

            Assert.Null(nome);
            Assert.Equal(WorkflowStepResolutionOutcome.EtapaAusenteNaDefinicao, resultado);
        }

        [Fact]
        public void StepIdVazio_TratadoComoDefinicaoIndisponivel_NuncaLanca()
        {
            var definicao = new JObject { ["steps"] = new JArray(new JObject { ["id"] = "STEP1", ["name"] = "X" }) };

            var (nome, resultado) = WorkflowStepNameResolver.Resolver(definicao, "");

            Assert.Null(nome);
            Assert.Equal(WorkflowStepResolutionOutcome.DefinicaoIndisponivel, resultado);
        }

        [Fact]
        public void JsonInvalidoDentroDaString_NuncaLanca_TratadoComoNaoResolvido()
        {
            var definicao = new JObject
            {
                ["metadata"] = new JObject { ["rawConfig"] = "{ISTO-CONTEM-STEPX-MAS-NAO-E-JSON-VALIDO" }
            };

            var (nome, resultado) = WorkflowStepNameResolver.Resolver(definicao, "STEPX");

            Assert.Null(nome);
            Assert.Equal(WorkflowStepResolutionOutcome.EtapaAusenteNaDefinicao, resultado);
        }
    }
}
