using APS.WebApp.Services.DisciplineAlias;
using OfficeOpenXml;

namespace NA_Connect.DisciplineAlias.Tests;

/// <summary>Planilhas de teste geradas em memória (fixtures EXCLUSIVAS: siglas sintéticas, nunca o Excel oficial).</summary>
internal static class Planilha
{
    // Mesma configuração global do Program.cs; vale para o leitor também (teste que não cria planilha antes).
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Licenca() => ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

    public static byte[] Criar(params string?[][] linhas) => CriarNaAba("Mapeamento", 1, linhas);

    public static byte[] CriarNaAba(string aba, int primeiraLinha, params string?[][] linhas)
    {
        using var p = new ExcelPackage();
        var ws = p.Workbook.Worksheets.Add(aba);
        for (var r = 0; r < linhas.Length; r++)
            for (var c = 0; c < linhas[r].Length; c++)
                if (linhas[r][c] is { } v) ws.Cells[primeiraLinha + r, c + 1].Value = v;
        return p.GetAsByteArray();
    }
}

/// <summary>Store em memória com a mesma semântica do SQL (chave única, versão, auditoria, importação transacional).</summary>
internal sealed class MemoriaStore : ISiglaDisciplinaStore
{
    private readonly object _lock = new();
    private long _id, _imp;
    public bool Instalado { get; set; } = true;
    public List<RegraSigla> Regras { get; } = [];
    public List<(long RegraId, string Acao, RegraSigla? Antes, RegraSigla? Depois, long? ImportacaoId, string Usuario)> Auditoria { get; } = [];
    public List<ImportacaoRegistrada> Importacoes { get; } = [];

    private void Garantir() { if (!Instalado) throw new NormalizadorNaoInstaladoException(); }

    public Task<bool> InstaladoAsync(CancellationToken ct) => Task.FromResult(Instalado);
    public Task<IReadOnlyList<RegraSigla>> ListarAsync(CancellationToken ct)
    {
        Garantir();
        lock (_lock) return Task.FromResult<IReadOnlyList<RegraSigla>>(Regras.ToList());
    }

    private RegraSigla Inserir(DadosRegra d, string origem, long? imp, string usuario)
    {
        if (Regras.Any(r => r.SiglaChave == SiglaNormalizer.Chave(d.SiglaOriginal) && r.ContextoChave == SiglaNormalizer.ChaveContexto(d.ContextoTipo, d.ContextoValor)))
            throw new RegraDuplicadaException(d.SiglaOriginal, d.ContextoTipo);
        var agora = DateTime.UtcNow;
        var r = new RegraSigla(++_id, d.SiglaOriginal, d.DisciplinaCanonica, d.ContextoTipo, d.ContextoValor, d.Ativo, origem, imp, 1, agora, usuario, agora, usuario);
        Regras.Add(r);
        Auditoria.Add((r.Id, "INCLUIR", null, r, imp, usuario));
        return r;
    }

    private RegraSigla Atualizar(long id, int versao, DadosRegra d, long? imp, string usuario)
    {
        var i = Regras.FindIndex(r => r.Id == id);
        if (i < 0) throw new RegraNaoEncontradaException(id);
        var antes = Regras[i];
        if (antes.Versao != versao) throw new ConcorrenciaException();
        var depois = antes with { SiglaOriginal = d.SiglaOriginal, DisciplinaCanonica = d.DisciplinaCanonica, ContextoTipo = d.ContextoTipo,
            ContextoValor = d.ContextoValor, Ativo = d.Ativo, ImportacaoId = imp ?? antes.ImportacaoId, Versao = antes.Versao + 1,
            AlteradoEm = DateTime.UtcNow, AlteradoPor = usuario };
        Regras[i] = depois;
        Auditoria.Add((id, "ALTERAR", antes, depois, imp, usuario));
        return depois;
    }

    public Task<RegraSigla> CriarAsync(DadosRegra dados, string usuario, CancellationToken ct)
    {
        Garantir();
        lock (_lock) return Task.FromResult(Inserir(dados, "MANUAL", null, usuario));
    }

    public Task<RegraSigla> AtualizarAsync(long id, int versao, DadosRegra dados, string usuario, CancellationToken ct)
    {
        Garantir();
        lock (_lock) return Task.FromResult(Atualizar(id, versao, dados, null, usuario));
    }

    public Task ExcluirAsync(long id, int versao, string usuario, CancellationToken ct)
    {
        Garantir();
        lock (_lock)
        {
            var r = Regras.FirstOrDefault(x => x.Id == id) ?? throw new RegraNaoEncontradaException(id);
            if (r.Versao != versao) throw new ConcorrenciaException();
            Auditoria.Add((id, "EXCLUIR", r, null, null, usuario));
            Regras.Remove(r);
        }
        return Task.CompletedTask;
    }

    public Task<ResultadoImportacao> AplicarImportacaoAsync(Func<IReadOnlyList<RegraSigla>, PlanoImportacao> planejar, string nomeArquivo,
        string hashSha256, string usuario, CancellationToken ct)
    {
        Garantir();
        lock (_lock)
        {
            var plano = planejar(Regras.ToList());
            var imp = ++_imp;
            foreach (var l in plano.Linhas)
            {
                var d = new DadosRegra(l.SiglaOriginal!, l.DisciplinaCanonica!, l.ContextoTipo, l.ContextoValor, l.Ativo ?? true);
                if (l.Acao == AcaoImportacao.Incluir) Inserir(d, "IMPORTACAO", imp, usuario);
                else if (l.Acao == AcaoImportacao.Alterar)
                {
                    var atual = Regras.First(r => r.Id == l.RegraExistenteId);
                    Atualizar(atual.Id, atual.Versao, d, imp, usuario);
                }
            }
            var s = plano.Resumo;
            Importacoes.Add(new ImportacaoRegistrada(imp, nomeArquivo, hashSha256, plano.Aba, s.Incluidas, s.Alteradas, s.Inalteradas, s.Rejeitadas,
                s.Duplicadas, DateTime.UtcNow, usuario));
            return Task.FromResult(new ResultadoImportacao(imp, s, plano.Linhas));
        }
    }

    public Task<IReadOnlyList<ImportacaoRegistrada>> ListarImportacoesAsync(int maximo, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ImportacaoRegistrada>>(Importacoes.AsEnumerable().Reverse().Take(maximo).ToList());
}
