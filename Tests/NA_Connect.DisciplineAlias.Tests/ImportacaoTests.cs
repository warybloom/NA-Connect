using APS.WebApp.Services.DisciplineAlias;

namespace NA_Connect.DisciplineAlias.Tests;

public sealed class ImportacaoTests
{
    private static readonly MapeamentoColunas Mapa = new("Sigla teste", "Nome canônico teste", "Tipo ctx teste", "Ctx teste", "Ativo teste");
    private static readonly string?[] Cab = ["Sigla teste", "Nome canônico teste", "Tipo ctx teste", "Ctx teste", "Ativo teste"];
    private const string Usuario = "teste@fixture.local";

    [Fact]
    public void Leitor_devolve_cabecalhos_reais_ignora_linhas_vazias_e_aceita_cabecalho_fora_da_linha_1()
    {
        var bytes = Planilha.CriarNaAba("Aba Teste", 3, ["Coluna X", null, "Coluna Y"], ["a", null, "b"], [null, null, null], ["c", null, "d"]);
        var p = SiglaPlanilhaReader.Ler(bytes);
        Assert.Equal("Aba Teste", p.Aba);
        Assert.Equal(["Coluna X", "Coluna Y"], p.Cabecalhos);
        Assert.Equal([4, 6], p.Linhas.Select(l => l.Numero));
        Assert.Equal("d", p.Linhas[1].Celulas["Coluna Y"]);
        Assert.Empty(p.Erros);
    }

    [Fact]
    public void Leitor_acusa_cabecalho_repetido_e_aba_inexistente()
    {
        var bytes = Planilha.Criar(["Sigla", "SIGLA "], ["XA", "XB"]);
        Assert.Contains(SiglaPlanilhaReader.Ler(bytes).Erros, e => e.Contains("repetido"));
        Assert.Contains(SiglaPlanilhaReader.Ler(bytes, "Outra").Erros, e => e.Contains("não encontrada"));
    }

    [Fact]
    public void Sugestao_de_colunas_so_por_nome_exato_e_nunca_obrigatoria()
    {
        Assert.Equal(new MapeamentoColunas("Sigla", "Disciplina Canônica", null, null, "Ativo"),
            SiglaPlanilhaReader.Sugerir(["Sigla", "Disciplina Canônica", "Ativo"]));
        Assert.Null(SiglaPlanilhaReader.Sugerir(["Cod", "Desc"]));
    }

    [Fact]
    public void Mapeamento_invalido_e_rejeitado()
    {
        var erros = SiglaImportacaoPlanner.ValidarMapeamento(new MapeamentoColunas("A", "A", null, "C"), ["A", "C"]);
        Assert.Contains(erros, e => e.Contains("dois papéis"));
        Assert.Contains(erros, e => e.Contains("tipo de contexto"));
        Assert.Contains(SiglaImportacaoPlanner.ValidarMapeamento(new MapeamentoColunas("Z", "A"), ["A"]), e => e.Contains("não existe"));
    }

