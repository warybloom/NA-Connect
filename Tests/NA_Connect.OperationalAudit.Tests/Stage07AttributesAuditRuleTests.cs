using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Configuration;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.OperationalAudit.Rules;
using NA_Connect.OperationalAudit.Tests.Fakes;
using NA_Connect.Services;

namespace NA_Connect.OperationalAudit.Tests
{
    public class Stage07AttributesAuditRuleTests
    {
        private const string FolderId = "F-ENG04";
        private static readonly string[] AtributosEngenharia04 = { "DOCUMENT TITLE", "CLIENT DOCUMENT NUMBER", "N&A DOCUMENT NUMBER", "REV.", "TRANMISTAL NUMBER", "EMISSION DATA" };

        private static (Stage07AttributesAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc, FakeAuditDatabaseReadOnlyAdapter Db, FakeAuditWorkbookReadOnlyAdapter Workbook, OperationalAuditOptions Options)
            NovaRegra(bool comPastaEngenharia = true)
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var db = new FakeAuditDatabaseReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();

            var options = new OperationalAuditOptions
            {
                ProjectProfiles = new Dictionary<string, AuditProjectProfile>
                {
                    ["default"] = new AuditProjectProfile { RequiredAttributeStructures = new List<string> { "ENGENHARIA_04" } }
                }
            };

            acc.ConfigurarPasta("09.14", null); // sem LDP — reconciliação fica vazia, não afeta os testes de atributo.
            if (comPastaEngenharia)
            {
                acc.ConfigurarPasta("04 - ENGENHARIA", TestFixtures.Pasta(FolderId, "04 - ENGENHARIA"));
                acc.ConfigurarListagem(FolderId, new AccFolderListingResult());
            }
            else
            {
                acc.ConfigurarPasta("04 - ENGENHARIA", null);
            }

            return (new Stage07AttributesAuditRule(acc, db, workbook), acc, db, workbook, options);
        }

        private static List<AccCustomAttributeDefinitionRef> DefinicoesCompletas() =>
            AtributosEngenharia04.Select((nome, i) => new AccCustomAttributeDefinitionRef { DefinitionId = i + 1, Name = nome }).ToList();

        [Fact]
        public void Etapa_e_NomeEtapa_corretos()
        {
            var (regra, _, _, _, _) = NovaRegra();
            Assert.Equal("7", regra.Etapa);
        }

        [Fact]
        public async Task EstruturaObrigatoriaAusente_GeraFinding()
        {
            var (regra, acc, _, _, options) = NovaRegra(comPastaEngenharia: false);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "ATR-ESTRUTURA_OBRIGATORIA_AUSENTE" && f.ConfigurationKey == "ENGENHARIA_04");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task EstruturaOpcionalAusente_NaoGeraFinding()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var db = new FakeAuditDatabaseReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            acc.ConfigurarPasta("09.14", null);
            acc.ConfigurarPasta("04 - ENGENHARIA", null);
            acc.ConfigurarPasta("09 - ENGINEERING DOCUMENTS", null);
            acc.ConfigurarPasta("10 -", null);
            acc.ConfigurarPasta("07 - 3D", null);

            var options = new OperationalAuditOptions
            {
                ProjectProfiles = new Dictionary<string, AuditProjectProfile>
                {
                    ["default"] = new AuditProjectProfile { OptionalAttributeStructures = new List<string> { "ENGENHARIA_04", "PRODUCAO_DOCUMENTOS_09", "PRODUCAO_10", "PRODUCAO_MODELOS_3D" } }
                }
            };

