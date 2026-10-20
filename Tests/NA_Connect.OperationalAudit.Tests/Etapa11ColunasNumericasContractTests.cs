using System.Text.RegularExpressions;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Correção 22/09/2026 (Custo Previsto BML011.013 — R$ 57.640 faltando): na Etapa 11 do
    /// GrdAutomationWorker, InsinuaSerData trata qualquer número entre 35000 e 60000 como serial de data do
    /// Excel. Planned_Cost=57640 (BML011.013-000-3D0-AT-001) virava 2057-10-22 e era gravado NULL em
    /// dbo.Ldp_Unificada. A heurística de data não pode mais ser aplicada às colunas numéricas. A Etapa 11
    /// é código inline do worker (sem costura de teste), então a garantia é verificada contra o
    /// TEXTO-FONTE REAL (mesmo padrão de Etapa07RecursaoUnicaContractTests).
    /// </summary>
    public class Etapa11ColunasNumericasContractTests
    {
        private static string LerWorker() => RepoFileLocator.LerArquivoDaRaiz("Services", "GrdAutomationWorker.cs");

        [Fact]
        public void HeuristicaDeData_NaoSeAplicaAsColunasNumericas()
        {
            Assert.Contains("if (!ColunasNumericasLdpUnificada.Contains(col) && InsinuaSerData(valorBruto))", LerWorker());
        }

        [Fact]
        public void ColunasNumericas_CobremHhECustoDaLdpUnificada()
        {
            var m = Regex.Match(LerWorker(), @"ColunasNumericasLdpUnificada = new\(\) \{ (?<cols>[\d, ]+) \}");
            Assert.True(m.Success, "Declaração de ColunasNumericasLdpUnificada não encontrada.");
            var cols = m.Groups["cols"].Value.Split(',').Select(c => int.Parse(c.Trim())).ToHashSet();
            // 30..35 = Planned_HH, Actual_HH, Remaining_HH, Planned_Cost, Actual_Cost, Remaining_Cost (DatabaseService.SalvarDadosLdpUnificada).
            foreach (var c in new[] { 30, 31, 32, 33, 34, 35 }) Assert.Contains(c, cols);
            // Colunas de data (9, 10, 11) continuam passando pela heurística.
            foreach (var c in new[] { 9, 10, 11 }) Assert.DoesNotContain(c, cols);
        }
    }
}
