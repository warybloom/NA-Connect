using System.Collections.Generic;
using OfficeOpenXml;

namespace NA_Connect.LdpTipo2.Tests.TestHelpers
{
    /// <summary>Colunas físicas de UM gate (bloco de dados manuais) montadas por
    /// LdpTipo2WorkbookHelper.MontarAbaLdp — devolvidas ao teste para escrever as linhas de documento sem
    /// jamais hardcodar letra/número de coluna (a posição é decidida pelo helper, não pelo teste).</summary>
    internal sealed class LdpTipo2WorkbookGateColunas
    {
        public int ColunaFase { get; set; }
        public int ColunaDocumentStatus { get; set; }
        public int ColunaSituation { get; set; }
        public int ColunaPlannedBaseline { get; set; }
        public int ColunaReplannedDate { get; set; }
        public int ColunaPlannedPhysical { get; set; }
        public int ColunaPhysicalCompleted { get; set; }
    }

    internal sealed class LdpTipo2WorkbookLayout
    {
        public ExcelWorksheet Worksheet { get; set; } = null!;
        public int LinhaCabecalho { get; set; }
        public int ColunaItem { get; set; }
        public int ColunaNa { get; set; }
        public int ColunaCliente { get; set; }
        public int ColunaTitulo { get; set; }
        public Dictionary<byte, LdpTipo2WorkbookGateColunas> Gates { get; set; } = new();
        public Dictionary<byte, int> ColunaAplicabilidade { get; set; } = new();
    }

    /// <summary>
    /// Monta, em memória (EPPlus — mesma lib usada pelo parser real), uma aba "LDP" sintética que
    /// reproduz fielmente a estrutura da LDP Tipo 2: cabeçalhos duplicados "Fase {p}%" em dois blocos com
    /// função diferente (dados manuais x indicador de aplicabilidade), campos comuns e por-gate. A
    /// ORDEM física das colunas e a LINHA do cabeçalho são sempre parametrizáveis — nenhum teste deve
    /// depender de uma ordem/linha fixa, exatamente a regra que o parser real também segue.
    /// </summary>
    internal static class LdpTipo2WorkbookHelper
    {
        public static ExcelPackage NovoPacote()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            return new ExcelPackage();
        }

        /// <summary>Papéis dos 4 cabeçalhos companheiros de "Fase {p}%" dentro do bloco de dados de um
        /// gate — usado só para parametrizar a ORDEM interna nos testes de reordenação (item 5 do
        /// checkpoint); o resolvedor real não depende dessa ordem.</summary>
        internal enum CampoBlocoGate { Status, Situation, Baseline, Replanned }

        private static readonly CampoBlocoGate[] OrdemPadraoBlocoGate =
        {
            CampoBlocoGate.Status, CampoBlocoGate.Situation, CampoBlocoGate.Baseline, CampoBlocoGate.Replanned
        };

        private static readonly byte[] OrdemPadraoAplicabilidade = { 30, 60, 90 };

