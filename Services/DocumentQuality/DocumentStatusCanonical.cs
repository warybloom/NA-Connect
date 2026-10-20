using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace APS.WebApp.Services.DocumentQuality;

/// <summary>
/// Chave CANÔNICA do Document_Status para o avanço físico gerencial (nunca Status_In_Flow).
/// 1) normalização textual determinística: sem acentos, "_"/"-" viram espaço, espaços colapsados, trim, maiúsculas;
/// 2) de-para EXPLÍCITO português ⇄ inglês (lista fechada abaixo): cada significado aceita os nomes nos dois idiomas
///    (ex.: APROVADO = APPROVED). Sem fuzzy: status fora da lista vale por si.
/// A mesma chave é usada para PERSISTIR (Document_Status_Normalizado) e para PROCURAR a configuração do documento.
/// O frontend espelha esta tabela (physicalProgressApi.ts — STATUS_ALIASES), conferida por teste.
/// </summary>
public static class DocumentStatusCanonical
{
    /// <summary>Significado canônico → variantes aceitas (já na forma textual normalizada).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Aliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["NAO INICIADO"] = ["NOT STARTED", "NOT INITIATED", "NAO INICIADA"],
        ["COLETA DE DADOS"] = ["DATA COLLECTION", "DATA GATHERING", "COLLECTING DATA", "EM COLETA DE DADOS"],
        ["INICIADO"] = ["STARTED", "INITIATED", "INICIADA"],
        ["ELABORANDO"] = ["EM ELABORACAO", "IN PROGRESS", "WORK IN PROGRESS", "IN ELABORATION", "UNDER ELABORATION", "ELABORATING", "UNDER DEVELOPMENT"],
        ["FINALIZADO ELABORACAO"] = ["ELABORACAO FINALIZADA", "ELABORACAO CONCLUIDA", "ELABORATION FINISHED", "ELABORATION COMPLETED", "ELABORATION COMPLETE"],
        ["AUTORIZACAO"] = ["EM AUTORIZACAO", "AGUARDANDO AUTORIZACAO", "AUTHORIZATION", "UNDER AUTHORIZATION", "AWAITING AUTHORIZATION"],
        ["EMITIDO PARA CLIENTE"] = ["EMITIDO AO CLIENTE", "ENVIADO AO CLIENTE", "ENVIADO PARA CLIENTE", "ISSUED TO CLIENT", "ISSUED FOR CLIENT", "SENT TO CLIENT"],
        ["EM ATENDIMENTO DE COMENTARIO"] = ["EM ATENDIMENTO DE COMENTARIOS", "ATENDIMENTO DE COMENTARIOS", "ADDRESSING COMMENTS", "ANSWERING COMMENTS", "COMMENTS IN PROGRESS"],
        ["APROVADO"] = ["APPROVED", "APROVADA"],
    };

    private static readonly Dictionary<string, string> PorVariante = Aliases
        .SelectMany(a => a.Value.Select(v => (Variante: v, Canonico: a.Key)))
        .ToDictionary(x => x.Variante, x => x.Canonico, StringComparer.Ordinal);

    /// <summary>Forma textual normalizada (sem aplicar aliases).</summary>
    public static string Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var semAcento = new StringBuilder();
        foreach (var c in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) semAcento.Append(c);
        var texto = Regex.Replace(semAcento.ToString().Normalize(NormalizationForm.FormC), @"[_\-]+", " ");
        return Regex.Replace(texto, @"\s+", " ").Trim().ToUpperInvariant();
    }

    /// <summary>Chave canônica: forma textual + alias explícito (quando houver).</summary>
    public static string Key(string? value)
    {
        var texto = Text(value);
        return PorVariante.TryGetValue(texto, out var canonico) ? canonico : texto;
    }
}
