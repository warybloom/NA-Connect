using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using APS.WebApp.Services.Permissions;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Contratos do sistema de permissões entre backend (PermissionCatalog) e frontend/migração:
    /// nenhuma rota, item de menu, tela ou aba sem permissionKey; nenhuma key fora do catálogo; seed SQL = catálogo.
    /// </summary>
    public class PermissionFrontendContractTests
    {
        private static string Src(params string[] partes) =>
            RepoFileLocator.LerArquivoDaRaiz(new[] { "ClientApp", "src" }.Concat(partes).ToArray());

        private static string RoutePermissions() => Src("features", "permissions", "permissionLogic.ts");

        private static void TodasNoCatalogo(IEnumerable<string> keys, string origem)
        {
            var lista = keys.ToList();
            Assert.NotEmpty(lista);
            var fora = lista.Where(k => !PermissionCatalog.Existe(k)).ToList();
            Assert.True(fora.Count == 0, $"{origem}: keys fora do catálogo: {string.Join(", ", fora)}");
        }

        [Fact]
        public void Sidebar_TodaFolhaTemPermissionKey_ENoCatalogo_EOLayoutFiltra()
        {
            var layout = Src("components", "Layout.tsx");
            int ini = layout.IndexOf("const NAVEGACAO: NavNode[] = [");
            int fim = layout.IndexOf("\n];", ini);
            var bloco = layout.Substring(ini, fim - ini);
            // Cada "to:" (folha) precisa de permissionKey logo em seguida no mesmo nó (uma key ou lista any-of).
            var folhas = Regex.Matches(bloco, @"\bto:\s*""([^""]+)""(,\s*permissionKey:\s*(""[^""]+""|\[[^\]]+\]))?");
            Assert.True(folhas.Count >= 15);
            var semKey = folhas.Where(m => !m.Groups[3].Success).Select(m => m.Groups[1].Value).ToList();
            Assert.True(semKey.Count == 0, "Itens do sidebar sem permissionKey: " + string.Join(", ", semKey));
            TodasNoCatalogo(folhas.SelectMany(m => Regex.Matches(m.Groups[3].Value, @"""([^""]+)""").Select(k => k.Groups[1].Value)), "Layout NAVEGACAO");
            Assert.Contains("filtrarPorPermissao(NAVEGACAO, can)", layout);
            Assert.Contains("navegacaoVisivel.map(", layout);
            Assert.DoesNotContain("{NAVEGACAO.map(", layout);
            // Ramos gerados dos registries (sem segunda lista): Controle de Produção e Configuração de Templates.
            Assert.Contains("{ label: \"Controle de Produção\", icon: Factory, children: CONTROLE_PRODUCAO }", bloco);
            Assert.Contains("const CONTROLE_PRODUCAO: NavNode[] = navProducao().map(", layout);
            Assert.Contains("permissionKey: f.permissionKeys", layout);
            Assert.Contains("TEMPLATE_TYPES.map(", bloco);
            // Sem divisão Gerencial/Operacional; Home fora do sidebar; CDE → Documentos fora do menu; player da TV fora do menu.
            Assert.DoesNotContain("Gerencial\"", bloco);
            Assert.DoesNotContain("Operacional\"", bloco);
            Assert.DoesNotContain("to: \"/dashboard\"", bloco);
            Assert.DoesNotContain("to: \"/cde\"", bloco);
            Assert.DoesNotContain("to: \"/gestao-a-vista\"", bloco);
            Assert.DoesNotContain("to: \"/painel\"", bloco);
            Assert.Contains("label: \"Painel de Planejamento\"", bloco);
            Assert.DoesNotContain("Planejamento do Projeto", layout);
        }

        [Fact]
        public void PainelGerencialPmo_EmRelatorios_AbreRelatoriosReal_SemKeyNova()
        {
            var layout = Src("components", "Layout.tsx");
            Assert.Single(Regex.Matches(layout, @"label:\s*""Painel Gerencial PMO"",\s*icon:\s*\w+,\s*to:\s*""/relatorios"",\s*permissionKey:\s*""reports\.view""").Cast<Match>());
            Assert.Contains("<Route path=\"/relatorios\" element={<Reports />} />", Src("App.tsx")); // rota legada intacta
            Assert.True(PermissionCatalog.Existe("reports.view"));
            Assert.True(PermissionCatalog.Existe("production.managerial.indicadores.view"));
            Assert.True(PermissionCatalog.Existe("production.operational.indicadores.view"));
            Assert.True(PermissionCatalog.Existe("cde.document-list.view"));
            Assert.True(PermissionCatalog.Existe("settings.tv.configure"));
            Assert.True(PermissionCatalog.Existe("tv.view"));
            Assert.True(PermissionCatalog.Existe("robot.schedule.view"));
            Assert.Equal(129, PermissionCatalog.Todos.Count); // nenhuma key nova (a migração 002 continua igual ao catálogo)
        }

        [Fact]
        public void Templates_TodoTipoTemKeyDoCatalogo()
        {
            var tipos = Src("features", "settings", "templateTypes.ts");
            var keys = Regex.Matches(tipos, @"permissionKey:\s*""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(9, keys.Count);
            TodasNoCatalogo(keys, "templateTypes.ts");
        }

        [Fact]
        public void ControleDeProducao_CadaDashboardLiberadoPelaKeyGerencialOuOperacional_DoBackend()
        {
            var rotas = Src("features", "production", "productionRoutes.ts");
            // dash(slug, label, screenId, segmento) → keys production.<managerial|operational>.<segmento>.view
            var dashs = Regex.Matches(rotas, @"dash\(""([^""]+)"",\s*""[^""]+"",\s*""([^""]+)"",\s*""([^""]+)""\)")
                .Select(m => (Slug: m.Groups[1].Value, ScreenId: m.Groups[2].Value, Segmento: m.Groups[3].Value)).ToList();
            Assert.Equal(12, dashs.Count);
            Assert.Equal(PermissionCatalog.DashboardsProducao.Keys.OrderBy(x => x), dashs.Select(d => d.ScreenId).OrderBy(x => x));
            Assert.Contains("permissionKeys: [`production.managerial.${segmento}.view`, `production.operational.${segmento}.view`]", rotas);
            foreach (var d in dashs)
            {
                var esperadas = new[] { $"production.managerial.{d.Segmento}.view", $"production.operational.{d.Segmento}.view" };
                Assert.Equal(esperadas, PermissionCatalog.DashboardsProducao[d.ScreenId]);
                TodasNoCatalogo(esperadas, "dash " + d.Slug);
            }
            // Keys literais das demais folhas (Lista de Documentos, Curva S, Boletim, HH/Custo, Análise Financeira).
            TodasNoCatalogo(Regex.Matches(rotas, @"""((?:production|cde)\.[a-z-]+(?:\.[a-z-]+)*\.view)""").Select(m => m.Groups[1].Value), "productionRoutes.ts");
            // Telas com custo absoluto: nenhuma key operacional.
            var hhCusto = rotas.Substring(rotas.IndexOf("slug: \"controle-hh-custo\""), 300);
            Assert.DoesNotContain("production.operational.", hhCusto.Substring(0, hhCusto.IndexOf("},")));

            var pagina = Src("features", "production", "ControleProducaoPage.tsx");
            Assert.Contains("perm.can(folha.permissionKeys)", pagina);               // permissão da tela no projeto
            Assert.Contains("semVarianteLegada(", pagina);                            // rotas antigas /gerencial|/operacional redirecionam
            Assert.DoesNotContain("ProductionScreenNav", pagina);                     // sem cards de navegação
            Assert.False(System.IO.File.Exists(RepoFileLocator.CaminhoAbsoluto("ClientApp", "src", "features", "production", "ProductionScreenNav.tsx")));
        }

        [Fact]
        public void TelasTv_BackendProtegido_ConfigSoParaQuemConfigura_PlayerForaDoLayout()
        {
            var controller = RepoFileLocator.LerArquivoDaRaiz("Controllers", "TvConfigurationController.cs");
            Assert.Contains("[RequirePermission(\"settings.tv.configure\", \"tv.view\")]", controller); // GET: configura ou assiste
            Assert.Contains("[HttpPut]\n    [RequirePermission(\"settings.tv.configure\")]", controller.Replace("\r\n", "\n"));
            var app = Src("App.tsx");
            int layout = app.IndexOf("<Route element={<Layout />}>");
            Assert.True(app.IndexOf("<Route path=\"/gestao-a-vista\" element={<TvPlayerPage />} />") is var tv && tv > 0 && tv < layout);
            Assert.True(app.IndexOf("<Route path=\"/dashboard\" element={<DashboardPage />} />") is var home && home > 0 && home < layout);
            var config = Src("features", "settings", "TelasTvConfigPage.tsx");
            Assert.Contains("salvarConfiguracaoTv(config.revision", config);
            Assert.Contains("carregarConfiguracaoTv(", config);
        }

        [Fact]
        public void Sidebar_InicioConfiguracaoDoProjeto_ScrollProprio_AppShell_LogoUnica()
        {
            var layout = Src("components", "Layout.tsx");
            int ini = layout.IndexOf("const NAVEGACAO: NavNode[] = [");
            var bloco = layout.Substring(ini, layout.IndexOf("\n];", ini) - ini);
            // Configuração do Projeto logo abaixo de Norma de Coordenação; cada etapa LDP é item próprio.
            int norma = bloco.IndexOf("label: \"Norma de Coordenação\""), config = bloco.IndexOf("label: \"Configuração do Projeto\""), prog = bloco.IndexOf("label: \"Programação Semanal\"");
            Assert.True(norma > 0 && config > norma && prog > config, "Configuração do Projeto deve ficar entre Norma de Coordenação e Programação Semanal.");
            foreach (var item in new[] { "Template LDP", "Mapeamento do Cronograma", "Geração LDP Espelho", "Configuração LDP Controle", "Configuração de Templates" })
                Assert.Contains($"label: \"{item}\"", bloco);
            // Início (volta à Home), sidebar com rolagem própria e app-shell na altura da viewport.
            Assert.Contains("<NavLink to=\"/dashboard\" title=\"Início", layout);
            Assert.Contains("sidebar-scroll", layout);
            Assert.Contains("flex h-screen w-full overflow-hidden", layout);
            Assert.Contains(".sidebar-scroll", Src("index.css"));
            // Painel do Planejador não embute mais a configuração de projetos.
            Assert.DoesNotContain("ControlPanel", Src("features", "planning", "PainelPlanejadorPage.tsx"));
            // Logo: um único componente/URL; nenhuma outra tela monta o endereço da imagem.
            var identidade = Src("features", "project", "ProjectIdentity.tsx");
            Assert.Contains("/api/project-identity/", identidade);
            var outros = System.IO.Directory.GetFiles(RepoFileLocator.CaminhoAbsoluto("ClientApp", "src"), "*.tsx", System.IO.SearchOption.AllDirectories)
                .Where(f => !f.EndsWith("ProjectIdentity.tsx") && System.IO.File.ReadAllText(f).Contains("/api/project-identity/")).ToList();
            Assert.True(outros.Count == 0, "Endereço da logo fora do ProjectIdentity: " + string.Join(", ", outros));
        }

        [Fact]
        public void TodaRotaDoAppTemPermissao_ETodaKeyDeRotaExiste()
        {
            var app = Src("App.tsx");
            var mapa = RoutePermissions();
            var padroes = Regex.Matches(mapa, @"pattern:\s*""([^""]+)""").Select(m => m.Groups[1].Value).ToHashSet();

            var planning = Src("features", "planning", "index.ts");
            var planningPaths = Regex.Matches(planning, @"(\w+):\s*'(/planejamento/[^']+)'").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

            var rotas = new List<string>();
            foreach (Match m in Regex.Matches(app, @"path=(?:""([^""]+)""|\{planningRoutePaths\.(\w+)\})"))
            {
                if (m.Groups[2].Success) { rotas.Add(planningPaths[m.Groups[2].Value]); continue; }
                var p = m.Groups[1].Value;
                if (p == "*" || p == "/login") continue; // fallback e página pública
                rotas.Add(p.StartsWith("/") ? p : "/configuracao-ldp/" + p); // únicas rotas relativas: filhas de /configuracao-ldp
            }
            Assert.True(rotas.Count >= 30);
            var semMapa = rotas.Where(r => !padroes.Contains(r)).ToList();
            Assert.True(semMapa.Count == 0, "Rotas sem entrada em ROUTE_PERMISSIONS: " + string.Join(", ", semMapa));

            var keys = Regex.Matches(mapa, @"""([a-z0-9-]+(?:\.[a-z0-9-]+)+)""").Select(m => m.Groups[1].Value)
                .Where(k => k.EndsWith(".view") || k.EndsWith(".manage") || k.EndsWith(".edit"));
            TodasNoCatalogo(keys, "ROUTE_PERMISSIONS");

            // Toda rota autenticada passa pelo guard (Layout e master-admin).
            Assert.Equal(2, Regex.Matches(app, @"<Route element=\{<PermissionRouteGuard />\}>").Count);
            Assert.Contains("<PermissionProvider>", app);
        }

        [Fact]
        public void AbasECanDoFrontend_UsamKeysDoCatalogo()
        {
            var cde = Src("pages", "cde", "CdeProjetoPage.tsx");
            TodasNoCatalogo(Regex.Matches(cde, @"permissionKey:\s*""([^""]+)""").Select(m => m.Groups[1].Value), "CDE abas");
            Assert.Equal(3, Regex.Matches(cde, @"permissionKey:\s*""").Count);

            var robo = Src("features", "robot-configuration", "RobotConfigurationPage.tsx");
            Assert.Contains("can('robot.configure')", robo);
            Assert.Contains("can('robot.run')", robo);
            TodasNoCatalogo(Regex.Matches(robo, @"can\('([^']+)'\)").Select(m => m.Groups[1].Value), "Robô");
        }

        [Fact]
        public void AdministracaoDePermissoes_NaoUsaPopupNativoDoNavegador()
        {
            var raiz = RepoFileLocator.CaminhoAbsoluto("ClientApp", "src", "features", "permissions");
            var popups = new Regex(@"window\.(prompt|confirm|alert)\s*\(|(?<![\w.])(prompt|confirm|alert)\s*\(");
            var comPopup = System.IO.Directory.EnumerateFiles(raiz, "*.ts*", System.IO.SearchOption.AllDirectories)
                .Where(f => popups.IsMatch(System.IO.File.ReadAllText(f)))
                .Select(System.IO.Path.GetFileName)
                .ToList();
            Assert.True(comPopup.Count == 0, "Popup nativo (prompt/confirm/alert) em: " + string.Join(", ", comPopup));
        }

        [Fact]
        public void Migracao_SeedDoCatalogoIgualAoPermissionCatalog_ESemDrop()
        {
            // A migração MAIS RECENTE de permissões sincroniza o catálogo inteiro (upsert, sem remoção).
            var sql = RepoFileLocator.LerArquivoDaRaiz("Migrations", "Permissions", "20260929_002_Permissions_Navegacao.sql");
            int ini = sql.IndexOf("-- BEGIN CATALOG");
            int fim = sql.IndexOf("-- END CATALOG");
            var linhas = Regex.Matches(sql.Substring(ini, fim - ini),
                @"\(N'([^']+)', (NULL|N'([^']*)'), N'((?:[^']|'')*)', N'(\w+)', N'(\w+)', (NULL|N'[^']*'), (\d+)\)");
            var seed = linhas.Select(m => (Key: m.Groups[1].Value, Parent: m.Groups[3].Success ? m.Groups[3].Value : null,
                Type: m.Groups[5].Value, Scope: m.Groups[6].Value)).ToList();
            var catalogo = PermissionCatalog.Todos.Select(r => (r.Key, Parent: r.ParentKey, r.Type, r.Scope)).ToList();
            Assert.Equal(catalogo, seed);

            var semComentarios = Regex.Replace(sql, @"/\*.*?\*/|--[^\n]*", "", RegexOptions.Singleline);
            Assert.DoesNotMatch(@"(?i)\bDROP\b|\bTRUNCATE\b|\bDELETE\b|\bALTER\s+TABLE\s+dbo\.", semComentarios);
        }
    }
}