            var regra = new Stage07AttributesAuditRule(acc, db, workbook);
            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-ESTRUTURA_OBRIGATORIA_AUSENTE");
        }

        [Fact]
        public async Task AtributoCadastradoEVisivel_NaoGeraFinding()
        {
            var (regra, acc, db, _, options) = NovaRegra();
            var item = TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderId, DefinicoesCompletas());
            acc.ConfigurarValores("urn:1", AtributosEngenharia04.Select(n => new AccCustomAttributeValueRef { Name = n, ValorPresente = true, Valor = "x" }).ToList());
            db.ConfigurarResposta("ACC_Attribute_Sync_State", TestFixtures.Tabela(new[] { "UltimaTentativaSucesso" }, new object?[] { true }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId.StartsWith("ATR-CUSTOM"));
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-PAYLOAD_INCOMPLETO");
        }

        [Fact]
        public async Task AtributoAusente_GeraAtrCustom001()
        {
            var (regra, acc, db, _, options) = NovaRegra();
            var item = TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderId, new List<AccCustomAttributeDefinitionRef>()); // nenhuma definição cadastrada.
            db.ConfigurarResposta("ACC_Attribute_Sync_State", new System.Data.DataTable());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            var finding = Assert.Single(resultado.Findings.Where(f => f.RegraId == "ATR-CUSTOM-001" && f.CustomAttributeName == "DOCUMENT TITLE"));
            Assert.Equal(OperationalAuditErrorDomain.Operacional, finding.ErrorDomain);
            Assert.Equal(OperationalAuditClassification.QualidadeConfiguracaoAcc, finding.Classificacao);
            Assert.Equal("BIM Manager / Administrador ACC", finding.ResponsavelProvavel);
        }

        [Fact]
        public async Task AtributoComprovadamenteOculto_GeraAtrCustom002()
        {
            var (regra, acc, db, _, options) = NovaRegra();
            var item = TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderId, DefinicoesCompletas());
            var valores = AtributosEngenharia04.Select(n => new AccCustomAttributeValueRef { Name = n, ValorPresente = true, Valor = "x" }).ToList();
            valores[0] = new AccCustomAttributeValueRef { Name = "DOCUMENT TITLE", ValorPresente = false, EvidenciaVisibilidadeExplicita = true };
            acc.ConfigurarValores("urn:1", valores);
            db.ConfigurarResposta("ACC_Attribute_Sync_State", new System.Data.DataTable());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "ATR-CUSTOM-002");
            Assert.Equal(OperationalAuditClassification.AtributoAccOculto, finding.Classificacao);
            Assert.Equal(CustomAttributeState.PresentHidden, finding.CustomAttributeState);
            Assert.True(finding.IsCustomAttributeHidden);
        }

        [Fact]
        public async Task DuasDefinicoesComMesmoNomeNormalizado_GeraAtrCustom003()
        {
            var (regra, acc, db, _, options) = NovaRegra();
            var item = TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            var definicoes = DefinicoesCompletas();
            definicoes.Add(new AccCustomAttributeDefinitionRef { DefinitionId = 99, Name = "document title" }); // mesmo nome normalizado de "DOCUMENT TITLE".
            acc.ConfigurarDefinicoes(FolderId, definicoes);
            acc.ConfigurarValores("urn:1", AtributosEngenharia04.Select(n => new AccCustomAttributeValueRef { Name = n, ValorPresente = true, Valor = "x" }).ToList());
            db.ConfigurarResposta("ACC_Attribute_Sync_State", TestFixtures.Tabela(new[] { "UltimaTentativaSucesso" }, new object?[] { true }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "ATR-CUSTOM-003" && f.CustomAttributeName == "DOCUMENT TITLE");
            Assert.Equal(OperationalAuditClassification.AtributoAccAmbiguo, finding.Classificacao);
        }

        [Fact]
        public async Task VisibilidadeInconclusiva_GeraAtrCustom004_NuncaAfirmaOculto()
        {
            var (regra, acc, db, _, options) = NovaRegra();
            var item = TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderId, DefinicoesCompletas());
            var valores = AtributosEngenharia04.Select(n => new AccCustomAttributeValueRef { Name = n, ValorPresente = true, Valor = "x" }).ToList();
            // "REV." sem valor e SEM evidência explícita de visibilidade — exatamente o caso real da API hoje.
            valores[3] = new AccCustomAttributeValueRef { Name = "REV.", ValorPresente = false, EvidenciaVisibilidadeExplicita = null };
            acc.ConfigurarValores("urn:1", valores);
            db.ConfigurarResposta("ACC_Attribute_Sync_State", new System.Data.DataTable());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "ATR-CUSTOM-004");
            Assert.Equal(OperationalAuditErrorDomain.Inconclusivo, finding.ErrorDomain);
            Assert.Equal(CustomAttributeState.ApiVisibilityInconclusive, finding.CustomAttributeState);
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-CUSTOM-002"); // nunca afirma oculto sem evidência.
        }

        [Fact]
        public async Task PayloadCompleto_NaoGeraAtrPayloadIncompleto()
        {
            var (regra, acc, db, _, options) = NovaRegra();
            var item = TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderId, DefinicoesCompletas());
            acc.ConfigurarValores("urn:1", AtributosEngenharia04.Select(n => new AccCustomAttributeValueRef { Name = n, ValorPresente = true, Valor = "x" }).ToList());
            db.ConfigurarResposta("ACC_Attribute_Sync_State", TestFixtures.Tabela(new[] { "UltimaTentativaSucesso" }, new object?[] { true }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-PAYLOAD_INCOMPLETO");
        }

        [Fact]
        public async Task PayloadIncompleto_GeraFinding()
        {
            var (regra, acc, db, _, options) = NovaRegra();
            var item = TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderId, DefinicoesCompletas());
            var valores = AtributosEngenharia04.Select(n => new AccCustomAttributeValueRef { Name = n, ValorPresente = true, Valor = "x" }).ToList();
            valores[5] = new AccCustomAttributeValueRef { Name = "EMISSION DATA", ValorPresente = false, EvidenciaVisibilidadeExplicita = false };
            acc.ConfigurarValores("urn:1", valores);
            db.ConfigurarResposta("ACC_Attribute_Sync_State", new System.Data.DataTable());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.Single(resultado.Findings, f => f.RegraId == "ATR-PAYLOAD_INCOMPLETO");
        }

        [Fact]
        public async Task SqlSemValorDeCache_NuncaGeraFalsoErroDeAtributo_SoAvisoDeCache()
        {
            var (regra, acc, db, _, options) = NovaRegra();
            var item = TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderId, DefinicoesCompletas());
            acc.ConfigurarValores("urn:1", AtributosEngenharia04.Select(n => new AccCustomAttributeValueRef { Name = n, ValorPresente = true, Valor = "x" }).ToList());
            db.ConfigurarResposta("ACC_Attribute_Sync_State", new System.Data.DataTable()); // sem linha -> sem evidência de sucesso.

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId.StartsWith("ATR-CUSTOM"));
            Assert.Single(resultado.Findings, f => f.RegraId == "ATR-CACHE_SEM_EVIDENCIA_DE_POST");
        }

        [Fact]
        public async Task ArquivoEmPastaDeBackup_GeraFinding_NuncaAvaliaAtributos()
        {
            var (regra, acc, db, _, options) = NovaRegra();
            var item = TestFixtures.Item("I1", "doc.pdf", FolderId, "OLD");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderId, DefinicoesCompletas());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "ATR-ARQUIVO_EM_BACKUP");
            Assert.Equal("doc.pdf", finding.NomeArquivo);
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId.StartsWith("ATR-CUSTOM"));
        }

        [Fact]
        public async Task ArquivoEmPastaOculta_GeraFindingDeRecycleBin()
        {
            var (regra, acc, _, _, options) = NovaRegra();
            var item = TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA", oculta: true);
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderId, DefinicoesCompletas());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.Single(resultado.Findings, f => f.RegraId == "ATR-ARQUIVO_EM_RECYCLE_BIN");
        }

        [Fact]
        public async Task ArquivoTemporario_IgnoradoSemFinding()
        {
            var (regra, acc, _, _, options) = NovaRegra();
            var item = TestFixtures.Item("I1", "~$doc.pdf", FolderId, "04 - ENGENHARIA");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderId, DefinicoesCompletas());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.Empty(resultado.Findings.Where(f => f.NomeArquivo == "~$doc.pdf"));
        }

        [Fact]
        public async Task PaginacaoCompleta_RegistraQuantidadeERequisitoAprovado()
        {
            var (regra, acc, db, _, options) = NovaRegra();
            var item = TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderId, DefinicoesCompletas());
            acc.ConfigurarValores("urn:1", AtributosEngenharia04.Select(n => new AccCustomAttributeValueRef { Name = n, ValorPresente = true, Valor = "x" }).ToList());
            db.ConfigurarResposta("ACC_Attribute_Sync_State", TestFixtures.Tabela(new[] { "UltimaTentativaSucesso" }, new object?[] { true }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            var requisito = Assert.Single(resultado.Requirements, r => r.RequirementCode == "ATR-PAGINACAO");
            Assert.Equal(OperationalRequirementStatus.Approved, requisito.Status);
        }

        // =========================================================================================
        // Gap 1 — ATR-SNAPSHOT_INCOMPLETO (agora com finding real, não só requirement).
        // =========================================================================================

        [Fact]
        public async Task Snapshot_PaginacaoCompleta_NaoGeraFinding()
        {
            var (regra, acc, db, _, options) = NovaRegra();
            acc.ConfigurarListagem(FolderId, new AccFolderListingResult { PaginacaoCompleta = true });
            db.ConfigurarResposta("ACC_Attribute_Sync_State", new System.Data.DataTable());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-SNAPSHOT_INCOMPLETO");
        }

        [Fact]
        public async Task Snapshot_ProximaPaginaPerdida_GeraFindingCritico()
        {
            var (regra, acc, _, _, options) = NovaRegra();
            acc.ConfigurarListagem(FolderId, new AccFolderListingResult
            {
                PaginacaoCompleta = false,
                MotivoPaginacaoIncompleta = "Limite de segurança de 500 páginas atingido na pasta '04 - ENGENHARIA' — a próxima página não foi obtida."
            });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "ATR-SNAPSHOT_INCOMPLETO");
            Assert.Equal(OperationalAuditSeverity.Critico, finding.Severidade);
            Assert.Equal(OperationalAuditErrorDomain.Automacao, finding.ErrorDomain);
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task Snapshot_ErroNaPagina2_GeraFindingComEvidenciaDeIntegracaoExterna()
        {
            var (regra, acc, _, _, options) = NovaRegra();
            acc.ConfigurarListagem(FolderId, new AccFolderListingResult
            {
                PaginacaoCompleta = false,
                MotivoPaginacaoIncompleta = "Falha ao obter a página 2 da pasta '04 - ENGENHARIA': tempo esgotado."
            });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "ATR-SNAPSHOT_INCOMPLETO");
            Assert.Equal(OperationalAuditClassification.IntegracaoExterna, finding.Classificacao);
        }

        [Fact]
        public async Task Snapshot_TotalConhecidoDivergente_GeraFinding()
        {
            var (regra, acc, _, _, options) = NovaRegra();
            acc.ConfigurarListagem(FolderId, new AccFolderListingResult
            {
                Itens = new[] { TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA") },
                PaginacaoCompleta = false,
                MotivoPaginacaoIncompleta = "Nenhum link de próxima página, mas a API declarou totalResults=5 e apenas 1 item(ns) foram coletados.",
                TotalDeclaradoPelaApi = 5
            });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "ATR-SNAPSHOT_INCOMPLETO");
            Assert.Equal("5", finding.ValorEsperado);
            Assert.Equal("1", finding.ValorEncontrado);
        }

        [Fact]
        public async Task Snapshot_PoucosArquivosSemEvidenciaObjetiva_NuncaGeraFalsoPositivo()
        {
            var (regra, acc, db, _, options) = NovaRegra();
            // Só 1 arquivo — mas PaginacaoCompleta continua true (nenhuma evidência objetiva de incompletude).
            acc.ConfigurarListagem(FolderId, new AccFolderListingResult
            {
                Itens = new[] { TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA") },
                PaginacaoCompleta = true
            });
            db.ConfigurarResposta("ACC_Attribute_Sync_State", new System.Data.DataTable());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-SNAPSHOT_INCOMPLETO");
        }

        // =========================================================================================
        // Gap 2 — ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA.
        // =========================================================================================

        private static AuditAttributeStructureProfile ConfigModelos3DComSubpastas(params string[] subpastas) => new AuditAttributeStructureProfile
        {
            ChaveConfiguracao = "PRODUCAO_MODELOS_3D_07",
            AliasesRaiz = new[] { "07 - 3D AUTHORING SOFTWARE" },
            Recursivo = true,
            AtributosObrigatorios = new[] { "DOCUMENT TITLE", "CLIENT DOCUMENT NUMBER", "N&A DOCUMENT NUMBER", "REV." },
            CaminhosPermitidosRelativos = subpastas
        };

        private static AuditAttributeStructureProfile ConfigModelos3D08ComSubpastas(params string[] subpastas) => new AuditAttributeStructureProfile
        {
            ChaveConfiguracao = "PRODUCAO_MODELOS_3D_08",
            AliasesRaiz = new[] { "08 - FEDERATED MODEL" },
            Recursivo = true,
            AtributosObrigatorios = new[] { "DOCUMENT TITLE", "CLIENT DOCUMENT NUMBER", "N&A DOCUMENT NUMBER", "REV." },
            CaminhosPermitidosRelativos = subpastas
        };

        [Fact]
        public async Task PastaNaoPermitida_ArquivoEmSubpastaPermitida_NaoGeraFinding()
        {
            var (regra, acc, db, _, _) = NovaRegra();
            var config = ConfigModelos3DComSubpastas("07.01.01");
            var pasta = TestFixtures.Pasta("F-3D", "07 - 3D AUTHORING SOFTWARE");
            var item = TestFixtures.Item("I1", "modelo.rvt", "F-3D", "AUTODESK REVIT", caminhoRelativo: "07.01.01 - AUTODESK REVIT");
            acc.ConfigurarListagem("F-3D", TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes("F-3D", DefinicoesCompletas());
            acc.ConfigurarValores("urn:1", AtributosDe(config).Select(n => new AccCustomAttributeValueRef { Name = n, ValorPresente = true, Valor = "x" }).ToList());
            db.ConfigurarResposta("ACC_Attribute_Sync_State", TestFixtures.Tabela(new[] { "UltimaTentativaSucesso" }, new object?[] { true }));

            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            await regra.AuditarConfiguracaoAsync(TestFixtures.NovoContexto(), config, pasta, new HashSet<string>(), findings, requirements, default);

            Assert.DoesNotContain(findings, f => f.RegraId == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA");
            Assert.Contains(requirements, r => r.RequirementCode == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA" && r.Status == OperationalRequirementStatus.Approved);
        }

        [Fact]
        public async Task PastaNaoPermitida_ArquivoForaDeSubpastaPermitida_GeraFinding()
        {
            var (regra, acc, _, _, _) = NovaRegra();
            var config = ConfigModelos3DComSubpastas("07.01.01");
            var pasta = TestFixtures.Pasta("F-3D", "07 - 3D AUTHORING SOFTWARE");
            var item = TestFixtures.Item("I1", "modelo.rvt", "F-3D", "PASTA QUALQUER", caminhoRelativo: "99.99 - PASTA NAO CATALOGADA");
            acc.ConfigurarListagem("F-3D", TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes("F-3D", new List<AccCustomAttributeDefinitionRef>());

            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            await regra.AuditarConfiguracaoAsync(TestFixtures.NovoContexto(), config, pasta, new HashSet<string>(), findings, requirements, default);

            var finding = Assert.Single(findings, f => f.RegraId == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA");
            Assert.Equal(OperationalAuditErrorDomain.Operacional, finding.ErrorDomain);
            Assert.Equal(OperationalAuditClassification.EstruturaAcc, finding.Classificacao);
            Assert.Equal(OperationalAuditSeverity.Alto, finding.Severidade);
            Assert.Contains(requirements, r => r.RequirementCode == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA" && r.Status == OperationalRequirementStatus.Failed);
        }

        [Fact]
        public async Task PastaNaoPermitida_ConfiguracaoInsuficiente_NuncaInventaErro_MarcaInconclusive()
        {
            var (regra, acc, _, _, _) = NovaRegra();
            var config = ConfigModelos3DComSubpastas(); // nenhuma subpasta configurada = configuração insuficiente.
            var pasta = TestFixtures.Pasta("F-3D", "07 - 3D AUTHORING SOFTWARE");
            var item = TestFixtures.Item("I1", "modelo.rvt", "F-3D", "PASTA QUALQUER", caminhoRelativo: "QUALQUER COISA");
            acc.ConfigurarListagem("F-3D", TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes("F-3D", new List<AccCustomAttributeDefinitionRef>());

            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            await regra.AuditarConfiguracaoAsync(TestFixtures.NovoContexto(), config, pasta, new HashSet<string>(), findings, requirements, default);

            Assert.DoesNotContain(findings, f => f.RegraId == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA");
            var requisito = Assert.Single(requirements, r => r.RequirementCode == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA");
            Assert.Equal(OperationalRequirementStatus.Inconclusive, requisito.Status);
        }

        [Fact]
        public async Task PastaNaoPermitida_ConfiguracaoNaoRecursiva_NuncaAvaliaEsteCheck()
        {
            var (regra, acc, db, _, options) = NovaRegra();
            var item = TestFixtures.Item("I1", "doc.pdf", FolderId, "04 - ENGENHARIA");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes(FolderId, DefinicoesCompletas());
            acc.ConfigurarValores("urn:1", AtributosEngenharia04.Select(n => new AccCustomAttributeValueRef { Name = n, ValorPresente = true, Valor = "x" }).ToList());
            db.ConfigurarResposta("ACC_Attribute_Sync_State", TestFixtures.Tabela(new[] { "UltimaTentativaSucesso" }, new object?[] { true }));

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.DoesNotContain(resultado.Requirements, r => r.RequirementCode == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA");
        }

        [Fact]
        public async Task PastaNaoPermitida_DistintoDeBackupERecycleBin()
        {
            var (regra, acc, _, _, _) = NovaRegra();
            var config = ConfigModelos3DComSubpastas("07.01.01");
            var pasta = TestFixtures.Pasta("F-3D", "07 - 3D AUTHORING SOFTWARE");
            var itemBackup = TestFixtures.Item("I1", "modelo_old.rvt", "F-3D", "OLD", caminhoRelativo: "99.99 - FORA");
            var itemRecycle = TestFixtures.Item("I2", "modelo2.rvt", "F-3D", "Reciclagem", oculta: true, caminhoRelativo: "99.99 - FORA");
            acc.ConfigurarListagem("F-3D", TestFixtures.Listagem(itemBackup, itemRecycle));
            acc.ConfigurarDefinicoes("F-3D", new List<AccCustomAttributeDefinitionRef>());

            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            await regra.AuditarConfiguracaoAsync(TestFixtures.NovoContexto(), config, pasta, new HashSet<string>(), findings, requirements, default);

            Assert.Single(findings, f => f.RegraId == "ATR-ARQUIVO_EM_BACKUP");
            Assert.Single(findings, f => f.RegraId == "ATR-ARQUIVO_EM_RECYCLE_BIN");
            Assert.DoesNotContain(findings, f => f.RegraId == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA");
        }

        // =========================================================================================
        // Gaps 3/4 — ATR-CODIGO_AMBIGUO e ATR-MULTIPLAS_EXTENSOES_INCOMPLETAS.
        // =========================================================================================

        private static (Stage07AttributesAuditRule Regra, FakeAuditAccReadOnlyAdapter Acc, FakeAuditDatabaseReadOnlyAdapter Db, FakeAuditWorkbookReadOnlyAdapter Workbook, OperationalAuditOptions Options)
            NovaRegraComLdp(string codigoNa)
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var db = new FakeAuditDatabaseReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();

            var options = new OperationalAuditOptions
            {
                ProjectProfiles = new Dictionary<string, AuditProjectProfile>
                {
                    ["default"] = new AuditProjectProfile { RequiredAttributeStructures = new List<string> { "ENGENHARIA_04" } }
                }
            };

            var pastaGestao = TestFixtures.Pasta("F-GESTAO", "09.14 - MANAGEMENT");
            acc.ConfigurarPasta("09.14", pastaGestao);
            acc.ConfigurarListagem("F-GESTAO", TestFixtures.Listagem(TestFixtures.Item("ILDP", "LDP.xlsx", "F-GESTAO", "09.14 - MANAGEMENT", versionUrn: "urn:ldp")));
            acc.ConfigurarConteudo("urn:ldp", new byte[] { 1 });
            workbook.Enfileirar(TestFixtures.Workbook(TestFixtures.Aba("LDP", new[] { "N&A" },
                new Dictionary<int, Dictionary<string, string?>> { [2] = new() { ["N&A"] = codigoNa } })));

            acc.ConfigurarPasta("04 - ENGENHARIA", TestFixtures.Pasta(FolderId, "04 - ENGENHARIA"));
            db.ConfigurarResposta("ACC_Attribute_Sync_State", new System.Data.DataTable());

            return (new Stage07AttributesAuditRule(acc, db, workbook), acc, db, workbook, options);
        }

        [Fact]
        public async Task CodigoAmbiguo_DoisCandidatosIncompativeis_GeraFinding_SemEscolherVencedor()
        {
            var (regra, acc, _, _, options) = NovaRegraComLdp("DOC-1");
            var item1 = TestFixtures.Item("I1", "DOC-1-VersaoA.pdf", FolderId, "04 - ENGENHARIA", versionUrn: "urn:a");
            var item2 = TestFixtures.Item("I2", "DOC-1-OUTRO-ARQUIVO.pdf", FolderId, "04 - ENGENHARIA", versionUrn: "urn:b");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(item1, item2));
            acc.ConfigurarDefinicoes(FolderId, new List<AccCustomAttributeDefinitionRef>());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "ATR-CODIGO_AMBIGUO");
            Assert.Equal(OperationalAuditErrorDomain.Operacional, finding.ErrorDomain);
            Assert.Equal(OperationalAuditClassification.AtributoAccAmbiguo, finding.Classificacao);
            Assert.Equal(OperationalAuditSeverity.Alto, finding.Severidade);
            Assert.Equal("DOC-1", finding.DocumentoNa);
            Assert.Contains("DOC-1-VersaoA.pdf", finding.NomeArquivo);
            Assert.Contains("DOC-1-OUTRO-ARQUIVO.pdf", finding.NomeArquivo); // ambos os candidatos relatados — nenhuma escolha arbitrária.
        }

        [Fact]
        public async Task CodigoAmbiguo_CandidatoUnico_NaoGeraFinding()
        {
            var (regra, acc, _, _, options) = NovaRegraComLdp("DOC-1");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "DOC-1.pdf", FolderId, "04 - ENGENHARIA")));
            acc.ConfigurarDefinicoes(FolderId, new List<AccCustomAttributeDefinitionRef>());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-CODIGO_AMBIGUO");
        }

        [Fact]
        public async Task CodigoAmbiguo_DoisCandidatosEquivalentes_MesmoNomeBase_NaoGeraAmbiguidade()
        {
            var (regra, acc, _, _, options) = NovaRegraComLdp("DOC-1");
            var itemDwg = TestFixtures.Item("I1", "DOC-1.dwg", FolderId, "04 - ENGENHARIA", versionUrn: "urn:a");
            var itemPdf = TestFixtures.Item("I2", "DOC-1.pdf", FolderId, "04 - ENGENHARIA", versionUrn: "urn:b");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(itemDwg, itemPdf));
            acc.ConfigurarDefinicoes(FolderId, new List<AccCustomAttributeDefinitionRef>());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-CODIGO_AMBIGUO");
        }

        [Fact]
        public async Task MultiplasExtensoes_DwgCompletoEPdfIncompleto_GeraFinding()
        {
            var (regra, acc, _, _, options) = NovaRegraComLdp("DOC-1");
            var itemDwg = TestFixtures.Item("I1", "DOC-1.dwg", FolderId, "04 - ENGENHARIA", versionUrn: "urn:dwg");
            var itemPdf = TestFixtures.Item("I2", "DOC-1.pdf", FolderId, "04 - ENGENHARIA", versionUrn: "urn:pdf");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(itemDwg, itemPdf));
            var definicoes = new List<AccCustomAttributeDefinitionRef> { new() { DefinitionId = 1, Name = "REV." } };
            acc.ConfigurarDefinicoes(FolderId, definicoes);
            acc.ConfigurarValores("urn:dwg", new List<AccCustomAttributeValueRef> { new() { Name = "REV.", ValorPresente = true, Valor = "A" } });
            acc.ConfigurarValores("urn:pdf", new List<AccCustomAttributeValueRef> { new() { Name = "REV.", ValorPresente = false, EvidenciaVisibilidadeExplicita = false } });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "ATR-MULTIPLAS_EXTENSOES_INCOMPLETAS");
            Assert.Contains("DWG=completo", finding.Evidencia);
            Assert.Contains("PDF=incompleto", finding.Evidencia);
        }

        [Fact]
        public async Task MultiplasExtensoes_PdfCompletoEDwgIncompleto_GeraFinding()
        {
            var (regra, acc, _, _, options) = NovaRegraComLdp("DOC-2");
            var itemDwg = TestFixtures.Item("I1", "DOC-2.dwg", FolderId, "04 - ENGENHARIA", versionUrn: "urn:dwg2");
            var itemPdf = TestFixtures.Item("I2", "DOC-2.pdf", FolderId, "04 - ENGENHARIA", versionUrn: "urn:pdf2");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(itemDwg, itemPdf));
            var definicoes = new List<AccCustomAttributeDefinitionRef> { new() { DefinitionId = 1, Name = "REV." } };
            acc.ConfigurarDefinicoes(FolderId, definicoes);
            acc.ConfigurarValores("urn:dwg2", new List<AccCustomAttributeValueRef> { new() { Name = "REV.", ValorPresente = false, EvidenciaVisibilidadeExplicita = false } });
            acc.ConfigurarValores("urn:pdf2", new List<AccCustomAttributeValueRef> { new() { Name = "REV.", ValorPresente = true, Valor = "B" } });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.Single(resultado.Findings, f => f.RegraId == "ATR-MULTIPLAS_EXTENSOES_INCOMPLETAS");
        }

        [Fact]
        public async Task MultiplasExtensoes_AmbosCompletos_NaoGeraFinding()
        {
            var (regra, acc, _, _, options) = NovaRegraComLdp("DOC-3");
            var itemDwg = TestFixtures.Item("I1", "DOC-3.dwg", FolderId, "04 - ENGENHARIA", versionUrn: "urn:dwg3");
            var itemPdf = TestFixtures.Item("I2", "DOC-3.pdf", FolderId, "04 - ENGENHARIA", versionUrn: "urn:pdf3");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(itemDwg, itemPdf));
            var definicoes = new List<AccCustomAttributeDefinitionRef> { new() { DefinitionId = 1, Name = "REV." } };
            acc.ConfigurarDefinicoes(FolderId, definicoes);
            acc.ConfigurarValores("urn:dwg3", new List<AccCustomAttributeValueRef> { new() { Name = "REV.", ValorPresente = true, Valor = "A" } });
            acc.ConfigurarValores("urn:pdf3", new List<AccCustomAttributeValueRef> { new() { Name = "REV.", ValorPresente = true, Valor = "A" } });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-MULTIPLAS_EXTENSOES_INCOMPLETAS");
        }

        [Fact]
        public async Task MultiplasExtensoes_DocumentoDeExtensaoUnica_NaoGeraFalsoPositivo()
        {
            var (regra, acc, _, _, options) = NovaRegraComLdp("DOC-4");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(TestFixtures.Item("I1", "DOC-4.pdf", FolderId, "04 - ENGENHARIA", versionUrn: "urn:pdf4")));
            var definicoes = new List<AccCustomAttributeDefinitionRef> { new() { DefinitionId = 1, Name = "REV." } };
            acc.ConfigurarDefinicoes(FolderId, definicoes);
            acc.ConfigurarValores("urn:pdf4", new List<AccCustomAttributeValueRef> { new() { Name = "REV.", ValorPresente = false, EvidenciaVisibilidadeExplicita = false } });

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-MULTIPLAS_EXTENSOES_INCOMPLETAS");
        }

        private static string[] AtributosDe(AuditAttributeStructureProfile config) => config.AtributosObrigatorios;

        // =========================================================================================
        // Correção pós-Checkpoint 2 — risco 1: matching por prefixo de tokens (nunca Contains cru).
        // Exemplos exatos pedidos na correção (seção 7).
        // =========================================================================================

        [Fact]
        public void Matching_CodigoEArquivoIdenticos_ComPdf_Casa()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-000-EL-TS-0005.pdf", "P-0602.1-000-EL-TS-0005"));
        }

        [Fact]
        public void Matching_MesmoDocumentoLogico_ExtensaoDwg_Casa()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-000-EL-TS-0005.dwg", "P-0602.1-000-EL-TS-0005"));
        }

        [Fact]
        public void Matching_SufixoDeRevisaoAceitoPelaRegraOperacional_Casa()
        {
            Assert.True(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-000-EL-TS-0005-05.pdf", "P-0602.1-000-EL-TS-0005"));
        }

        [Fact]
        public void Matching_SufixoColadoSemFronteiraDeToken_NaoCasa()
        {
            Assert.False(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "P-0602.1-000-EL-TS-00050.pdf", "P-0602.1-000-EL-TS-0005"));
        }

        [Fact]
        public void Matching_PrefixoIncompativelComARegraOperacional_NaoCasa()
        {
            Assert.False(DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(
                "XP-0602.1-000-EL-TS-0005.pdf", "P-0602.1-000-EL-TS-0005"));
        }

        [Fact]
        public void ResolverCodigo_NaVazio_UsaClienteComoFallback()
        {
            Assert.Equal("CLI-001", AuditDocumentCodeResolver.ResolverCodigoDaLinha("", "CLI-001"));
        }

        [Fact]
        public void ResolverCodigo_NaEClienteVazios_NuncaEscolheArbitrariamente()
        {
            Assert.Null(AuditDocumentCodeResolver.ResolverCodigoDaLinha("", " "));
        }

        [Fact]
        public void ResolverCodigo_NaPreenchido_TemPrioridadeSobreCliente()
        {
            Assert.Equal("NA-001", AuditDocumentCodeResolver.ResolverCodigoDaLinha("NA-001", "CLI-001"));
        }

        [Fact]
        public async Task CodigoAmbiguo_CandidatoComSufixoColado_NuncaContaComoCandidato_NaoGeraFalsaAmbiguidade()
        {
            // Prova direta de que Contains(codigo) não decide mais candidatos: o arquivo "...-00050.pdf"
            // contém a substring "P-0602.1-000-EL-TS-0005", mas é um documento DIFERENTE por regra de
            // fronteira de token (mesmo caso do bug histórico da Etapa 7 operacional). Com o Contains antigo,
            // isso geraria 2 candidatos e um falso ATR-CODIGO_AMBIGUO; com o matching real, é 0/1 candidato.
            var (regra, acc, _, _, options) = NovaRegraComLdp("P-0602.1-000-EL-TS-0005");
            var itemReal = TestFixtures.Item("I1", "P-0602.1-000-EL-TS-0005.pdf", FolderId, "04 - ENGENHARIA", versionUrn: "urn:real");
            var itemSufixoColado = TestFixtures.Item("I2", "P-0602.1-000-EL-TS-00050.pdf", FolderId, "04 - ENGENHARIA", versionUrn: "urn:colado");
            acc.ConfigurarListagem(FolderId, TestFixtures.Listagem(itemReal, itemSufixoColado));
            acc.ConfigurarDefinicoes(FolderId, new List<AccCustomAttributeDefinitionRef>());

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-CODIGO_AMBIGUO");
        }

        // =========================================================================================
        // Correção pós-Checkpoint 2 — risco 2: estrutura de pastas de PRODUCAO_MODELOS_3D nunca depende
        // de whitelist hardcoded (a antiga { "07.01.01", "07.01.02", "07.01.05", "08.01" }). Cobre as duas
        // árvores reais (07/08), disciplina configurável, subpastas variáveis e os 3 estados de decisão.
        // =========================================================================================

        [Fact]
        public async Task Estrutura07_SubpastaVariavel000_NaoGeraFinding()
        {
            var (regra, acc, _, _, _) = NovaRegra();
            var config = ConfigModelos3DComSubpastas("07.01.01 - AUTODESK REVIT");
            var pasta = TestFixtures.Pasta("F-3D", "07 - 3D AUTHORING SOFTWARE");
            var item = TestFixtures.Item("I1", "modelo.rvt", "F-3D", "AUTODESK REVIT", caminhoRelativo: "07.01.01 - AUTODESK REVIT/000");
            acc.ConfigurarListagem("F-3D", TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes("F-3D", new List<AccCustomAttributeDefinitionRef>());

            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            await regra.AuditarConfiguracaoAsync(TestFixtures.NovoContexto(), config, pasta, new HashSet<string>(), findings, requirements, default);

            Assert.DoesNotContain(findings, f => f.RegraId == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA");
        }

        [Fact]
        public async Task Estrutura07_SubpastaVariavel100_NaoGeraFinding()
        {
            var (regra, acc, _, _, _) = NovaRegra();
            var config = ConfigModelos3DComSubpastas("07.01.01 - AUTODESK REVIT");
            var pasta = TestFixtures.Pasta("F-3D", "07 - 3D AUTHORING SOFTWARE");
            var item = TestFixtures.Item("I1", "modelo.rvt", "F-3D", "AUTODESK REVIT", caminhoRelativo: "07.01.01 - AUTODESK REVIT/100");
            acc.ConfigurarListagem("F-3D", TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes("F-3D", new List<AccCustomAttributeDefinitionRef>());

            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            await regra.AuditarConfiguracaoAsync(TestFixtures.NovoContexto(), config, pasta, new HashSet<string>(), findings, requirements, default);

            Assert.DoesNotContain(findings, f => f.RegraId == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA");
        }

        [Fact]
        public async Task Estrutura07_SubpastaVariavelComNomeDeDisciplina_211Eletrocenter_NaoGeraFinding()
        {
            var (regra, acc, _, _, _) = NovaRegra();
            var config = ConfigModelos3DComSubpastas("07.01.01 - AUTODESK REVIT");
            var pasta = TestFixtures.Pasta("F-3D", "07 - 3D AUTHORING SOFTWARE");
            var item = TestFixtures.Item("I1", "modelo.rvt", "F-3D", "AUTODESK REVIT", caminhoRelativo: "07.01.01 - AUTODESK REVIT/211 - eletrocenter");
            acc.ConfigurarListagem("F-3D", TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes("F-3D", new List<AccCustomAttributeDefinitionRef>());

            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            await regra.AuditarConfiguracaoAsync(TestFixtures.NovoContexto(), config, pasta, new HashSet<string>(), findings, requirements, default);

            Assert.DoesNotContain(findings, f => f.RegraId == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA");
        }

        [Fact]
        public async Task Estrutura07_SoftwareNaoConfigurado_Desabilitado_GeraFinding()
        {
            // Config só habilita Revit/Civil3D — Plant3D fica de fora deliberadamente (equivalente a
            // "software desabilitado" no projeto), sem depender de nenhuma whitelist fixa.
            var (regra, acc, _, _, _) = NovaRegra();
            var config = ConfigModelos3DComSubpastas("07.01.01 - AUTODESK REVIT", "07.01.02 - AUTODESK CIVIL 3D");
            var pasta = TestFixtures.Pasta("F-3D", "07 - 3D AUTHORING SOFTWARE");
            var item = TestFixtures.Item("I1", "modelo.dwg", "F-3D", "AUTODESK PLANT 3D", caminhoRelativo: "07.01.05 - AUTODESK PLANT 3D/000");
            acc.ConfigurarListagem("F-3D", TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes("F-3D", new List<AccCustomAttributeDefinitionRef>());

            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            await regra.AuditarConfiguracaoAsync(TestFixtures.NovoContexto(), config, pasta, new HashSet<string>(), findings, requirements, default);

            var finding = Assert.Single(findings, f => f.RegraId == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA");
            Assert.Equal(OperationalAuditErrorDomain.Operacional, finding.ErrorDomain);
        }

        [Fact]
        public async Task Estrutura07_DisciplinaDiferenteDaPadrao_QuandoConfigurada_NaoGeraFinding()
        {
            // Prova que o mecanismo é genérico — nunca hardcoded para a disciplina "01": qualquer disciplina
            // configurada em CaminhosPermitidosRelativos é aceita, mesmo que não exista hoje na configuração
            // operacional real (a regra nunca transforma nomes conhecidos numa whitelist fixa universal).
            var (regra, acc, _, _, _) = NovaRegra();
            var config = ConfigModelos3DComSubpastas("07.06 - AUTODESK REVIT");
            var pasta = TestFixtures.Pasta("F-3D", "07 - 3D AUTHORING SOFTWARE");
            var item = TestFixtures.Item("I1", "modelo.rvt", "F-3D", "AUTODESK REVIT", caminhoRelativo: "07.06 - AUTODESK REVIT/PROJ-100");
            acc.ConfigurarListagem("F-3D", TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes("F-3D", new List<AccCustomAttributeDefinitionRef>());

            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            await regra.AuditarConfiguracaoAsync(TestFixtures.NovoContexto(), config, pasta, new HashSet<string>(), findings, requirements, default);

            Assert.DoesNotContain(findings, f => f.RegraId == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA");
        }

        [Fact]
        public async Task Estrutura08_ArquivoNaSubpastaRealConfigurada_NaoGeraFinding()
        {
            var (regra, acc, _, _, _) = NovaRegra();
            var config = ConfigModelos3D08ComSubpastas("08.01 - 3D AUTHORING SOFTWARE/08.01.02 - AUTODESK CIVIL 3D");
            var pasta = TestFixtures.Pasta("F-08", "08 - FEDERATED MODEL");
            var item = TestFixtures.Item("I1", "federado.nwd", "F-08", "AUTODESK CIVIL 3D",
                caminhoRelativo: "08.01 - 3D AUTHORING SOFTWARE/08.01.02 - AUTODESK CIVIL 3D/100");
            acc.ConfigurarListagem("F-08", TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes("F-08", new List<AccCustomAttributeDefinitionRef>());

            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            await regra.AuditarConfiguracaoAsync(TestFixtures.NovoContexto(), config, pasta, new HashSet<string>(), findings, requirements, default);

            Assert.DoesNotContain(findings, f => f.RegraId == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA");
        }

        [Fact]
        public async Task Estrutura08_ArquivoForaDasSubpastasConfiguradas_GeraFinding()
        {
            var (regra, acc, _, _, _) = NovaRegra();
            var config = ConfigModelos3D08ComSubpastas("08.01 - 3D AUTHORING SOFTWARE/08.01.02 - AUTODESK CIVIL 3D");
            var pasta = TestFixtures.Pasta("F-08", "08 - FEDERATED MODEL");
            var item = TestFixtures.Item("I1", "federado.nwd", "F-08", "PASTA QUALQUER", caminhoRelativo: "99.99 - FORA");
            acc.ConfigurarListagem("F-08", TestFixtures.Listagem(item));
            acc.ConfigurarDefinicoes("F-08", new List<AccCustomAttributeDefinitionRef>());

            var findings = new List<OperationalFinding>();
            var requirements = new List<OperationalRequirementResult>();
            await regra.AuditarConfiguracaoAsync(TestFixtures.NovoContexto(), config, pasta, new HashSet<string>(), findings, requirements, default);

            Assert.Single(findings, f => f.RegraId == "ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA");
        }

        [Fact]
        public async Task ArvoreDupla_07E08_QuandoAmbasExistem_AmbasSaoAuditadasIndependentemente()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var db = new FakeAuditDatabaseReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            acc.ConfigurarPasta("09.14", null);
            var pasta07 = TestFixtures.Pasta("F-07", "07 - 3D AUTHORING SOFTWARE");
            var pasta08 = TestFixtures.Pasta("F-08", "08 - FEDERATED MODEL");
            acc.ConfigurarPasta("07 - 3D AUTHORING SOFTWARE", pasta07);
            acc.ConfigurarPasta("08 - FEDERATED MODEL", pasta08);
            acc.ConfigurarListagem("F-07", new AccFolderListingResult());
            acc.ConfigurarListagem("F-08", new AccFolderListingResult());
            db.ConfigurarResposta("ACC_Attribute_Sync_State", new System.Data.DataTable());

            var options = new OperationalAuditOptions
            {
                ProjectProfiles = new Dictionary<string, AuditProjectProfile> { ["default"] = new AuditProjectProfile() }
            };
            var regra = new Stage07AttributesAuditRule(acc, db, workbook);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.Contains(resultado.Requirements, r => r.RequirementCode == "ATR-ESTRUTURA-PRODUCAO_MODELOS_3D_07" && r.Status == OperationalRequirementStatus.Approved);
            Assert.Contains(resultado.Requirements, r => r.RequirementCode == "ATR-ESTRUTURA-PRODUCAO_MODELOS_3D_08" && r.Status == OperationalRequirementStatus.Approved);
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-ESTRUTURA_OBRIGATORIA_AUSENTE");
        }

        [Fact]
        public async Task ArvoreDupla_Apenas07Existe_ComAlternativaConfigurada_08VirNaoAplicavel()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var db = new FakeAuditDatabaseReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            acc.ConfigurarPasta("09.14", null);
            var pasta07 = TestFixtures.Pasta("F-07", "07 - 3D AUTHORING SOFTWARE");
            acc.ConfigurarPasta("07 - 3D AUTHORING SOFTWARE", pasta07);
            acc.ConfigurarPasta("08 - FEDERATED MODEL", null);
            acc.ConfigurarListagem("F-07", new AccFolderListingResult());
            db.ConfigurarResposta("ACC_Attribute_Sync_State", new System.Data.DataTable());

            var options = new OperationalAuditOptions
            {
                ProjectProfiles = new Dictionary<string, AuditProjectProfile>
                {
                    ["default"] = new AuditProjectProfile
                    {
                        RequiredAttributeStructures = new List<string> { "PRODUCAO_MODELOS_3D_08" },
                        AlternativeStructures = new Dictionary<string, List<string>> { ["PRODUCAO_MODELOS_3D_08"] = new List<string> { "PRODUCAO_MODELOS_3D_07" } }
                    }
                }
            };
            var regra = new Stage07AttributesAuditRule(acc, db, workbook);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.Contains(resultado.Requirements, r => r.RequirementCode == "ATR-ESTRUTURA-PRODUCAO_MODELOS_3D_08" && r.Status == OperationalRequirementStatus.NotApplicable);
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-ESTRUTURA_OBRIGATORIA_AUSENTE");
        }

        [Fact]
        public async Task ArvoreDupla_08ObrigatoriaSemAlternativaEAusente_GeraFinding()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var db = new FakeAuditDatabaseReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            acc.ConfigurarPasta("09.14", null);
            acc.ConfigurarPasta("07 - 3D AUTHORING SOFTWARE", null);
            acc.ConfigurarPasta("08 - FEDERATED MODEL", null);

            var options = new OperationalAuditOptions
            {
                ProjectProfiles = new Dictionary<string, AuditProjectProfile>
                {
                    ["default"] = new AuditProjectProfile { RequiredAttributeStructures = new List<string> { "PRODUCAO_MODELOS_3D_08" } }
                }
            };
            var regra = new Stage07AttributesAuditRule(acc, db, workbook);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            var finding = Assert.Single(resultado.Findings, f => f.RegraId == "ATR-ESTRUTURA_OBRIGATORIA_AUSENTE" && f.ConfigurationKey == "PRODUCAO_MODELOS_3D_08");
            Assert.True(finding.BloqueiaEtapa);
        }

        [Fact]
        public async Task ArvoreDupla_NenhumaConfigurada_SemConfiguracaoSuficiente_NuncaGeraFinding()
        {
            var acc = new FakeAuditAccReadOnlyAdapter();
            var db = new FakeAuditDatabaseReadOnlyAdapter();
            var workbook = new FakeAuditWorkbookReadOnlyAdapter();
            acc.ConfigurarPasta("09.14", null);
            acc.ConfigurarPasta("07 - 3D AUTHORING SOFTWARE", null);
            acc.ConfigurarPasta("08 - FEDERATED MODEL", null);

            var options = new OperationalAuditOptions
            {
                ProjectProfiles = new Dictionary<string, AuditProjectProfile> { ["default"] = new AuditProjectProfile() }
            };
            var regra = new Stage07AttributesAuditRule(acc, db, workbook);

            var resultado = await regra.ExecutarAsync(TestFixtures.NovoContexto(options: options));

            Assert.Contains(resultado.Requirements, r => r.RequirementCode == "ATR-ESTRUTURA-PRODUCAO_MODELOS_3D_07" && r.Status == OperationalRequirementStatus.Inconclusive);
            Assert.Contains(resultado.Requirements, r => r.RequirementCode == "ATR-ESTRUTURA-PRODUCAO_MODELOS_3D_08" && r.Status == OperationalRequirementStatus.Inconclusive);
            Assert.DoesNotContain(resultado.Findings, f => f.RegraId == "ATR-ESTRUTURA_OBRIGATORIA_AUSENTE");
        }
    }
}