        /// <param name="pkg">Pacote onde a aba será criada.</param>
        /// <param name="linhaCabecalho">Linha física do cabeçalho — pode ser qualquer valor, inclusive != 7.</param>
        /// <param name="ordemGates">Ordem física em que os blocos de DADOS de cada gate aparecem (o bloco de
        /// aplicabilidade é sempre escrito depois, na ordem fixa 30/60/90 exigida pela regra de negócio).</param>
        /// <param name="incluirClientDocumentNumber">Se false, omite a coluna CLIENT DOCUMENT NUMBER (para
        /// testar o cenário em que a âncora de localização de linha não pode ser satisfeita).</param>
        /// <param name="aplicabilidadeAntesDoBlocoDeGates">Se true, escreve o indicador de aplicabilidade
        /// ANTES dos blocos de dados — prova que a posição relativa entre os dois não é assumida em lugar
        /// nenhum do resolvedor.</param>
        /// <param name="ordemCamposDentroDoBlocoGate">Ordem interna dos 4 cabeçalhos companheiros de
        /// "Fase {p}%" dentro do bloco de dados de CADA gate (item 5 do checkpoint: deve continuar
        /// funcionando em qualquer ordem). Default: Status/Situation/Baseline/Replanned.</param>
        /// <param name="ordemAplicabilidade">Ordem interna do trio Fase30/Fase60/Fase90 dentro do
        /// indicador de aplicabilidade (item 5). Default: 30/60/90.</param>
        public static LdpTipo2WorkbookLayout MontarAbaLdp(
            ExcelPackage pkg,
            int linhaCabecalho,
            byte[]? ordemGates = null,
            bool incluirClientDocumentNumber = true,
            bool aplicabilidadeAntesDoBlocoDeGates = false,
            CampoBlocoGate[]? ordemCamposDentroDoBlocoGate = null,
            byte[]? ordemAplicabilidade = null)
        {
            ordemGates ??= new byte[] { 30, 60, 90 };
            ordemCamposDentroDoBlocoGate ??= OrdemPadraoBlocoGate;
            ordemAplicabilidade ??= OrdemPadraoAplicabilidade;

            var ws = pkg.Workbook.Worksheets.Add("LDP");
            var layout = new LdpTipo2WorkbookLayout { Worksheet = ws, LinhaCabecalho = linhaCabecalho };

            int col = 1;
            void W(string texto)
            {
                ws.Cells[linhaCabecalho, col].Value = texto;
                col++;
            }

            layout.ColunaItem = col; W("ITEM");
            layout.ColunaNa = col; W("N&A DOCUMENT NUMBER");
            if (incluirClientDocumentNumber) { layout.ColunaCliente = col; W("CLIENT DOCUMENT NUMBER"); }
            layout.ColunaTitulo = col; W("DOCUMENT TITLE");

            void EscreverIndicadorAplicabilidade()
            {
                foreach (byte g in ordemAplicabilidade)
                {
                    layout.ColunaAplicabilidade[g] = col;
                    W($"Fase {g}%");
                }
            }

            if (aplicabilidadeAntesDoBlocoDeGates) EscreverIndicadorAplicabilidade();

            foreach (byte g in ordemGates)
            {
                var gc = new LdpTipo2WorkbookGateColunas();

                gc.ColunaFase = col; W($"Fase {g}%");

                foreach (var campo in ordemCamposDentroDoBlocoGate)
                {
                    switch (campo)
                    {
                        case CampoBlocoGate.Status: gc.ColunaDocumentStatus = col; W($"Document Status {g}%"); break;
                        case CampoBlocoGate.Situation: gc.ColunaSituation = col; W($"Situation {g}%"); break;
                        case CampoBlocoGate.Baseline: gc.ColunaPlannedBaseline = col; W($"Planned Data (Base Line) {g}%"); break;
                        case CampoBlocoGate.Replanned: gc.ColunaReplannedDate = col; W($"Replanned Date {g}%"); break;
                    }
                }

                gc.ColunaPlannedPhysical = col; W($"%Planned Physical {g}%");
                gc.ColunaPhysicalCompleted = col; W($"%Physical Completed {g}%");

                layout.Gates[g] = gc;
            }

            if (!aplicabilidadeAntesDoBlocoDeGates) EscreverIndicadorAplicabilidade();

            return layout;
        }

        /// <summary>Escreve uma linha de documento completa (N&A/Client/Título + os 3 indicadores de
        /// aplicabilidade) — os campos de dados manuais de cada gate ficam vazios a menos que
        /// preenchidos manualmente pelo teste via layout.Gates[p].</summary>
        public static void EscreverDocumento(
            LdpTipo2WorkbookLayout layout,
            int linha,
            string numeroNa,
            string? numeroCliente,
            string titulo,
            IReadOnlyDictionary<byte, string> aplicabilidade)
        {
            var ws = layout.Worksheet;
            ws.Cells[linha, layout.ColunaNa].Value = numeroNa;
            if (layout.ColunaCliente > 0) ws.Cells[linha, layout.ColunaCliente].Value = numeroCliente;
            ws.Cells[linha, layout.ColunaTitulo].Value = titulo;

            foreach (var kv in aplicabilidade)
            {
                ws.Cells[linha, layout.ColunaAplicabilidade[kv.Key]].Value = kv.Value;
            }
        }
    }
}
