using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace APS.WebApp.Services
{
    public class AccStatusResolverService
    {
        private readonly ILogger<AccStatusResolverService> _logger;
        public AccStatusResolverService(ILogger<AccStatusResolverService> logger) { _logger = logger; }

        public string? ResolverVereditoFinal(string docCodeExcel, List<JToken> allReviews, List<JToken> allVersions)
{
    if (string.IsNullOrWhiteSpace(docCodeExcel)) return null;

    string key = NormalizeMatch(docCodeExcel);

    // 1) Versões do documento (match APENAS por NAME; ignora URN)
    var versoesDoc = allVersions
        .Where(v => NormalizeMatch(v["name"]?.ToString()) == key)
        .ToList();

    if (versoesDoc.Count == 0)
    {
        // _logger?.LogDebug($"[Resolver] Sem versões p/ doc '{docCodeExcel}' (key='{key}').");
        return null;
    }

    // 2) Reviews associadas a essas versões (reviewId foi anexado quando você coletou versions)
    var reviewIds = versoesDoc
        .Select(v => v["reviewId"]?.ToString())
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (reviewIds.Count == 0)
    {
        // _logger?.LogDebug($"[Resolver] Versões sem reviewId p/ doc '{docCodeExcel}'.");
        return null;
    }

    var reviewsDoDoc = allReviews
        .Where(r => reviewIds.Contains(r["id"]?.ToString() ?? "", StringComparer.OrdinalIgnoreCase))
        .ToList();

    if (reviewsDoDoc.Count == 0)
    {
        // _logger?.LogDebug($"[Resolver] ReviewIds encontrados mas reviews não vieram no payload p/ doc '{docCodeExcel}'.");
        return null;
    }

    // =========================================================
    // REGRA: se tem review aberta desse doc -> NÃO aplica veredito
    // (continua mostrando etapa/fluxo: REVISÃO FINAL/INICIAL etc.)
    // =========================================================
    bool temAberta = reviewsDoDoc.Any(r =>
    {
        var st = (r["status"]?.ToString() ?? "").Trim();
        return st.Equals("OPEN", StringComparison.OrdinalIgnoreCase) ||
               st.Equals("INITIATED", StringComparison.OrdinalIgnoreCase);
    });

    if (temAberta)
    {
        // _logger?.LogDebug($"[Resolver] Doc '{docCodeExcel}' tem review OPEN/INITIATED -> sem veredito.");
        return null;
    }

    // =========================================================
    // Agora só restam reviews fechadas (CLOSED/COMPLETED)
    // Escolhe a mais recente e extrai approveStatus da versão
    // =========================================================
    DateTime ReadDt(JToken t, string field)
    {
        var raw = t[field]?.ToString();
        return DateTime.TryParse(raw, out var dt) ? dt : DateTime.MinValue;
    }

    var reviewsFechadasOrdenadas = reviewsDoDoc
        .Where(r =>
        {
            var st = (r["status"]?.ToString() ?? "").Trim();
            return st.Equals("CLOSED", StringComparison.OrdinalIgnoreCase) ||
                   st.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase);
        })
        .OrderByDescending(r => ReadDt(r, "updatedAt"))
        .ThenByDescending(r => ReadDt(r, "createdAt"))
        .ToList();

    if (reviewsFechadasOrdenadas.Count == 0)
    {
        // _logger?.LogDebug($"[Resolver] Doc '{docCodeExcel}' sem reviews fechadas.");
        return null;
    }

    // Tenta, na ordem, achar a PRIMEIRA review fechada que tenha approveStatus != null em alguma versão do doc
    foreach (var review in reviewsFechadasOrdenadas)
    {
        string rid = review["id"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(rid)) continue;

        // versões desse doc dentro desse review
        var versoesDessaReview = versoesDoc
            .Where(v => string.Equals(v["reviewId"]?.ToString(), rid, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (versoesDessaReview.Count == 0) continue;

        // pega uma versão "mais recente" se houver datas; senão pega a última
        var versaoAlvo = versoesDessaReview
            .OrderByDescending(v => ReadDt(v, "updatedAt"))
            .ThenByDescending(v => ReadDt(v, "createdAt"))
            .FirstOrDefault() ?? versoesDessaReview.Last();

        var approve = versaoAlvo["approveStatus"];
        if (approve == null || approve.Type == JTokenType.Null)
        {
            // review fechada mas essa versão não tem approveStatus -> tenta próxima review fechada
            continue;
        }

        string label = (approve["label"]?.ToString() ?? approve["value"]?.ToString() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(label))
            continue;

        // 3) Normaliza/mapeia para o padrão da LDP
        string upper = label.ToUpperInvariant();

        // Rejeitado
        if (upper.Contains("REJEIT") || upper.Contains("REJECT"))
            return "REJEITADO";

        // Aprovado com comentário/observação
        if (upper.Contains("COMENT") || upper.Contains("COMMENT") || upper.Contains("OBS"))
            return "APROV. C/ OBS";

        // Aprovado
        if (upper.Contains("APROV") || upper.Contains("APPROV"))
            return "APROVADO";

        // fallback seguro: devolve o label em PT/EN “como veio” (se quiser)
        // mas eu recomendo manter padrão:
        return "APROVADO";
    }

    // _logger?.LogDebug($"[Resolver] Doc '{docCodeExcel}' teve reviews fechadas, mas nenhuma versão trouxe approveStatus.");
    return null;
}



        private string NormalizeMatch(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            string name = Path.GetFileNameWithoutExtension(input).ToUpperInvariant();
            name = name.Replace("_", "-").Replace(" ", "-");
            name = Regex.Replace(name, @"-R\d+$", "");
            name = Regex.Replace(name, @"-REV\d+$", "");
            name = Regex.Replace(name, @"-V\d+$", "");

            // Remove tudo que não é letra ou número para um match perfeito
            return Regex.Replace(name, @"[^A-Z0-9]", "");
        }
    }
}