using System;
using System.Collections.Generic;
using System.Linq;
using OfficeOpenXml;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Resolve dinamicamente a linha de cabeçalho de UMA worksheet já localizada (Timesheet/Custo Real/
    /// Custo profissionais) — nunca assume linha 1 fixa (a auditoria real confirmou que "Custo
    /// profissionais" tem cabeçalho na linha 2, não na 1 — ver
    /// AUDITORIA_FORGESYNC_NA/MAPA_DADOS_LDP_FINANCEIRO.md seção 3).
    /// </summary>
    public static class GestaoVistaHeaderResolver
    {
        public const int LimiteVarreduraLinhaCabecalho = 15;

        /// <summary>
        /// Varre as primeiras linhas até encontrar a primeira em que TODOS os cabeçalhos-âncora aparecem
        /// simultaneamente (cada um em qualquer coluna, já normalizados). Retorna null se nenhuma linha
        /// satisfizer as âncoras dentro do limite — nunca escolhe uma linha "parecida".
        /// </summary>
        public static int? LocalizarLinhaCabecalho(ExcelWorksheet ws, IReadOnlyList<string> ancorasCanonicas)
        {
            int maxRow = ws.Dimension?.End.Row ?? 0;
            int limite = Math.Min(maxRow, LimiteVarreduraLinhaCabecalho);

            for (int linha = 1; linha <= limite; linha++)
            {
                var mapa = LerCabecalhosDaLinha(ws, linha);
                if (mapa.Count == 0) continue;

                if (ancorasCanonicas.All(a => mapa.ContainsKey(a))) return linha;
            }

            return null;
        }

        /// <summary>
        /// Lê TODA uma linha (coluna 1 até Dimension.End.Column) e monta o mapa cabeçalho normalizado ->
        /// TODAS as colunas onde aparece (lista, nunca sobrescreve — permite detectar duplicidade).
        /// Células vazias nunca interrompem a varredura.
        /// </summary>
        public static Dictionary<string, List<int>> LerCabecalhosDaLinha(ExcelWorksheet ws, int linha)
        {
            var mapa = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            int maxCol = ws.Dimension?.End.Column ?? 0;

            for (int col = 1; col <= maxCol; col++)
            {
                string original = ws.Cells[linha, col].Text;
                string normalizado = GestaoVistaCabecalhos.Normalizar(original);
                if (string.IsNullOrEmpty(normalizado)) continue;

                if (!mapa.TryGetValue(normalizado, out var colunas))
                {
                    colunas = new List<int>();
                    mapa[normalizado] = colunas;
                }
                colunas.Add(col);
            }

            return mapa;
        }

        /// <summary>Resolve a coluna de um cabeçalho só quando aparece exatamente uma vez na linha —
        /// nunca escolhe arbitrariamente entre ocorrências duplicadas.</summary>
        public static int? ObterColunaUnica(Dictionary<string, List<int>> mapa, string cabecalhoCanonico)
        {
            return mapa.TryGetValue(cabecalhoCanonico, out var colunas) && colunas.Count == 1
                ? colunas[0]
                : (int?)null;
        }
    }
}
