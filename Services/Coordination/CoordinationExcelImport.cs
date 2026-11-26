using OfficeOpenXml;

namespace NA_Connect.Services.Coordination;

/// <summary>
/// Leitura/validação de Excel de importação da Norma de Coordenação (colunas SIGLA, DISCIPLINA) —
/// EPPlus, nunca persiste (isso é responsabilidade do controller, via
/// CoordinationStandardRepository.ImportarMapeamentos, só depois de confirmação explícita do preview).
/// Rejeita/avisa: SIGLA vazia, DISCIPLINA vazia, sigla duplicada NA MESMA PLANILHA, linhas totalmente
/// vazias (ignoradas silenciosamente). Sigla já cadastrada na Norma é só um AVISO (reimportar corrige
/// sem exigir edição manual) — nunca invalida a linha.
/// </summary>
public static class CoordinationExcelImport
{
    private static readonly string[] CabecalhosSigla = { "SIGLA" };
    private static readonly string[] CabecalhosDisciplina = { "DISCIPLINA" };

    public static ImportPreviewResultadoDto Validar(byte[] xlsx, IReadOnlySet<string> siglasJaCadastradas)
    {
        using var package = new ExcelPackage(new MemoryStream(xlsx));
        var ws = package.Workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("Planilha vazia ou sem abas.");

        int linhaCabecalho = -1, colSigla = -1, colDisciplina = -1;
        var ultimaLinhaBusca = Math.Min(ws.Dimension?.End.Row ?? 1, 10);
        for (var linha = 1; linha <= ultimaLinhaBusca && linhaCabecalho == -1; linha++)
        {
            for (var col = 1; col <= (ws.Dimension?.End.Column ?? 1); col++)
            {
                var texto = (ws.Cells[linha, col].Text ?? "").Trim().ToUpperInvariant();
                if (CabecalhosSigla.Contains(texto)) colSigla = col;
                else if (CabecalhosDisciplina.Contains(texto)) colDisciplina = col;
            }
            if (colSigla != -1 && colDisciplina != -1) linhaCabecalho = linha;
            else { colSigla = -1; colDisciplina = -1; }
        }
        if (linhaCabecalho == -1)
            throw new InvalidOperationException("Cabeçalho 'SIGLA' e 'DISCIPLINA' não encontrado nas primeiras 10 linhas.");

        var resultado = new ImportPreviewResultadoDto();
        var siglasNestaPlanilha = new HashSet<string>();
        var ultimaLinha = ws.Dimension?.End.Row ?? linhaCabecalho;
        for (var linha = linhaCabecalho + 1; linha <= ultimaLinha; linha++)
        {
            var siglaOriginal = ws.Cells[linha, colSigla].Text?.Trim();
            var disciplinaOriginal = ws.Cells[linha, colDisciplina].Text?.Trim();
            if (string.IsNullOrWhiteSpace(siglaOriginal) && string.IsNullOrWhiteSpace(disciplinaOriginal)) continue; // linha totalmente vazia

            var siglaNormalizada = (siglaOriginal ?? "").Trim().ToUpperInvariant();
            var erros = new List<string>();
            if (string.IsNullOrWhiteSpace(siglaOriginal)) erros.Add("SIGLA vazia.");
            if (string.IsNullOrWhiteSpace(disciplinaOriginal)) erros.Add("DISCIPLINA vazia.");
            if (siglaNormalizada != "" && !siglasNestaPlanilha.Add(siglaNormalizada)) erros.Add($"Sigla '{siglaNormalizada}' duplicada nesta planilha.");

            var linhaDto = new ImportPreviewLinhaDto
            {
                Linha = linha, SiglaOriginal = siglaOriginal, DisciplinaOriginal = disciplinaOriginal,
                SiglaNormalizada = siglaNormalizada, Valida = erros.Count == 0, Erros = erros,
            };
            if (linhaDto.Valida && siglasJaCadastradas.Contains(siglaNormalizada))
                linhaDto.Avisos.Add("Sigla já cadastrada nesta Norma — importar substitui a disciplina atual.");
            resultado.Linhas.Add(linhaDto);
        }
        resultado.TotalLinhas = resultado.Linhas.Count;
        resultado.LinhasValidas = resultado.Linhas.Count(l => l.Valida);
        resultado.LinhasInvalidas = resultado.TotalLinhas - resultado.LinhasValidas;
        return resultado;
    }
}
