using System.Text.RegularExpressions;
using OfficeOpenXml;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Correção 2026-09-22 (Custo Real, Etapa 03 — ViaPlanilha=0): a coluna "15. CUSTO" do Timesheet é
    /// escrita pela Etapa 01 (TimesheetDumpService) como FÓRMULA
    /// (=C{n}*(SUMIF('Custo Real'!C:C, TIMESHEET!A{n}, 'Custo Real'!I:I))) e salva pelo EPPlus SEM valor em
    /// cache — confirmado no XML da LDP real de BML011.013 (2937 células &lt;f&gt; sem &lt;v&gt;). Ler só o
    /// cache devolve vazio para todas as linhas. Aqui as fórmulas sem cache são avaliadas EM MEMÓRIA (o
    /// arquivo nunca é salvo), com referências de coluna inteira ("X:X") limitadas à última linha usada
    /// da aba referenciada — mesmo resultado, mas sem varrer 1.048.576 linhas por SUMIF (coluna inteira
    /// levou &gt;5 min em BML011.013; limitada, ~200 ms). Células com valor em cache nunca são tocadas.
    /// </summary>
    public static class GestaoVistaFormulaCache
    {
        private static readonly Regex ReferenciaColunaInteira = new(
            @"(?:(?<aba>'[^']+'|[A-Za-z_][\w\.]*)!)?\$?(?<c1>[A-Za-z]{1,3}):\$?(?<c2>[A-Za-z]{1,3})(?![\w\(])",
            RegexOptions.Compiled);

        /// <summary>Avalia as fórmulas sem valor em cache da coluna informada. Retorna quantas células foram avaliadas.</summary>
        public static int AvaliarFormulasSemCache(ExcelWorksheet ws, int coluna, int linhaInicial, int linhaFinal)
        {
            if (ws == null || linhaFinal < linhaInicial) return 0;

            int pendentes = 0;
            for (int linha = linhaInicial; linha <= linhaFinal; linha++)
            {
                var celula = ws.Cells[linha, coluna];
                if (string.IsNullOrEmpty(celula.Formula) || celula.Value != null) continue;
                celula.Formula = LimitarColunasInteiras(ws, celula.Formula);
                pendentes++;
            }

            if (pendentes == 0) return 0;

            for (int linha = linhaInicial; linha <= linhaFinal; linha++)
            {
                var celula = ws.Cells[linha, coluna];
                if (!string.IsNullOrEmpty(celula.Formula) && celula.Value == null) celula.Calculate();
            }
            return pendentes;
        }

        internal static string LimitarColunasInteiras(ExcelWorksheet wsOrigem, string formula) =>
            ReferenciaColunaInteira.Replace(formula, m =>
            {
                string prefixo = m.Groups["aba"].Success ? m.Groups["aba"].Value : null;
                var alvo = prefixo == null ? wsOrigem : wsOrigem.Workbook.Worksheets[prefixo.Trim('\'')];
                if (alvo == null) return m.Value; // aba desconhecida: mantém como está (EPPlus reporta o erro na avaliação).
                int ultimaLinha = alvo.Dimension?.End.Row ?? 1;
                string aba = prefixo == null ? string.Empty : prefixo + "!";
                return $"{aba}${m.Groups["c1"].Value}$1:${m.Groups["c2"].Value}${ultimaLinha}";
            });
    }
}
