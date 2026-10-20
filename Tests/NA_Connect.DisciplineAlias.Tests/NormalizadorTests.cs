using System.Text.Json;
using APS.WebApp.Services.DisciplineAlias;

namespace NA_Connect.DisciplineAlias.Tests;

public sealed class NormalizadorTests
{
    private sealed record RegraJson(long Id, string Sigla, string Disciplina, string? ContextoTipo, string? ContextoValor, bool Ativo);
    private sealed record Esperado(string Nome, string Origem, bool Conflito);
    private sealed record Caso(string Nome, string?[] Candidatos, string Rotulo, string[] Projetos, string? Fonte, Esperado Esperado);
    private sealed record Arquivo(RegraJson[] Regras, Caso[] Casos);

    private static Arquivo Fixture() => JsonSerializer.Deserialize<Arquivo>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "casos_resolucao.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    public static IEnumerable<object[]> Casos() => Fixture().Casos.Select(c => new object[] { c.Nome });

    [Theory]
    [MemberData(nameof(Casos))]
    public void Resolucao_segue_prioridade_contexto_global_original(string nome)
    {
        var f = Fixture();
        var caso = f.Casos.Single(c => c.Nome == nome);
        var regras = f.Regras.Select(r => new RegraSigla(r.Id, r.Sigla, r.Disciplina, r.ContextoTipo, r.ContextoValor, r.Ativo, "MANUAL", null, 1,
            DateTime.UtcNow, "teste", DateTime.UtcNow, "teste")).ToList();
        var res = SiglaNormalizer.Resolver(regras, caso.Candidatos, caso.Rotulo, new ContextoResolucao(caso.Projetos, caso.Fonte));
        Assert.Equal(caso.Esperado.Nome, res.Nome);
        Assert.Equal(caso.Esperado.Origem, res.Origem.ToString());
        Assert.Equal(caso.Esperado.Conflito, res.Conflito is not null);
    }

    [Fact]
    public void Chave_normaliza_espacos_e_caixa_mas_preserva_unicode()
    {
        Assert.Equal("ELÉTRICA E INSTRUMENTAÇÃO", SiglaNormalizer.Chave("  elétrica \t e  instrumentação "));
        Assert.Equal("Elétrica e Instrumentação", SiglaNormalizer.Limpar("  Elétrica   e Instrumentação "));
        // NFD (e + acento combinante) e NFC são a mesma chave.
        Assert.Equal(SiglaNormalizer.Chave("Elétrica"), SiglaNormalizer.Chave("Elétrica"));
        Assert.NotEqual(SiglaNormalizer.Chave("ELÉTRICA"), SiglaNormalizer.Chave("ELETRICA"));
    }

    [Theory]
    [InlineData("", "Teste", null, null, "Sigla vazia.")]
    [InlineData("XA", " ", null, null, "Disciplina canônica vazia.")]
    [InlineData("XA", "Teste", null, "TST901", "Contexto informado sem tipo")]
    [InlineData("XA", "Teste", "CLIENTE", "TST901", "inválido")]
    [InlineData("XA", "Teste", "PROJETO", "", "sem valor")]
    [InlineData("XA", "Teste", "FONTE", "PLANILHA", "Fonte 'PLANILHA' inválida")]
    public void Validacao_rejeita_dado_incompleto_sem_completar(string sigla, string disciplina, string? tipo, string? valor, string erro)
    {
        var (regra, erros) = SiglaNormalizer.Validar(sigla, disciplina, tipo, valor, true);
        Assert.Null(regra);
        Assert.Contains(erros, e => e.Contains(erro));
    }

    [Fact]
    public void Validacao_limpa_texto_e_normaliza_fonte()
    {
        var (regra, erros) = SiglaNormalizer.Validar("  xa ", " Teste   Ãlfa ", "fonte", "timesheet", false);
        Assert.Empty(erros);
        Assert.Equal(new DadosRegra("xa", "Teste Ãlfa", "FONTE", "TIMESHEET", false), regra);
    }
}
