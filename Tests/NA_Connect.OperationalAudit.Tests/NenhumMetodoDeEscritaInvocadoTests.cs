using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using APS.WebApp.Services;
using APS.WebApp.Services.OperationalAudit;
using NA_Connect.Services;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Canario estrutural: garante que NENHUM tipo do subsistema de auditoria depende de um serviço
    /// operacional de escrita, por DUAS vias independentes — nenhuma delas sozinha é suficiente:
    ///
    /// (1) reflexão sobre o assembly COMPILADO (nunca falha por causa de comentários/strings no
    ///     código-fonte, mas só alcança referências de TIPO — nunca alcançaria uma string SQL);
    /// (2) inspeção do TEXTO-FONTE de cada .cs em Services\OperationalAudit (alcança exatamente o que a
    ///     reflexão não alcança: nomes de método usados como texto/chamada, strings SQL, verbos HTTP
    ///     literais) — nunca dependendo só de referências de tipo.
    ///
    /// Nesta fase (Checkpoint 1) isso e' trivialmente verdadeiro — nao existe nenhuma regra nem orquestrador
    /// ainda. O valor deste teste e' ser um alarme que dispara IMEDIATAMENTE no Checkpoint 2+ se alguem, por
    /// engano, injetar AutodeskApiService/LdpUpdateService/etc. (em vez de um adaptador somente-leitura) em
    /// uma regra de auditoria, ou colar uma string SQL de escrita, ou montar uma requisição HTTP de escrita.
    /// </summary>
    public class NenhumMetodoDeEscritaInvocadoTests
    {
        private static readonly Type[] ServicosOperacionaisProibidos =
        {
            typeof(AutodeskApiService),
            typeof(LdpUpdateService),
            typeof(AccReviewWorkflowService),
            typeof(AccScheduleSyncService),
            typeof(GrdAutomationWorker),
            // Checkpoint 3 — os 13 arquivos operacionais protegidos completos (seção 1 do pedido):
            // DatabaseService, GrdToLdpSyncService, AccReviewStageSnapshotService, LdpToCustoSyncService,
            // TimesheetDumpService, PlanningControlSyncService, QualityControlSyncService. Nunca inclui
            // DocumentMatchingService aqui — suas funções puras de matching são explicitamente reutilizadas
            // pela auditoria (Stage02/04/05), nunca proibidas.
            typeof(DatabaseService),
            typeof(GrdToLdpSyncService),
            typeof(AccReviewStageSnapshotService),
            typeof(LdpToCustoSyncService),
            typeof(TimesheetDumpService),
            typeof(PlanningControlSyncService),
            typeof(QualityControlSyncService)
        };

        private static readonly string[] NomesDeMetodoDeEscritaOperacionalConhecidos =
        {
            "CreateNewVersionAsync",
            "UploadFileToFolderAsync",
            "UpdateCustomAttributesForVersionAsync",
            "RunApprovalVerdictSyncAsync",
            "Save",
            "SaveAs",
            // Checkpoint 3 (seção 21 do pedido).
            "ResetAndDumpTimesheetAsync",
            "UpdateLdpFromDatabaseAsync",
            "SyncGrdsToLdpAsync",
            "UpdateWorkflowStatusAsync",
            "SincronizarSnapshotAsync",
            "ExecuteFullSyncProcessAsync",
            "SalvarDadosLdpUnificada",
            "PersistirTabelaAsync",
            "SincronizarTabelaDinamicaAsync"
        };

        private const BindingFlags TodosOsMembrosDeclaradosNoProprioTipo =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static IEnumerable<Type> TiposDaAuditoria() =>
            typeof(OperationalAuditResult).Assembly.GetTypes()
                .Where(t => t.Namespace != null && t.Namespace.StartsWith("APS.WebApp.Services.OperationalAudit", StringComparison.Ordinal));

        private static IEnumerable<Type> TodosOsTiposReferenciadosPor(Type tipo)
        {
            foreach (var campo in tipo.GetFields(TodosOsMembrosDeclaradosNoProprioTipo)) yield return campo.FieldType;
            foreach (var prop in tipo.GetProperties(TodosOsMembrosDeclaradosNoProprioTipo)) yield return prop.PropertyType;

            foreach (var ctor in tipo.GetConstructors(TodosOsMembrosDeclaradosNoProprioTipo))
                foreach (var p in ctor.GetParameters()) yield return p.ParameterType;

            foreach (var metodo in tipo.GetMethods(TodosOsMembrosDeclaradosNoProprioTipo))
            {
                yield return metodo.ReturnType;
                foreach (var p in metodo.GetParameters()) yield return p.ParameterType;
            }
        }

        [Fact]
        public void NenhumTipoDaAuditoriaReferenciaDiretamenteUmServicoOperacionalDeEscrita()
        {
            var violacoes = new List<string>();

            foreach (var tipo in TiposDaAuditoria())
            {
                foreach (var referenciado in TodosOsTiposReferenciadosPor(tipo).Distinct())
                {
                    foreach (var proibido in ServicosOperacionaisProibidos)
                    {
                        if (referenciado == proibido)
                        {
                            violacoes.Add($"{tipo.FullName} referencia diretamente {proibido.FullName}");
                        }
                    }
                }
            }

            Assert.True(violacoes.Count == 0,
                "A auditoria operacional nao pode depender diretamente de um servico operacional de escrita:\n" + string.Join("\n", violacoes));
        }

        [Fact]
        public void NenhumTipoDaAuditoriaDeclaraMetodoComNomeDeEscritaOperacionalConhecido()
        {
            var violacoes = new List<string>();

            foreach (var tipo in TiposDaAuditoria())
            {
                foreach (var metodo in tipo.GetMethods(TodosOsMembrosDeclaradosNoProprioTipo))
                {
                    if (NomesDeMetodoDeEscritaOperacionalConhecidos.Contains(metodo.Name))
                    {
                        violacoes.Add($"{tipo.FullName}.{metodo.Name}");
                    }
                }
            }

            Assert.True(violacoes.Count == 0,
                "Metodo com nome de uma operacao de escrita operacional conhecida foi declarado dentro da auditoria:\n" + string.Join("\n", violacoes));
        }

        [Fact]
        public void SqlWriteVerbGuardEAuditHttpWriteGuardHandler_ExistemNoAssembly_ComoCamadaDeDefesa()
        {
            // Confirma que as duas camadas de defesa em profundidade (seção 18) realmente fazem parte do
            // assembly compilado — se um deles for removido/renomeado sem atualizar este teste, o teste
            // falha em vez de silenciosamente parar de proteger nada.
            var assembly = typeof(OperationalAuditResult).Assembly;

            Assert.NotNull(assembly.GetType("APS.WebApp.Services.OperationalAudit.Safety.SqlWriteVerbGuard"));
            Assert.NotNull(assembly.GetType("APS.WebApp.Services.OperationalAudit.Safety.AuditHttpWriteGuardHandler"));
        }

        // =========================================================================================
        // Reforço de Checkpoint 1, item 9 — inspeção do TEXTO-FONTE (não reflexão) de todo .cs em
        // Services\OperationalAudit, para alcançar o que reflexão sobre tipos nunca alcançaria: nomes de
        // método usados como chamada, strings SQL embutidas, verbos HTTP literais.
        // =========================================================================================

        private static readonly string[] MetodosDeEscritaProibidosComoChamada =
        {
            "CreateNewVersionAsync(",
            "UploadFileToFolderAsync(",
            "UpdateCustomAttributesForVersionAsync(",
            "RunApprovalVerdictSyncAsync(",
            "Package.Save(",
            "Package.SaveAs(",
            ".SaveAs(",
            // Checkpoint 3 (seção 21 do pedido) — nenhuma regra nova pode chamar o método operacional real
            // que ela audita; só pode reconstruir a LEITURA equivalente via adaptadores somente-leitura.
            "ResetAndDumpTimesheetAsync(",
            "UpdateLdpFromDatabaseAsync(",
            "SyncGrdsToLdpAsync(",
            "UpdateWorkflowStatusAsync(",
            "SincronizarSnapshotAsync(",
            "ExecuteFullSyncProcessAsync(",
            "SalvarDadosLdpUnificada(",
            "PersistirAsync(",
            "PersistirTabelaAsync(",
            "SincronizarTabelaDinamicaAsync(",
            "SincronizarPlanilhaEspecificaAsync("
        };

        private static readonly string[] VerbosHttpDeEscritaLiterais =
        {
            "HttpMethod.Post", "HttpMethod.Put", "HttpMethod.Patch", "HttpMethod.Delete"
        };

        private static readonly string[] TabelasOperacionaisProibidas =
        {
            "dbo.", "Ldp_Unificada", "Cronograma_Projetos", "ACC_Attribute_Sync_State",
            "ACC_Review_Current_Stage", "tb_ExternalActionLog", "tb_InternalActionLog",
            "tb_ScopeDeviation", "tb_HoldControl", "tb_WeeklyEngineeringSchedule",
            "tbTimeSheetRealizadoDocumento" // Checkpoint 3 (Stage01).
        };

        /// <summary>
        /// Checkpoint 4, seção 17: amplia a varredura textual para também cobrir o executor manual
        /// (<c>Tools\NA_Connect.OperationalAudit.Cli\</c>) e o composition root — nunca só
        /// <c>Services\OperationalAudit\</c>. Mesma garantia, agora em dois diretórios.
        /// </summary>
        private static IEnumerable<(string Caminho, string Conteudo)> ArquivosFonteDaAuditoria()
        {
            foreach (var raizRelativa in new[] { new[] { "Services", "OperationalAudit" }, new[] { "Tools", "NA_Connect.OperationalAudit.Cli" } })
            {
                string pasta = RepoFileLocator.CaminhoAbsoluto(raizRelativa);
                if (!Directory.Exists(pasta)) continue;

                foreach (var caminho in Directory.GetFiles(pasta, "*.cs", SearchOption.AllDirectories))
                {
                    if (caminho.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                        caminho.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                    yield return (caminho, File.ReadAllText(caminho));
                }
            }
        }

        [Fact]
        public void PastaDaAuditoriaFoiEncontradaEContemArquivosFonte()
        {
            Assert.NotEmpty(ArquivosFonteDaAuditoria());
        }

        [Fact]
        public void NenhumArquivoFonteDaAuditoriaChamaUmMetodoDeEscritaOperacionalProibido()
        {
            var violacoes = new List<string>();
            foreach (var (caminho, conteudo) in ArquivosFonteDaAuditoria())
            {
                foreach (var metodo in MetodosDeEscritaProibidosComoChamada)
                {
                    if (conteudo.Contains(metodo, StringComparison.Ordinal))
                    {
                        violacoes.Add($"{Path.GetFileName(caminho)} contém a chamada proibida '{metodo}'");
                    }
                }
            }
            Assert.True(violacoes.Count == 0, string.Join("\n", violacoes));
        }

        [Fact]
        public void NenhumArquivoFonteDaAuditoriaReferenciaUmVerboHttpDeEscritaLiteralmente()
        {
            // AuditHttpWriteGuardHandler.cs é a ÚNICA exceção esperada — ele existe justamente para
            // BLOQUEAR esses verbos, então precisa nomeá-los no código de bloqueio. Qualquer OUTRO arquivo
            // referenciando HttpMethod.Post/Put/Patch/Delete seria um HttpClient de escrita construído fora
            // do guard.
            var violacoes = new List<string>();
            foreach (var (caminho, conteudo) in ArquivosFonteDaAuditoria())
            {
                if (Path.GetFileName(caminho) == "AuditHttpWriteGuardHandler.cs") continue;

                foreach (var verbo in VerbosHttpDeEscritaLiterais)
                {
                    if (conteudo.Contains(verbo, StringComparison.Ordinal))
                    {
                        violacoes.Add($"{Path.GetFileName(caminho)} referencia '{verbo}' fora do guard.");
                    }
                }
            }
            Assert.True(violacoes.Count == 0, string.Join("\n", violacoes));
        }

        [Fact]
        public void NenhumArquivoFonteDaAuditoriaEscreveEmUmaTabelaOperacionalProibida()
        {
            // Checkpoint 2 introduziu regras que LEEM (SELECT-only, via IAuditDatabaseReadOnlyAdapter, já
            // guardado por SqlWriteVerbGuard) tabelas operacionais legítimas — Cronograma_Projetos (CRN-012),
            // ACC_Attribute_Sync_State (ATR-CACHE), Projetos (DNA-001/005). Isso é esperado e seguro; o que
            // nunca pode acontecer é uma dessas tabelas aparecer como ALVO DE ESCRITA (INSERT/UPDATE/DELETE)
            // em qualquer lugar do código-fonte da auditoria — daí o escopo à instrução, não ao identificador
            // solto (que "UnicoDestinoDeEscritaSqlNoTextoFonteEhOSchemaAudit" já cobre de forma mais ampla:
            // TODO INSERT/UPDATE do código da auditoria tem que apontar para "audit." — isto aqui é a mesma
            // garantia, só reafirmada objetivamente contra a lista nomeada de tabelas operacionais).
            var violacoes = new List<string>();
            var padraoComandoSql = new Regex(@"CommandText\s*=\s*@?""(?<sql>[\s\S]*?)"";");
            var padraoEscrita = new Regex(@"(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+([\[\]\w\.\s]+?)(?=\s|\(|;|$)", RegexOptions.IgnoreCase);

            foreach (var (caminho, conteudo) in ArquivosFonteDaAuditoria())
            {
                foreach (Match comando in padraoComandoSql.Matches(conteudo))
                {
                    string sql = comando.Groups["sql"].Value;
                    foreach (Match escrita in padraoEscrita.Matches(sql))
                    {
                        string alvo = escrita.Groups[2].Value.Trim();
                        foreach (var tabela in TabelasOperacionaisProibidas)
                        {
                            if (alvo.Contains(tabela, StringComparison.OrdinalIgnoreCase))
                            {
                                violacoes.Add($"{Path.GetFileName(caminho)}: comando de escrita contra '{alvo}' (tabela proibida '{tabela}')");
                            }
                        }
                    }
                }
            }
            Assert.True(violacoes.Count == 0, string.Join("\n", violacoes));
        }

        [Fact]
        public void UnicoDestinoDeEscritaSqlNoTextoFonteEhOSchemaAudit()
        {
            // Só dentro de strings efetivamente atribuídas a "CommandText" — nunca no texto inteiro do
            // arquivo, onde um comentário/doc mencionando "UPDATE" (ex.: "decide entre INSERT ou UPDATE")
            // não é um comando SQL real e não pode virar falso positivo.
            var violacoes = new List<string>();
            var padraoComandoSql = new Regex(@"CommandText\s*=\s*@?""(?<sql>[\s\S]*?)"";");
            var padraoEscrita = new Regex(@"(INSERT INTO|UPDATE)\s+([\[\]\w\.]+)", RegexOptions.IgnoreCase);

            foreach (var (caminho, conteudo) in ArquivosFonteDaAuditoria())
            {
                foreach (Match comando in padraoComandoSql.Matches(conteudo))
                {
                    string sql = comando.Groups["sql"].Value;
                    foreach (Match m in padraoEscrita.Matches(sql))
                    {
                        string alvo = m.Groups[2].Value.Replace("[", "").Replace("]", "");
                        if (!alvo.StartsWith("audit.", StringComparison.OrdinalIgnoreCase))
                        {
                            violacoes.Add($"{Path.GetFileName(caminho)}: '{m.Value}' não aponta para o schema audit.");
                        }
                    }
                }
            }
            Assert.True(violacoes.Count == 0, string.Join("\n", violacoes));
        }

        [Fact]
        public void UnicoDestinoDeEscritaEmDiscoEhOOutputDirectory_NenhumCaminhoAbsolutoFixoDeOutraArea()
        {
            // Os três writers de IMPLEMENTAÇÃO (Txt/Csv/Json — nunca a interface IOperationalAuditReportWriter,
            // que não escreve nada sozinha) só devem escrever via
            // ReportFileNaming.GarantirDiretorioECaminho (que recebe outputDirectory por parâmetro) — nunca
            // um caminho fixo hardcoded fora dele.
            var violacoes = new List<string>();
            foreach (var (caminho, conteudo) in ArquivosFonteDaAuditoria())
            {
                string nomeArquivo = Path.GetFileName(caminho);
                bool ehImplementacaoDeWriter = Regex.IsMatch(nomeArquivo, @"^Operational.*ReportWriter\.cs$");
                if (!ehImplementacaoDeWriter) continue;

                bool usaHelperCentral = conteudo.Contains("ReportFileNaming.GarantirDiretorioECaminho", StringComparison.Ordinal);
                Assert.True(usaHelperCentral, $"{nomeArquivo} deveria escrever via ReportFileNaming.GarantirDiretorioECaminho.");
            }
        }
    }
}
