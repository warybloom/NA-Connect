namespace APS.WebApp.Services.DisciplineAlias;

/// <summary>Persistência auditável das regras (schema próprio normalizador.*; nada no banco legado).</summary>
public interface ISiglaDisciplinaStore
{
    Task<bool> InstaladoAsync(CancellationToken ct);
    Task<IReadOnlyList<RegraSigla>> ListarAsync(CancellationToken ct);
    Task<RegraSigla> CriarAsync(DadosRegra dados, string usuario, CancellationToken ct);
    Task<RegraSigla> AtualizarAsync(long id, int versao, DadosRegra dados, string usuario, CancellationToken ct);
    Task ExcluirAsync(long id, int versao, string usuario, CancellationToken ct);
    /// <summary>
    /// Aplica a importação numa transação: relê as regras com bloqueio, chama <paramref name="planejar"/> sobre o estado
    /// ATUAL e grava inclusões/alterações + auditoria + registro da importação. Linhas rejeitadas/inalteradas não gravam regra.
    /// </summary>
    Task<ResultadoImportacao> AplicarImportacaoAsync(Func<IReadOnlyList<RegraSigla>, PlanoImportacao> planejar, string nomeArquivo,
        string hashSha256, string usuario, CancellationToken ct);
    Task<IReadOnlyList<ImportacaoRegistrada>> ListarImportacoesAsync(int maximo, CancellationToken ct);
}

public sealed record PreviaImportacao(string HashSha256, string Aba, IReadOnlyList<string> Abas, IReadOnlyList<string> Cabecalhos,
    MapeamentoColunas? MapeamentoSugerido, MapeamentoColunas? Mapeamento, IReadOnlyList<string> Erros, PlanoImportacao? Plano,
    ResumoImportacao? Resumo, IReadOnlyList<IReadOnlyDictionary<string, string?>> Amostra);

/// <summary>Regras de negócio do normalizador: validação única para edição manual e importação.</summary>
public sealed class SiglaDisciplinaService(ISiglaDisciplinaStore store)
{
    public Task<bool> InstaladoAsync(CancellationToken ct) => store.InstaladoAsync(ct);
    public Task<IReadOnlyList<RegraSigla>> ListarAsync(CancellationToken ct) => store.ListarAsync(ct);
    public Task<IReadOnlyList<ImportacaoRegistrada>> ListarImportacoesAsync(CancellationToken ct) => store.ListarImportacoesAsync(50, ct);

    public async Task<IReadOnlyList<RegraSigla>> AtivasAsync(CancellationToken ct) =>
        (await store.ListarAsync(ct)).Where(r => r.Ativo).ToList();

    public async Task<RegraSigla> CriarAsync(string? sigla, string? disciplina, string? contextoTipo, string? contextoValor, bool ativo,
        string usuario, CancellationToken ct)
    {
        var dados = Validar(sigla, disciplina, contextoTipo, contextoValor, ativo);
        var existentes = await store.ListarAsync(ct);
        GarantirUnica(existentes, dados, null);
        return await store.CriarAsync(dados, usuario, ct);
    }

    public async Task<RegraSigla> AtualizarAsync(long id, int versao, string? sigla, string? disciplina, string? contextoTipo,
        string? contextoValor, bool ativo, string usuario, CancellationToken ct)
    {
        var dados = Validar(sigla, disciplina, contextoTipo, contextoValor, ativo);
        var existentes = await store.ListarAsync(ct);
        if (existentes.All(r => r.Id != id)) throw new RegraNaoEncontradaException(id);
        GarantirUnica(existentes, dados, id);
        return await store.AtualizarAsync(id, versao, dados, usuario, ct);
    }

    public async Task<RegraSigla> DefinirAtivoAsync(long id, int versao, bool ativo, string usuario, CancellationToken ct)
    {
        var atual = (await store.ListarAsync(ct)).FirstOrDefault(r => r.Id == id) ?? throw new RegraNaoEncontradaException(id);
        return await store.AtualizarAsync(id, versao,
            new DadosRegra(atual.SiglaOriginal, atual.DisciplinaCanonica, atual.ContextoTipo, atual.ContextoValor, ativo), usuario, ct);
    }

