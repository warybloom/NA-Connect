using APS.WebApp.Services.Tv;

namespace NA_Connect.Planning.Tests;

/// <summary>Configuração de Telas para TV: salvar → reabrir → usar (store JSON com revisão, backup e validação).</summary>
public class TvConfigurationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tv-config-tests-" + Guid.NewGuid().ToString("N"));
    private string Arquivo => Path.Combine(_dir, "tv-configuration.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* limpeza best-effort */ } }

    private const string Hub = "b.11111111-1111-1111-1111-111111111111", Proj = "b.22222222-2222-2222-2222-222222222222";

    private static TvPresentation Apresentacao(string id = "sala-reuniao", int segundos = 30, params string[] telas) =>
        new(id, "Sala de reunião", segundos, (telas.Length == 0 ? new[] { "ldp", "hold" } : telas)
            .Select(t => new TvSlide(t, Hub, Proj, "CHS001 - ETHANOL PLANT")).ToArray());

    [Fact]
    public async Task SemArquivo_ConfiguracaoVazia()
    {
        var c = await new TvConfigurationStore(Arquivo).ReadAsync();
        Assert.Equal(0, c.Revision);
        Assert.Empty(c.Presentations);
    }

    [Fact]
    public async Task Salvar_Reabrir_MantemConfiguracao_EIncrementaRevisao()
    {
        var store = new TvConfigurationStore(Arquivo);
        var salva = await store.SaveAsync(new[] { Apresentacao() }, expectedRevision: 0, savedBy: "admin@na.com");
        Assert.Equal(1, salva.Revision);

        // "Reabrir": nova instância lendo o mesmo arquivo (como após reiniciar o app).
        var reaberta = await new TvConfigurationStore(Arquivo).ReadAsync();
        var a = Assert.Single(reaberta.Presentations);
        Assert.Equal(("sala-reuniao", "Sala de reunião", 30), (a.Id, a.Name, a.SecondsPerScreen));
        Assert.Equal(new[] { "ldp", "hold" }, a.Slides.Select(s => s.ScreenId));
        Assert.Equal("CHS001 - ETHANOL PLANT", a.Slides[0].ProjectName);
        Assert.Equal("admin@na.com", reaberta.SavedBy);

        var segunda = await store.SaveAsync(new[] { Apresentacao(segundos: 45) }, expectedRevision: 1, savedBy: null);
        Assert.Equal(2, segunda.Revision);
        Assert.True(File.Exists(Arquivo + ".bak"));                                  // versão anterior preservada
    }

    [Fact]
    public async Task EdicaoConcorrente_RecebeConflito_NaoSobrescreve()
    {
        var store = new TvConfigurationStore(Arquivo);
        await store.SaveAsync(new[] { Apresentacao() }, 0, null);
        await Assert.ThrowsAsync<TvConfigurationConflictException>(() => store.SaveAsync(new[] { Apresentacao(segundos: 90) }, 0, null));
        Assert.Equal(30, (await store.ReadAsync()).Presentations.Single().SecondsPerScreen);
    }

    [Theory]
    [InlineData("tela-inexistente", 30)]   // só dashboards do Controle de Produção
    [InlineData("ldp", 5)]                 // tempo mínimo
    [InlineData("ldp", 601)]               // tempo máximo
    public async Task Validacao_RecusaTelaOuTempoInvalido(string tela, int segundos)
    {
        var store = new TvConfigurationStore(Arquivo);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new[] { Apresentacao("a1", segundos, tela) }, 0, null));
        Assert.False(File.Exists(Arquivo));
    }

    [Fact]
    public async Task Validacao_RecusaIdsDuplicadosProjetoInvalidoEApresentacaoVazia()
    {
        var store = new TvConfigurationStore(Arquivo);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new[] { Apresentacao("x"), Apresentacao("x") }, 0, null));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new[] { new TvPresentation("x", "X", 30, new[] { new TvSlide("ldp", "hub", Proj, null) }) }, 0, null));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new[] { new TvPresentation("x", "X", 30, Array.Empty<TvSlide>()) }, 0, null));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new[] { Apresentacao("ID INVALIDO") }, 0, null));
    }

    [Fact]
    public async Task ArquivoCorrompido_BloqueiaSemSobrescrever()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Arquivo, "{ isto não é json");
        var store = new TvConfigurationStore(Arquivo);
        await Assert.ThrowsAsync<TvConfigurationException>(() => store.ReadAsync());
        await Assert.ThrowsAsync<TvConfigurationException>(() => store.SaveAsync(new[] { Apresentacao() }, 0, null));
        Assert.Equal("{ isto não é json", await File.ReadAllTextAsync(Arquivo));
    }

    /// <summary>As 16 telas do catálogo antigo — apresentações já salvas referenciam esses ids (nunca podem sair).</summary>
    private static readonly string[] TelasAntigas = { "ldp", "nivelamento-programacao", "controle-programacao", "desvio-qualidade", "cap-interno",
        "cap-externo", "change-order", "controle-documental", "hold", "desvio-escopo", "rnc", "programacao", "curva-s", "resumo", "analise-financeira", "gestao-a-vista" };

    private static readonly string[] TelasJamile = { "gestao-a-vista-visao-geral", "gestao-a-vista-programacao", "gestao-a-vista-documentacao",
        "gestao-a-vista-indicadores", "gestao-a-vista-hh-disciplina", "gestao-a-vista-custo-disciplina" };

    [Fact]
    public void TelasPermitidas_CatalogoCompletoDoControleDeProducao_PreservaIdsAntigos()
    {
        // 30 telas implementadas do Controle de Produção (12 dashboards + 18) + painel PMO antigo "gestao-a-vista"
        // + 10 Gestão à Vista Tipo 2 (19/10/2026).
        Assert.Equal(41, TvConfigurationRules.TelasPermitidas.Count);
        Assert.Equal(TvTelas.Todas.Count, TvTelas.Todas.Select(t => t.Id).Distinct().Count());
        foreach (var t in TelasAntigas.Concat(TelasJamile).Concat(new[] { "resumo-tv-teste", "lista-documentos", "gestao-hh-custo", "controle-hh-custo",
                     "lancamentos-pessoas", "lancamentos-disciplinas", "lancamentos-documentos", "analise-financeira-projeto", "analise-financeira-documento" }))
            Assert.Contains(t, TvConfigurationRules.TelasPermitidas);
        // Em desenvolvimento: nunca aceitas.
        foreach (var t in new[] { "boletim-medicao", "indicadores-producao", "analise-financeira-pessoa" })
            Assert.DoesNotContain(t, TvConfigurationRules.TelasPermitidas);
    }

    [Fact]
    public void TelasDashboard_MesmosIdsDoRegistry_EMesmasKeysDoCatalogoDePermissoes()
    {
        foreach (var id in APS.WebApp.Services.Reports.Dashboards.ProductionDashboards.Todos.Keys)
        {
            var tela = TvTelas.Obter(id);
            Assert.NotNull(tela);
            Assert.True(tela!.MultiProjeto, id);                                      // união das linhas no backend
            Assert.Equal(APS.WebApp.Services.Permissions.PermissionCatalog.DashboardsProducao[id], tela.PermissionKeys);
        }
        var catalogo = APS.WebApp.Services.Permissions.PermissionCatalog.Todos.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var t in TvTelas.Todas) Assert.All(t.PermissionKeys, k => Assert.Contains(k, catalogo));
        // Telas com custo/HH gerencial não são liberadas por key operacional.
        foreach (var id in new[] { "gestao-hh-custo", "controle-hh-custo", "gestao-a-vista-indicadores", "gestao-a-vista-custo-disciplina",
                     "analise-financeira", "analise-financeira-projeto", "analise-financeira-documento" })
            Assert.DoesNotContain(TvTelas.Obter(id)!.PermissionKeys, k => k.StartsWith("production.operational.", StringComparison.Ordinal));
        Assert.Equal(new[] { "permissions.manage" }, TvTelas.Obter("resumo-tv-teste")!.PermissionKeys);
    }

    [Fact]
    public void Autorizadas_SoTelasComKeyDoUsuario_MasterVeTodas()
    {
        Assert.Equal(41, TvTelas.Autorizadas(true, Array.Empty<string>()).Count);
        Assert.Empty(TvTelas.Autorizadas(false, Array.Empty<string>()));
        var operacional = TvTelas.Autorizadas(false, new[] { "production.operational.ldp.view", "reports.gestao.view" });
        Assert.Contains("ldp", operacional);
        Assert.Contains("gestao-a-vista-visao-geral", operacional);
        Assert.DoesNotContain("gestao-a-vista-custo-disciplina", operacional);
        Assert.DoesNotContain("controle-hh-custo", operacional);
        Assert.DoesNotContain("resumo-tv-teste", operacional);
        Assert.DoesNotContain("hold", operacional);
        // Gestão à Vista Tipo 2: mesma key das páginas do BI (reports.gestao.view).
        Assert.All(TelasTipo2, id => Assert.Contains(id, operacional));
        Assert.All(TelasTipo2, id => Assert.DoesNotContain(id, TvTelas.Autorizadas(false, new[] { "production.operational.ldp.view" })));
    }

    // ---------------------------------------------------------------------------------------------
    // Gestão à Vista Tipo 2 (19/10/2026): 10 páginas prioritárias do BI CHS001, um projeto por item, EXCLUSIVAS da CHS001.
    // ---------------------------------------------------------------------------------------------
    private const string Chs001 = "b.fcdb7ebd-1b23-440d-a04b-90391098522e", OutroProjeto = "b.33333333-3333-3333-3333-333333333333";

    private static readonly string[] TelasTipo2 = { "gestao-vista-tipo2-resumo-30", "gestao-vista-tipo2-resumo-60", "gestao-vista-tipo2-resumo-90",
        "gestao-vista-tipo2-ldp-3d-30", "gestao-vista-tipo2-ldp-3d-60", "gestao-vista-tipo2-ldp-3d-90",
        "gestao-vista-tipo2-ldp-engineering-30", "gestao-vista-tipo2-ldp-engineering-60", "gestao-vista-tipo2-ldp-engineering-90",
        "gestao-vista-tipo2-programacao" };

    private static TvPresentation ApresentacaoTipo2(string projeto, params string[] telas) =>
        new("tv-tipo2", "TV Gestão à Vista Tipo 2", 30, telas.Select(t => new TvSlide(t, "", projeto, "CHS001 - ETHANOL PLANT")).ToArray());

    [Fact]
    public void Tipo2_ExatamenteDezTelas_Unico_ReportsGestaoView_ExclusivasDaChs001()
    {
        Assert.Equal(Chs001, TvTelas.ProjetoChs001);
        var tipo2 = TvTelas.Todas.Where(t => t.Id.StartsWith("gestao-vista-tipo2-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(TelasTipo2, tipo2.Select(t => t.Id));
        Assert.All(tipo2, t =>
        {
            Assert.False(t.MultiProjeto, t.Id);
            Assert.Equal(new[] { "reports.gestao.view" }, t.PermissionKeys);
            Assert.Equal(Chs001, t.ProjetoExclusivo);
            Assert.Contains(t.Id, TvConfigurationRules.TelasPermitidas);
        });
        // Nenhuma outra tela tem restrição de projeto; as 13 páginas não prioritárias do BI não são aceitas.
        Assert.All(TvTelas.Todas.Except(tipo2), t => Assert.Null(t.ProjetoExclusivo));
        foreach (var slug in new[] { "capa", "resumo-geral", "progress-to-date", "planned-activities", "3d-model", "cap-externo", "cap-interno",
                     "controle-de-desvio", "controle-de-holds", "nao-conformidades", "controle-de-hora-e-custo", "controle-de-hora-e-custo-gestao", "controle-de-qualidade" })
            Assert.Null(TvTelas.Obter("gestao-vista-tipo2-" + slug));
    }

    [Fact]
    public async Task Tipo2_ComChs001_SalvaEReabre_JuntoComTelasExistentes()
    {
        var store = new TvConfigurationStore(Arquivo);
        var slides = TelasTipo2.Select(t => new TvSlide(t, "", Chs001, "CHS001 - ETHANOL PLANT"))
            .Append(new TvSlide("ldp", Hub, Proj, "OUTRO")).ToArray();
        await store.SaveAsync(new[] { new TvPresentation("tv-tipo2", "TV ENGENHARIA", 30, slides) }, 0, "admin@na.com");
        var reaberta = Assert.Single((await new TvConfigurationStore(Arquivo).ReadAsync()).Presentations);
        Assert.Equal(TelasTipo2.Append("ldp"), reaberta.Slides.Select(s => s.ScreenId));
        Assert.All(reaberta.Slides.Take(10), s => { Assert.Equal(Chs001, s.ProjectId); Assert.Null(s.Projetos); });
    }

    [Fact]
    public async Task Tipo2_OutroProjeto_RecusadoMesmoPorApi_SemGravar()
    {
        var store = new TvConfigurationStore(Arquivo);
        foreach (var tela in TelasTipo2)
        {
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new[] { ApresentacaoTipo2(OutroProjeto, tela) }, 0, null));
            Assert.Contains("exclusiva do projeto CHS001", ex.Message);
            // Comparação exata: o mesmo GUID em maiúsculas não é aceito.
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new[] { ApresentacaoTipo2("b." + Chs001[2..].ToUpperInvariant(), tela) }, 0, null));
            // CHS001 como principal + outro projeto na lista: recusado (um projeto por item e exclusiva).
            var combinado = new TvPresentation("tv", "TV", 30, new[] { new TvSlide(tela, "", Chs001, null,
                new[] { new TvSlideProjeto("", Chs001, null), new TvSlideProjeto("", OutroProjeto, null) }) });
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new[] { combinado }, 0, null));
        }
        Assert.False(File.Exists(Arquivo));
        // Um arquivo editado à mão com tela Tipo 2 de outro projeto não é aceito na leitura (nunca exibe outro projeto).
        Assert.Throws<ArgumentException>(() => TvConfigurationRules.Validate(new TvConfiguration(1, 1, null, null, new[] { ApresentacaoTipo2(OutroProjeto, TelasTipo2[0]) })));
        Assert.NotNull(TvConfigurationRules.Validate(new TvConfiguration(1, 1, null, null, new[] { ApresentacaoTipo2(Chs001, TelasTipo2) })));
    }

    [Fact]
    public async Task TelasJamile_UmProjeto_E_ConsolidadoCom17Projetos_NoMesmoSlide_SalvamEReabrem()
    {
        var store = new TvConfigurationStore(Arquivo);
        // 1 projeto por slide (formato antigo) continua igual.
        var slides = TelasJamile.Select(t => new TvSlide(t, "", Proj, "CHS001 - ETHANOL PLANT")).ToArray();
        await store.SaveAsync(new[] { new TvPresentation("tv-engenharia", "TV ENGENHARIA", 30, slides) }, 0, null);
        var reaberta = await new TvConfigurationStore(Arquivo).ReadAsync();
        Assert.Equal(TelasJamile, reaberta.Presentations.Single().Slides.Select(s => s.ScreenId));
        Assert.All(reaberta.Presentations.Single().Slides, s => Assert.Null(s.Projetos));

        // Consolidado: as seis telas aceitam vários projetos em UM slide (17 projetos, principal = primeiro).
        Assert.All(TelasJamile, t => Assert.True(TvTelas.Obter(t)!.MultiProjeto, t));
        var dezessete = Enumerable.Range(1, 17).Select(i => new TvSlideProjeto("", $"b.{i:D8}-0000-0000-0000-000000000000", $"PRJ{i:D3}")).ToArray();
        var consolidados = TelasJamile.Select(t => new TvSlide(t, "", dezessete[0].ProjectId, dezessete[0].ProjectName, dezessete)).ToArray();
        var salva = await store.SaveAsync(new[] { new TvPresentation("tv-engenharia", "TV ENGENHARIA", 30, consolidados) }, 1, null);
        Assert.Equal(2, salva.Revision);
        var lida = (await new TvConfigurationStore(Arquivo).ReadAsync()).Presentations.Single();
        Assert.All(lida.Slides, s => Assert.Equal(dezessete.Select(p => p.ProjectId), s.Projetos!.Select(p => p.ProjectId)));
        // Telas que exibem um projeto por vez continuam recusando vários projetos.
        var erro = await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new[] { new TvPresentation("tv-engenharia", "TV ENGENHARIA", 30,
            new[] { new TvSlide("lancamentos-pessoas", "", dezessete[0].ProjectId, "PRJ001", dezessete) }) }, 2, null));
        Assert.Contains("um projeto por vez", erro.Message);
        Assert.Equal(2, (await store.ReadAsync()).Revision);                        // nada foi gravado
    }

    [Fact]
    public async Task ConfiguracaoReal_GravadaAntesDestaVersao_ContinuaValida_EAceitaTelaDaJamile()
    {
        // Formato EXATO do App_Data/tv-configuration.json em uso (14/10/2026), inclusive "estaAtiva" e "projetos": null.
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Arquivo, """
            { "version": 1, "revision": 31, "savedAt": "2026-10-14T15:15:04.5432165+00:00", "savedBy": "planejador@na.com",
              "presentations": [
                { "id": "apresentacao-1", "name": "Apresentação 1", "secondsPerScreen": 10,
                  "slides": [ { "screenId": "programacao", "hubId": "", "projectId": "b.fcdb7ebd-1b23-440d-a04b-90391098522e", "projectName": "CHS001 - ETHANOL PLANT", "projetos": null } ],
                  "ativa": true, "atualizadoEm": "2026-10-14T15:15:04.5432165+00:00", "estaAtiva": true },
                { "id": "tv-2", "name": "TV 2", "secondsPerScreen": 10,
                  "slides": [
                    { "screenId": "nivelamento-programacao", "hubId": "b.2ee333e9-3e57-42dd-ad89-86864dd86845", "projectId": "b.fcdb7ebd-1b23-440d-a04b-90391098522e", "projectName": "CHS001 - ETHANOL PLANT", "projetos": null },
                    { "screenId": "controle-programacao", "hubId": "b.2ee333e9-3e57-42dd-ad89-86864dd86845", "projectId": "b.fcdb7ebd-1b23-440d-a04b-90391098522e", "projectName": "CHS001 - ETHANOL PLANT", "projetos": null } ],
                  "ativa": false, "atualizadoEm": "2026-10-05T23:53:30.5780789+00:00", "estaAtiva": false } ] }
            """);
        var store = new TvConfigurationStore(Arquivo);
        var c = await store.ReadAsync();
        Assert.Equal(31, c.Revision);
        Assert.Equal(new[] { "Apresentação 1", "TV 2" }, c.Presentations.Select(p => p.Name));
        Assert.Equal(new[] { true, false }, c.Presentations.Select(p => p.EstaAtiva));
        Assert.Equal(new[] { "nivelamento-programacao", "controle-programacao" }, c.Presentations[1].Slides.Select(s => s.ScreenId));
        // Acrescentar uma tela da Jamile à TV existente: só ela muda; a outra TV mantém data, status e ordem.
        var nova = c.Presentations.Select(p => p.Id == "apresentacao-1"
            ? p with { Slides = p.Slides.Append(new TvSlide("gestao-a-vista-visao-geral", "", p.Slides[0].ProjectId, p.Slides[0].ProjectName)).ToArray() } : p).ToArray();
        var salva = await store.SaveAsync(nova, 31, "planejador@na.com");
        Assert.Equal(32, salva.Revision);
        Assert.Equal(c.Presentations[1].AtualizadoEm, salva.Presentations[1].AtualizadoEm);
        Assert.False(salva.Presentations[1].EstaAtiva);
        Assert.Equal(10, salva.Presentations[0].SecondsPerScreen);
        Assert.Equal(new[] { "programacao", "gestao-a-vista-visao-geral" }, (await new TvConfigurationStore(Arquivo).ReadAsync()).Presentations[0].Slides.Select(s => s.ScreenId));
        Assert.True(File.Exists(Arquivo + ".bak"));                                  // backup do arquivo anterior
    }

    private const string Proj2 = "b.33333333-3333-3333-3333-333333333333";

    [Fact]
    public async Task Multiprojeto_PorItem_SalvaEReabre_SemVazarEntreItens()
    {
        var store = new TvConfigurationStore(Arquivo);
        var unico = new TvSlide("curva-s", Hub, Proj, "CHS001");
        var multi = new TvSlide("curva-s", Hub, Proj, "CHS001", new[] { new TvSlideProjeto(Hub, Proj, "CHS001"), new TvSlideProjeto(Hub, Proj2, "BML011.016") });
        await store.SaveAsync(new[] { new TvPresentation("tv-diretoria", "TV DIRETORIA", 30, new[] { unico, multi }) }, 0, null);
        var slides = (await new TvConfigurationStore(Arquivo).ReadAsync()).Presentations.Single().Slides;
        Assert.Null(slides[0].Projetos);                                                   // item 1: só CHS001
        Assert.Equal(new[] { Proj, Proj2 }, slides[1].Projetos!.Select(p => p.ProjectId));  // item 2: CHS001 + BML011.016
    }

    [Fact]
    public async Task Multiprojeto_EmQualquerTela_EPrincipalPrecisaSerOPrimeiro()
    {
        var store = new TvConfigurationStore(Arquivo);
        var dois = new[] { new TvSlideProjeto(Hub, Proj, "A"), new TvSlideProjeto(Hub, Proj2, "B") };
        var salva = await store.SaveAsync(new[] { new TvPresentation("a", "A", 30, new[] { new TvSlide("ldp", Hub, Proj, "A", dois), new TvSlide("resumo", Hub, Proj, "A", dois) }) }, 0, null);
        Assert.All(salva.Presentations[0].Slides, s => Assert.Equal(2, s.Projetos!.Count));
        store = new TvConfigurationStore(Arquivo + ".b");
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new[] { new TvPresentation("a", "A", 30, new[] { new TvSlide("curva-s", Hub, Proj2, "B", dois) }) }, 0, null));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new[] { new TvPresentation("a", "A", 30,
            new[] { new TvSlide("curva-s", Hub, Proj, "A", new[] { dois[0], dois[0] }) }) }, 0, null));
    }

    [Fact]
    public async Task CurvaS_PodeSerAdicionada_Salva_EReaberta()
    {
        var store = new TvConfigurationStore(Arquivo);
        await store.SaveAsync(new[] { Apresentacao("tv-pmo", 30, "curva-s", "ldp") }, 0, null);
        var reaberta = await new TvConfigurationStore(Arquivo).ReadAsync();
        Assert.Equal(new[] { "curva-s", "ldp" }, reaberta.Presentations.Single().Slides.Select(s => s.ScreenId));
    }

    [Fact]
    public async Task ArquivoAntigo_SemAtivaNemAtualizadoEm_ContinuaValido_EAtivo()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Arquivo, $$"""
            { "version": 1, "revision": 3, "savedAt": null, "savedBy": null, "presentations": [
              { "id": "sala", "name": "Sala", "secondsPerScreen": 30,
                "slides": [ { "screenId": "ldp", "hubId": "{{Hub}}", "projectId": "{{Proj}}", "projectName": "CHS001" } ] } ] }
            """);
        var c = await new TvConfigurationStore(Arquivo).ReadAsync();
        var a = Assert.Single(c.Presentations);
        Assert.True(a.EstaAtiva);
        Assert.Null(a.AtualizadoEm);
        Assert.Equal(3, c.Revision);
    }

    [Fact]
    public async Task NomeDaTela_Persiste_DuasTvsDistintas_StatusEUltimaAtualizacaoPorTv()
    {
        var store = new TvConfigurationStore(Arquivo);
        var eng = Apresentacao("tv-sala-de-engenharia") with { Name = "TV SALA DE ENGENHARIA" };
        var dir = Apresentacao("tv-diretoria") with { Name = "TV DIRETORIA", Ativa = false };
        await store.SaveAsync(new[] { eng, dir }, 0, null);

        var reaberto = await new TvConfigurationStore(Arquivo).ReadAsync();            // fechar e abrir novamente
        Assert.Equal(new[] { "TV SALA DE ENGENHARIA", "TV DIRETORIA" }, reaberto.Presentations.Select(p => p.Name));
        Assert.Equal(new[] { true, false }, reaberto.Presentations.Select(p => p.EstaAtiva));
        Assert.All(reaberto.Presentations, p => Assert.NotNull(p.AtualizadoEm));
        var dataEng = reaberto.Presentations[0].AtualizadoEm;

        // Só a TV alterada recebe nova data de atualização.
        await Task.Delay(20);
        var alterada = reaberto.Presentations.Select(p => p.Id == "tv-diretoria" ? p with { SecondsPerScreen = 45 } : p).ToArray();
        var salva = await store.SaveAsync(alterada, reaberto.Revision, null);
        Assert.Equal(dataEng, salva.Presentations[0].AtualizadoEm);
        Assert.NotEqual(reaberto.Presentations[1].AtualizadoEm, salva.Presentations[1].AtualizadoEm);
    }
}
