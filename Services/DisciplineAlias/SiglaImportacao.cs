using System.Security.Cryptography;
using OfficeOpenXml;

namespace APS.WebApp.Services.DisciplineAlias;

/// <summary>
/// Leitura da planilha de mapeamento (EPPlus, já usado no projeto). Os cabeçalhos REAIS do arquivo são devolvidos e o
/// usuário confirma qual coluna é sigla/disciplina/contexto/ativo — nenhum layout é presumido além da sugestão por nome exato.
/// </summary>
public static class SiglaPlanilhaReader
{
    public const long TamanhoMaximoBytes = 5 * 1024 * 1024;
    public const int MaximoLinhas = 5000;

    public static string Hash(byte[] conteudo) => Convert.ToHexString(SHA256.HashData(conteudo)).ToLowerInvariant();

    public static PlanilhaLida Ler(byte[] conteudo, string? aba = null)
    {
        try { return LerPacote(conteudo, aba); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new PlanilhaLida(aba ?? "", [], [], [], ["Não foi possível ler a planilha (.xlsx inválido, corrompido ou protegido por senha)."]);
        }
    }

    private static PlanilhaLida LerPacote(byte[] conteudo, string? aba)
    {
        using var ms = new MemoryStream(conteudo);
        using var pacote = new ExcelPackage(ms);
        var abas = pacote.Workbook.Worksheets.Select(w => w.Name).ToList();
        if (abas.Count == 0) return new PlanilhaLida("", abas, [], [], ["Arquivo sem abas."]);
        var ws = string.IsNullOrWhiteSpace(aba) ? pacote.Workbook.Worksheets[0] : pacote.Workbook.Worksheets[aba];
        if (ws is null) return new PlanilhaLida(aba ?? "", abas, [], [], [$"Aba '{aba}' não encontrada."]);
        if (ws.Dimension is null) return new PlanilhaLida(ws.Name, abas, [], [], ["Aba vazia."]);

        int r0 = ws.Dimension.Start.Row, r1 = ws.Dimension.End.Row, c0 = ws.Dimension.Start.Column, c1 = ws.Dimension.End.Column;
        string Celula(int r, int c) => SiglaNormalizer.Limpar(ws.Cells[r, c].Text);
        // Cabeçalho = primeira linha com algum texto.
        var linhaCab = Enumerable.Range(r0, r1 - r0 + 1).FirstOrDefault(r => Enumerable.Range(c0, c1 - c0 + 1).Any(c => Celula(r, c).Length > 0));
        if (linhaCab == 0) return new PlanilhaLida(ws.Name, abas, [], [], ["Aba sem cabeçalho."]);

        var erros = new List<string>();
        var colunas = new List<(int Col, string Nome)>();
        for (var c = c0; c <= c1; c++)
        {
            var nome = Celula(linhaCab, c);
            if (nome.Length == 0) continue;
            if (colunas.Any(x => SiglaNormalizer.Chave(x.Nome) == SiglaNormalizer.Chave(nome)))
            {
                erros.Add($"Cabeçalho '{nome}' repetido na linha {linhaCab}.");
                continue;
            }
            colunas.Add((c, nome));
        }
        var linhas = new List<LinhaPlanilha>();
        for (var r = linhaCab + 1; r <= r1; r++)
        {
            var celulas = colunas.ToDictionary(x => x.Nome, x => (string?)Celula(r, x.Col));
            if (celulas.Values.All(string.IsNullOrEmpty)) continue;
            if (linhas.Count >= MaximoLinhas)
            {
                erros.Add($"Arquivo com mais de {MaximoLinhas} linhas de dados; divida a planilha.");
                break;
            }
            linhas.Add(new LinhaPlanilha(r, celulas));
        }
        return new PlanilhaLida(ws.Name, abas, colunas.Select(x => x.Nome).ToList(), linhas, erros);
    }