    public Task ExcluirAsync(long id, int versao, string usuario, CancellationToken ct) => store.ExcluirAsync(id, versao, usuario, ct);

    public async Task<PreviaImportacao> PreverAsync(byte[] arquivo, string? aba, MapeamentoColunas? mapeamento, bool aplicarAlteracoes,
        CancellationToken ct)
    {
        var hash = SiglaPlanilhaReader.Hash(arquivo);
        var planilha = SiglaPlanilhaReader.Ler(arquivo, aba);
        var sugerido = SiglaPlanilhaReader.Sugerir(planilha.Cabecalhos);
        var amostra = planilha.Linhas.Take(10).Select(l => l.Celulas).ToList();
        var erros = planilha.Erros.ToList();
        if (mapeamento is null || erros.Count > 0)
            return new PreviaImportacao(hash, planilha.Aba, planilha.Abas, planilha.Cabecalhos, sugerido, mapeamento, erros, null, null, amostra);
        erros.AddRange(SiglaImportacaoPlanner.ValidarMapeamento(mapeamento, planilha.Cabecalhos));
        if (erros.Count > 0)
            return new PreviaImportacao(hash, planilha.Aba, planilha.Abas, planilha.Cabecalhos, sugerido, mapeamento, erros, null, null, amostra);
        var plano = SiglaImportacaoPlanner.Planejar(planilha, mapeamento, await store.ListarAsync(ct), aplicarAlteracoes);
        return new PreviaImportacao(hash, planilha.Aba, planilha.Abas, planilha.Cabecalhos, sugerido, mapeamento, erros, plano, plano.Resumo, amostra);
    }

    /// <summary>Aplica a importação confirmada. O hash garante que é o MESMO arquivo da prévia.</summary>
    public async Task<ResultadoImportacao> ConfirmarAsync(byte[] arquivo, string nomeArquivo, string hashPrevia, string? aba,
        MapeamentoColunas mapeamento, bool aplicarAlteracoes, string usuario, CancellationToken ct)
    {
        var hash = SiglaPlanilhaReader.Hash(arquivo);
        if (!string.Equals(hash, hashPrevia, StringComparison.OrdinalIgnoreCase))
            throw new RegraInvalidaException(["O arquivo enviado não é o mesmo da prévia. Gere a prévia novamente."]);
        var planilha = SiglaPlanilhaReader.Ler(arquivo, aba);
        var erros = planilha.Erros.Concat(SiglaImportacaoPlanner.ValidarMapeamento(mapeamento, planilha.Cabecalhos)).ToList();
        if (erros.Count > 0) throw new RegraInvalidaException(erros);
        return await store.AplicarImportacaoAsync(atuais => SiglaImportacaoPlanner.Planejar(planilha, mapeamento, atuais, aplicarAlteracoes),
            nomeArquivo, hash, usuario, ct);
    }

    private static DadosRegra Validar(string? sigla, string? disciplina, string? contextoTipo, string? contextoValor, bool ativo)
    {
        var (dados, erros) = SiglaNormalizer.Validar(sigla, disciplina, contextoTipo, contextoValor, ativo);
        return dados ?? throw new RegraInvalidaException(erros);
    }

    private static void GarantirUnica(IReadOnlyList<RegraSigla> existentes, DadosRegra dados, long? ignorarId)
    {
        var chave = SiglaNormalizer.Chave(dados.SiglaOriginal);
        var contexto = SiglaNormalizer.ChaveContexto(dados.ContextoTipo, dados.ContextoValor);
        if (existentes.Any(r => r.Id != ignorarId && r.SiglaChave == chave && r.ContextoChave == contexto))
            throw new RegraDuplicadaException(dados.SiglaOriginal, contexto);
    }
}