    [Fact]
    public void Plano_classifica_linha_a_linha_com_duplicadas_conflitos_e_rejeitadas()
    {
        var bytes = Planilha.Criar(Cab,
            ["XA", "Teste Ãlfa", null, null, "SIM"],          // 2 incluir
            ["xa ", "Teste Ãlfa", null, null, null],          // 3 duplicada (mesma chave e nome)
            ["XB", "Teste Beta", null, null, null],           // 4 conflito no arquivo
            ["XB", "Teste Gama", null, null, null],           // 5 conflito no arquivo
            ["XB", "Teste Gama", "PROJETO", "TST901", null],  // 6 incluir (contexto diferente)
            ["", "Teste", null, null, null],                  // 7 rejeitada: sigla vazia
            ["XC", "Teste", null, null, "TALVEZ"],            // 8 rejeitada: ativo inválido
            ["XD", "Teste Delta", "FONTE", "timesheet", "não"]); // 9 incluir inativa
        var plano = SiglaImportacaoPlanner.Planejar(SiglaPlanilhaReader.Ler(bytes), Mapa, [], false);
        var acoes = plano.Linhas.ToDictionary(l => l.Linha, l => l.Acao);
        Assert.Equal(AcaoImportacao.Incluir, acoes[2]);
        Assert.Equal(AcaoImportacao.Duplicada, acoes[3]);
        Assert.Equal(AcaoImportacao.Rejeitada, acoes[4]);
        Assert.Equal(AcaoImportacao.Rejeitada, acoes[5]);
        Assert.Contains("Conflito no arquivo", plano.Linhas.Single(l => l.Linha == 4).Motivo);
        Assert.Equal(AcaoImportacao.Incluir, acoes[6]);
        Assert.Equal(AcaoImportacao.Rejeitada, acoes[7]);
        Assert.Equal(AcaoImportacao.Rejeitada, acoes[8]);
        var l9 = plano.Linhas.Single(l => l.Linha == 9);
        Assert.Equal((AcaoImportacao.Incluir, "TIMESHEET", false), (l9.Acao, l9.ContextoValor, l9.Ativo));
        Assert.Equal(new ResumoImportacao(3, 0, 0, 4, 1, 0), plano.Resumo);
    }

    [Fact]
    public async Task Importacao_repetida_e_idempotente_e_nao_apaga_regras_fora_do_arquivo()
    {
        var store = new MemoriaStore();
        var service = new SiglaDisciplinaService(store);
        var manual = await service.CriarAsync("XM", "Teste Manual", null, null, true, Usuario, default);
        var bytes = Planilha.Criar(Cab, ["XA", "Teste Ãlfa", null, null, null], ["T1", "Teste Ãlfa", null, null, null]);
        var hash = SiglaPlanilhaReader.Hash(bytes);

        var r1 = await service.ConfirmarAsync(bytes, "fixture.xlsx", hash, null, Mapa, false, Usuario, default);
        Assert.Equal(new ResumoImportacao(2, 0, 0, 0, 0, 0), r1.Resumo);
        var r2 = await service.ConfirmarAsync(bytes, "fixture.xlsx", hash, null, Mapa, false, Usuario, default);
        Assert.Equal(new ResumoImportacao(0, 0, 2, 0, 0, 0), r2.Resumo);

        Assert.Equal(3, store.Regras.Count);
        Assert.Contains(store.Regras, r => r.Id == manual.Id && r.DisciplinaCanonica == "Teste Manual");
        Assert.Equal(2, store.Importacoes.Count);
        Assert.Equal(3, store.Auditoria.Count(a => a.Acao == "INCLUIR"));
    }

    [Fact]
    public async Task Conflito_com_regra_existente_so_altera_com_confirmacao_explicita()
    {
        var store = new MemoriaStore();
        var service = new SiglaDisciplinaService(store);
        await service.CriarAsync("XA", "Teste Ãlfa", null, null, true, Usuario, default);
        var bytes = Planilha.Criar(Cab, ["XA", "Teste Ômega", null, null, null]);
        var hash = SiglaPlanilhaReader.Hash(bytes);

        var previa = await service.PreverAsync(bytes, null, Mapa, false, default);
        var linha = Assert.Single(previa.Plano!.Linhas);
        Assert.Equal((AcaoImportacao.Rejeitada, true, "Teste Ãlfa"), (linha.Acao, linha.ConflitoComExistente, linha.DisciplinaAtual));

        var semConfirmar = await service.ConfirmarAsync(bytes, "fixture.xlsx", hash, null, Mapa, false, Usuario, default);
        Assert.Equal(1, semConfirmar.Resumo.Rejeitadas);
        Assert.Equal("Teste Ãlfa", store.Regras.Single().DisciplinaCanonica);

        var confirmado = await service.ConfirmarAsync(bytes, "fixture.xlsx", hash, null, Mapa, true, Usuario, default);
        Assert.Equal(1, confirmado.Resumo.Alteradas);
        Assert.Equal("Teste Ômega", store.Regras.Single().DisciplinaCanonica);
        var auditoria = store.Auditoria.Last();
        Assert.Equal(("ALTERAR", "Teste Ãlfa", "Teste Ômega"), (auditoria.Acao, auditoria.Antes!.DisciplinaCanonica, auditoria.Depois!.DisciplinaCanonica));
    }

