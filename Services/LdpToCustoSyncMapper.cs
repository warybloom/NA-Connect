using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace APS.WebApp.Services;

/// <summary>
/// Cópia LDP -> CUSTO, limitada estritamente às colunas A:G na CUSTO.
/// Preflight completo antes de alterar qualquer célula. Suporta cabeçalhos
/// PT/EN na linha 7, inclusive códigos com fórmula e valor em cache.
/// </summary>
internal static class LdpToCustoSyncMapper
{
    private const int LinhaCabecalho = 7;
    private const int InicioDados = 8;
    private const int ColunaFinalDestino = 7;

    private static readonly string[] Campos = {
        "ITEM", "SCHEDULE_ID", "DISCIPLINE", "NA_DOCUMENT_NUMBER",
        "CLIENT_DOCUMENT_NUMBER", "DOCUMENT_TITLE", "FORMAT"
    };

    // Nome já normalizado (sem acentos/espaços/sinais) => campo canônico.
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ITEM"] = "ITEM",
            ["IDDOCRONOGRAMA"] = "SCHEDULE_ID",
            ["IDCRONOGRAMA"] = "SCHEDULE_ID",
            ["SCHEDULEID"] = "SCHEDULE_ID",
            ["DISCIPLINA"] = "DISCIPLINE",
            ["DISCIPLINE"] = "DISCIPLINE",
            ["NADOCUMENTNUMBER"] = "NA_DOCUMENT_NUMBER",
            ["NUMERODOCUMENTONA"] = "NA_DOCUMENT_NUMBER",
            ["NUMERODODOCUMENTONA"] = "NA_DOCUMENT_NUMBER",
            ["CLIENTDOCUMENTNUMBER"] = "CLIENT_DOCUMENT_NUMBER",
            ["NUMERODOCUMENTOCLIENTE"] = "CLIENT_DOCUMENT_NUMBER",
            ["NUMERODODOCUMENTOCLIENTE"] = "CLIENT_DOCUMENT_NUMBER",
            ["DOCUMENTTITLE"] = "DOCUMENT_TITLE",
            ["TITULODODOCUMENTO"] = "DOCUMENT_TITLE",
            ["TITULODOCUMENTO"] = "DOCUMENT_TITLE",
            ["FORMAT"] = "FORMAT",
            ["FORMATO"] = "FORMAT",
        };

    internal sealed record Resultado(int Documentos, int Colunas, int Celulas, int CelulasNaoVazias);

    internal static Resultado Sincronizar(ExcelWorksheet ldp, ExcelWorksheet custo)
    {
        ArgumentNullException.ThrowIfNull(ldp);
        ArgumentNullException.ThrowIfNull(custo);

        var origem = Mapear(ldp);
        var destino = Mapear(custo);

        // NUNCA limpar ou publicar uma CUSTO com mapeamento parcial.
        var faltantesOrigem = Campos.Where(c => !origem.ContainsKey(c)).ToArray();
        var faltantesDestino = Campos.Where(c => !destino.ContainsKey(c)).ToArray();
        if (faltantesOrigem.Length > 0 || faltantesDestino.Length > 0)
            throw new InvalidOperationException(
                "Sincronização LDP->CUSTO bloqueada: cabeçalhos obrigatórios ausentes. "
                + "LDP=[" + string.Join(", ", faltantesOrigem) + "] "
                + "CUSTO=[" + string.Join(", ", faltantesDestino) + "]. "
                + "Cabeçalhos esperados na linha 7: ITEM, ID DO CRONOGRAMA, DISCIPLINA, "
                + "NÚMERO DOCUMENTO N&A, NÚMERO DOCUMENTO CLIENTE, TÍTULO DO DOCUMENTO, FORMATO.");

        if (Campos.Any(c => destino[c] < 1 || destino[c] > ColunaFinalDestino))
            throw new InvalidOperationException(
                "Sincronização LDP->CUSTO bloqueada: cabeçalho mapeado fora de A:G.");

        if (Campos.Select(c => destino[c]).Distinct().Count() != Campos.Length)
            throw new InvalidOperationException(
                "Sincronização LDP->CUSTO bloqueada: colunas de destino duplicadas.");

        var linhas = new List<object?[]>();
        var ultimaLinhaLdp = ldp.Dimension?.End.Row ?? LinhaCabecalho;
        for (var row = InicioDados; row <= ultimaLinhaLdp; row++)
        {
            var documento = ldp.Cells[row, origem["NA_DOCUMENT_NUMBER"]].Value?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(documento)) continue;

            var valores = new object?[Campos.Length];
            for (var i = 0; i < Campos.Length; i++)
                valores[i] = ldp.Cells[row, origem[Campos[i]]].Value;
            linhas.Add(valores);
        }

        if (linhas.Count == 0)
            throw new InvalidOperationException(
                "Sincronização LDP->CUSTO bloqueada: nenhum número de documento na LDP. "
                + "Nenhuma célula foi limpa e nenhuma versão deve ser publicada.");

        var ultimaCusto = custo.Dimension?.End.Row ?? LinhaCabecalho;
        if (ultimaCusto >= InicioDados)
            custo.Cells[InicioDados, 1, ultimaCusto, ColunaFinalDestino].Value = null;

        var naoVazias = 0;
        for (var i = 0; i < linhas.Count; i++)
        {
            for (var j = 0; j < Campos.Length; j++)
            {
                var valor = linhas[i][j];
                var celula = custo.Cells[InicioDados + i, destino[Campos[j]]];
                celula.Value = valor;
                if (valor is not null && !string.IsNullOrWhiteSpace(valor.ToString()))
                    naoVazias++;
            }
        }

        // Confirma o conteúdo ANTES de criar nova versão no ACC.
        for (var i = 0; i < linhas.Count; i++)
        {
            for (var j = 0; j < Campos.Length; j++)
            {
                var lido = custo.Cells[InicioDados + i, destino[Campos[j]]].Value;
                if (!Equals(lido, linhas[i][j]))
                    throw new InvalidOperationException(
                        $"Falha de validação na aba CUSTO: linha {InicioDados + i}, "
                        + $"campo {Campos[j]}. Upload cancelado.");
            }
        }

        return new Resultado(linhas.Count, Campos.Length, linhas.Count * Campos.Length, naoVazias);
    }

    private static Dictionary<string, int> Mapear(ExcelWorksheet aba)
    {
        var resultado = new Dictionary<string, int>(StringComparer.Ordinal);
        var limite = Math.Min(100, aba.Dimension?.End.Column ?? 0);
        for (var coluna = 1; coluna <= limite; coluna++)
        {
            var cabecalho = Normalizar(aba.Cells[LinhaCabecalho, coluna].Value?.ToString());
            if (cabecalho.Length == 0 || !Aliases.TryGetValue(cabecalho, out var campo))
                continue;
            if (!resultado.TryAdd(campo, coluna))
                throw new InvalidOperationException(
                    $"Sincronização LDP->CUSTO bloqueada: cabeçalho duplicado para {campo} "
                    + $"na aba {aba.Name}, linha {LinhaCabecalho}.");
        }
        return resultado;
    }

    private static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var decomposto = texto.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            var upper = char.ToUpperInvariant(c);
            if (upper >= 'A' && upper <= 'Z' || upper >= '0' && upper <= '9')
                sb.Append(upper);
        }
        return sb.ToString();
    }
}