    /// <summary>
    /// Sugestão de mapeamento SOMENTE por nome de cabeçalho exato (sem acento/caixa). Sempre exige confirmação na tela.
    /// </summary>
    public static MapeamentoColunas? Sugerir(IReadOnlyList<string> cabecalhos)
    {
        string? Achar(params string[] nomes) =>
            cabecalhos.FirstOrDefault(h => nomes.Contains(SemAcento(SiglaNormalizer.Chave(h))));
        var sigla = Achar("SIGLA", "SIGLA ORIGINAL");
        var disciplina = Achar("DISCIPLINA CANONICA", "NOME CANONICO", "DISCIPLINA", "DISCIPLINA PT-BR", "NOME DA DISCIPLINA");
        if (sigla is null || disciplina is null || sigla == disciplina) return null;
        return new MapeamentoColunas(sigla, disciplina, Achar("TIPO DE CONTEXTO", "CONTEXTO TIPO"), Achar("CONTEXTO", "PROJETO", "FONTE"), Achar("ATIVO"));
    }

    private static string SemAcento(string s) =>
        new(s.Normalize(System.Text.NormalizationForm.FormD).Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
            != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
}

/// <summary>
/// Planejamento puro (sem banco) da importação: classifica cada linha em Incluir / Alterar / Inalterada / Rejeitada /
/// Duplicada. Idempotente: reimportar o mesmo arquivo dá tudo "Inalterada". Nunca exclui regra ausente do arquivo.
/// Conflitos nunca são resolvidos em silêncio: mesma sigla+contexto com nomes diferentes NO ARQUIVO rejeita todas as
/// linhas envolvidas; linha que muda a disciplina de regra EXISTENTE só é aplicada com aplicarAlteracoes=true.
/// </summary>
public static class SiglaImportacaoPlanner
{
    public static IReadOnlyList<string> ValidarMapeamento(MapeamentoColunas m, IReadOnlyList<string> cabecalhos)
    {
        var erros = new List<string>();
        void Exigir(string? coluna, string papel, bool obrigatoria)
        {
            if (string.IsNullOrWhiteSpace(coluna)) { if (obrigatoria) erros.Add($"Selecione a coluna de {papel}."); return; }
            if (!cabecalhos.Contains(coluna)) erros.Add($"Coluna '{coluna}' ({papel}) não existe no arquivo.");
        }
        Exigir(m.Sigla, "sigla", true);
        Exigir(m.Disciplina, "disciplina canônica", true);
        Exigir(m.ContextoTipo, "tipo de contexto", false);
        Exigir(m.ContextoValor, "contexto", false);
        Exigir(m.Ativo, "ativo", false);
        var usadas = new[] { m.Sigla, m.Disciplina, m.ContextoTipo, m.ContextoValor, m.Ativo }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (usadas.Count != usadas.Distinct().Count()) erros.Add("A mesma coluna foi escolhida para dois papéis.");
        if (!string.IsNullOrWhiteSpace(m.ContextoValor) && string.IsNullOrWhiteSpace(m.ContextoTipo))
            erros.Add("Coluna de contexto exige a coluna de tipo de contexto (PROJETO/FONTE).");
        return erros;
    }

    public static bool? LerAtivo(string? valor, out bool valido)
    {
        valido = true;
        var v = SiglaNormalizer.Chave(valor);
        switch (v)
        {
            case "": return null;
            case "1" or "SIM" or "S" or "TRUE" or "VERDADEIRO" or "ATIVO" or "ATIVA" or "X": return true;
            case "0" or "NAO" or "NÃO" or "N" or "FALSE" or "FALSO" or "INATIVO" or "INATIVA": return false;
            default: valido = false; return null;
        }
    }

