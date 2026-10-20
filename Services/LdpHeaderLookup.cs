using System.Collections.Generic;
using OfficeOpenXml;

namespace APS.WebApp.Services
{
    /// <summary>
    /// Utilitário genérico de acesso físico a uma LDP (Tipo1 ou Tipo2) por texto de cabeçalho, nunca por
    /// posição fixa de coluna — usado quando uma etapa precisa localizar uma célula cujo significado
    /// (a REGRA de negócio) é o mesmo para qualquer Tipo_LDP, mas cuja posição física pode variar entre
    /// layouts (a LDP Tipo2 tem blocos de gate 30/60/90 que deslocam colunas subsequentes). Nunca decide
    /// nada de negócio — só localiza a célula. Usado por LdpUpdateService (coluna "STATUS IN THE FLOW",
    /// Etapa 05) e TimesheetDumpService (colunas "CR"/"CR-OS", Etapa 01).
    /// </summary>
    internal static class LdpHeaderLookup
    {
        /// <summary>
        /// Localiza a coluna cujo texto normalizado (mesma tolerância a espaço/pontuação/acentuação de
        /// LdpUpdateService.NormalizarNomePasta) bate com algum dos cabeçalhos aceitos, na linha
        /// indicada. Cai em colunaPadrao (nunca lança exceção, nunca bloqueia a etapa chamadora) quando:
        /// a planilha/linha é inválida, nenhum cabeçalho aceito é encontrado, ou mais de uma coluna bate
        /// (ambíguo) — nunca escolhe no chute.
        /// </summary>
        public static int LocalizarColunaPorCabecalho(
            ExcelWorksheet ws, int linhaCabecalho, IReadOnlyList<string> cabecalhosAceitos, int colunaPadrao)
        {
            if (ws?.Dimension == null || linhaCabecalho < 1 || linhaCabecalho > ws.Dimension.End.Row)
                return colunaPadrao;

            var aceitosNormalizados = new List<string>(cabecalhosAceitos.Count);
            foreach (var cabecalho in cabecalhosAceitos)
            {
                aceitosNormalizados.Add(LdpUpdateService.NormalizarNomePasta(cabecalho));
            }

            var candidatas = new List<int>();
            for (int col = 1; col <= ws.Dimension.End.Column; col++)
            {
                string texto = ws.Cells[linhaCabecalho, col].Text;
                if (string.IsNullOrWhiteSpace(texto)) continue;

                string normalizado = LdpUpdateService.NormalizarNomePasta(texto);
                if (aceitosNormalizados.Contains(normalizado))
                    candidatas.Add(col);
            }

            return candidatas.Count == 1 ? candidatas[0] : colunaPadrao;
        }
    }
}
