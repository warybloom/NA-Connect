using System;
using System.Collections.Generic;
using System.Linq;
using APS.WebApp.Models;
using OfficeOpenXml;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Enumera TODAS as worksheets do workbook da LDP (Visible/Hidden/VeryHidden) e localiza, por NOME
    /// normalizado (nunca por Contains/fuzzy agressivo — só variações explícitas de espaço/case), as
    /// abas Timesheet/Custo Real/Custo profissionais. Visibilidade NUNCA decide relevância — é só
    /// registrada no diagnóstico. A aba "CUSTO" é reconhecida explicitamente e marcada como
    /// CUSTO_IGNORADA — nunca usada como fonte (ver MAPA_DADOS_LDP_FINANCEIRO.md seção 4).
    /// </summary>
    public static class GestaoVistaWorksheetLocator
    {
        /// <summary>Variações de nome tolerada por espaço/case (nunca fuzzy: comparação exata após
        /// normalização, nunca Contains/Levenshtein).</summary>
        private static readonly Dictionary<string, string[]> NomesPorPapel = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Timesheet"] = new[] { "TIMESHEET", "TIME SHEET" },
            ["CustoReal"] = new[] { "CUSTO REAL" },
            ["CustoProfissionais"] = new[] { "CUSTO PROFISSIONAIS", "CUSTO PROFISSIONAL" },
        };

        private static readonly string[] NomesCustoIgnorada = { "CUSTO" };

        private static string NormalizarNomeAba(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome)) return string.Empty;
            string texto = System.Text.RegularExpressions.Regex.Replace(nome.Trim(), @"\s+", " ");
            return texto.ToUpperInvariant();
        }

        /// <summary>
        /// Monta o diagnóstico de TODAS as abas do workbook e devolve, junto, a worksheet real localizada
        /// para cada papel reconhecido (null quando não encontrada/ambígua).
        /// </summary>
        public static (List<GestaoVistaWorksheetDiagnostico> Diagnosticos, Dictionary<string, ExcelWorksheet> Localizadas)
            EnumerarEEncontrar(ExcelPackage package)
        {
            var diagnosticos = new List<GestaoVistaWorksheetDiagnostico>();
            var localizadas = new Dictionary<string, ExcelWorksheet>(StringComparer.OrdinalIgnoreCase);
            var candidatasPorPapel = new Dictionary<string, List<ExcelWorksheet>>(StringComparer.OrdinalIgnoreCase);

            foreach (var papel in NomesPorPapel.Keys) candidatasPorPapel[papel] = new List<ExcelWorksheet>();

            foreach (var ws in package.Workbook.Worksheets)
            {
                string nomeNormalizado = NormalizarNomeAba(ws.Name);
                string papelIdentificado = null;

                foreach (var (papel, nomes) in NomesPorPapel)
                {
                    if (nomes.Any(n => string.Equals(n, nomeNormalizado, StringComparison.OrdinalIgnoreCase)))
                    {
                        candidatasPorPapel[papel].Add(ws);
                        papelIdentificado = papel;
                        break;
                    }
                }

                if (papelIdentificado == null && NomesCustoIgnorada.Any(n => string.Equals(n, nomeNormalizado, StringComparison.OrdinalIgnoreCase)))
                {
                    papelIdentificado = "CUSTO_IGNORADA";
                }

                diagnosticos.Add(new GestaoVistaWorksheetDiagnostico
                {
                    NomeAba = ws.Name,
                    Visibilidade = ws.Hidden.ToString(),
                    PapelIdentificado = papelIdentificado,
                    Encontrada = papelIdentificado != null && papelIdentificado != "CUSTO_IGNORADA",
                    QuantidadeLinhas = ws.Dimension?.End.Row ?? 0,
                });
            }

            foreach (var (papel, candidatas) in candidatasPorPapel)
            {
                if (candidatas.Count == 1)
                {
                    localizadas[papel] = candidatas[0];
                }
                else if (candidatas.Count > 1)
                {
                    foreach (var c in candidatas)
                    {
                        var d = diagnosticos.First(x => x.NomeAba == c.Name);
                        d.Encontrada = false;
                        d.Warnings.Add($"[GESTÃO À VISTA] Mais de uma aba corresponde a '{papel}' após normalização — ambíguo, nenhuma será usada.");
                    }
                }
            }

            return (diagnosticos, localizadas);
        }
    }
}