    public static PlanoImportacao Planejar(PlanilhaLida planilha, MapeamentoColunas m, IReadOnlyList<RegraSigla> existentes, bool aplicarAlteracoes)
    {
        string? Valor(LinhaPlanilha l, string? coluna) => coluna is null ? null : l.Celulas.GetValueOrDefault(coluna);
        var porChave = existentes.ToDictionary(r => (r.SiglaChave, r.ContextoChave));

        // 1) validação linha a linha
        var validadas = planilha.Linhas.Select(l =>
        {
            var ativo = LerAtivo(Valor(l, m.Ativo), out var ativoValido);
            var (regra, erros) = SiglaNormalizer.Validar(Valor(l, m.Sigla), Valor(l, m.Disciplina), Valor(l, m.ContextoTipo), Valor(l, m.ContextoValor), ativo ?? true);
            if (!ativoValido) erros = [.. erros, $"Valor de ativo '{Valor(l, m.Ativo)}' não reconhecido (use SIM/NÃO)."];
            return (Linha: l, Regra: ativoValido ? regra : null, Erros: erros, AtivoInformado: ativo);
        }).ToList();

        // 2) conflitos dentro do próprio arquivo (mesma sigla + contexto com nomes diferentes)
        var conflitantes = validadas.Where(x => x.Regra is not null)
            .GroupBy(x => (SiglaNormalizer.Chave(x.Regra!.SiglaOriginal), SiglaNormalizer.ChaveContexto(x.Regra.ContextoTipo, x.Regra.ContextoValor)))
            .Where(g => g.Select(x => SiglaNormalizer.Chave(x.Regra!.DisciplinaCanonica)).Distinct().Count() > 1)
            .ToDictionary(g => g.Key, g => string.Join(" / ", g.Select(x => x.Regra!.DisciplinaCanonica).Distinct()) + " (linhas " + string.Join(", ", g.Select(x => x.Linha.Numero)) + ")");

        var vistas = new HashSet<(string, string)>();
        var saida = new List<LinhaPlano>();
        foreach (var (linha, regra, erros, ativoInformado) in validadas)
        {
            if (regra is null)
            {
                saida.Add(new LinhaPlano(linha.Numero, Valor(linha, m.Sigla), Valor(linha, m.Disciplina), Valor(linha, m.ContextoTipo),
                    Valor(linha, m.ContextoValor), ativoInformado, AcaoImportacao.Rejeitada, string.Join(" ", erros)));
                continue;
            }
            var chave = (SiglaNormalizer.Chave(regra.SiglaOriginal), SiglaNormalizer.ChaveContexto(regra.ContextoTipo, regra.ContextoValor));
            LinhaPlano Plano(AcaoImportacao acao, string? motivo, RegraSigla? atual = null, bool conflito = false) =>
                new(linha.Numero, regra.SiglaOriginal, regra.DisciplinaCanonica, regra.ContextoTipo, regra.ContextoValor, ativoInformado ?? atual?.Ativo ?? true,
                    acao, motivo, conflito, atual?.Id, atual?.DisciplinaCanonica, atual?.Ativo);
            if (conflitantes.TryGetValue(chave, out var detalhe))
            {
                saida.Add(Plano(AcaoImportacao.Rejeitada, $"Conflito no arquivo: mesma sigla e contexto com disciplinas diferentes — {detalhe}."));
                continue;
            }
            if (!vistas.Add(chave))
            {
                saida.Add(Plano(AcaoImportacao.Duplicada, "Linha repetida no arquivo (mesma sigla, contexto e disciplina); considerada uma vez."));
                continue;
            }
            if (!porChave.TryGetValue(chave, out var atual))
            {
                saida.Add(Plano(AcaoImportacao.Incluir, null));
                continue;
            }
            var mudaNome = atual.DisciplinaCanonica != regra.DisciplinaCanonica;
            var conflitoNome = SiglaNormalizer.Chave(atual.DisciplinaCanonica) != SiglaNormalizer.Chave(regra.DisciplinaCanonica);
            var mudaAtivo = ativoInformado is bool a && a != atual.Ativo;
            var mudaSigla = atual.SiglaOriginal != regra.SiglaOriginal;
            if (!mudaNome && !mudaAtivo && !mudaSigla) { saida.Add(Plano(AcaoImportacao.Inalterada, null, atual)); continue; }
            if (conflitoNome && !aplicarAlteracoes)
            {
                saida.Add(Plano(AcaoImportacao.Rejeitada,
                    $"Conflito com regra existente: hoje '{atual.DisciplinaCanonica}', arquivo '{regra.DisciplinaCanonica}'. Marque \"aplicar alterações\" para substituir.",
                    atual, conflito: true));
                continue;
            }
            var mudancas = new List<string>();
            if (mudaNome) mudancas.Add($"disciplina '{atual.DisciplinaCanonica}' → '{regra.DisciplinaCanonica}'");
            if (mudaSigla) mudancas.Add($"grafia da sigla '{atual.SiglaOriginal}' → '{regra.SiglaOriginal}'");
            if (mudaAtivo) mudancas.Add(ativoInformado == true ? "reativada" : "desativada");
            saida.Add(Plano(AcaoImportacao.Alterar, string.Join("; ", mudancas), atual, conflito: conflitoNome));
        }
        return new PlanoImportacao(planilha.Aba, planilha.Cabecalhos, m, saida);
    }
}