    [Fact]
    public async Task Coluna_ativo_ausente_nao_reativa_regra_desativada()
    {
        var store = new MemoriaStore();
        var service = new SiglaDisciplinaService(store);
        var r = await service.CriarAsync("XA", "Teste Ãlfa", null, null, false, Usuario, default);
        var bytes = Planilha.Criar(["Sigla teste", "Nome canônico teste"], ["XA", "Teste Ãlfa"]);
        var res = await service.ConfirmarAsync(bytes, "f.xlsx", SiglaPlanilhaReader.Hash(bytes), null, new MapeamentoColunas("Sigla teste", "Nome canônico teste"),
            false, Usuario, default);
        Assert.Equal(1, res.Resumo.Inalteradas);
        Assert.False(store.Regras.Single(x => x.Id == r.Id).Ativo);
    }

    [Fact]
    public async Task Confirmacao_exige_o_mesmo_arquivo_da_previa()
    {
        var service = new SiglaDisciplinaService(new MemoriaStore());
        var bytes = Planilha.Criar(Cab, ["XA", "Teste", null, null, null]);
        var ex = await Assert.ThrowsAsync<RegraInvalidaException>(() =>
            service.ConfirmarAsync(bytes, "f.xlsx", new string('0', 64), null, Mapa, false, Usuario, default));
        Assert.Contains("mesmo da prévia", ex.Message);
    }

    [Fact]
    public async Task Edicao_manual_valida_unicidade_versao_e_audita_ativar_desativar_excluir()
    {
        var store = new MemoriaStore();
        var service = new SiglaDisciplinaService(store);
        var a = await service.CriarAsync("XA", "Teste Ãlfa", null, null, true, Usuario, default);
        await Assert.ThrowsAsync<RegraDuplicadaException>(() => service.CriarAsync(" xa ", "Outro", null, null, true, Usuario, default));
        var b = await service.CriarAsync("XA", "Outro", "PROJETO", "TST901", true, Usuario, default); // contexto diferente: permitido
        var desativada = await service.DefinirAtivoAsync(a.Id, a.Versao, false, Usuario, default);
        Assert.False(desativada.Ativo);
        await Assert.ThrowsAsync<ConcorrenciaException>(() => service.DefinirAtivoAsync(a.Id, a.Versao, true, Usuario, default));
        Assert.Single(await service.AtivasAsync(default));
        await service.ExcluirAsync(b.Id, b.Versao, Usuario, default);
        Assert.Equal(("EXCLUIR", "Outro"), (store.Auditoria.Last().Acao, store.Auditoria.Last().Antes!.DisciplinaCanonica));
    }

    [Fact]
    public async Task Sem_migracao_aplicada_falha_explicitamente()
    {
        var service = new SiglaDisciplinaService(new MemoriaStore { Instalado = false });
        await Assert.ThrowsAsync<NormalizadorNaoInstaladoException>(() => service.ListarAsync(default));
    }

    [Fact]
    public void Arquivo_que_nao_e_xlsx_vira_erro_de_leitura_sem_excecao()
    {
        var p = SiglaPlanilhaReader.Ler("isto não é um xlsx"u8.ToArray());
        Assert.Contains(p.Erros, e => e.Contains("Não foi possível ler"));
        Assert.Empty(p.Linhas);
    }
}
