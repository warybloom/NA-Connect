using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Diagnóstico de UMA worksheet do workbook da LDP, examinada pelo extrator de Gestão à Vista
    /// (Services/GestaoVista/GestaoVistaWorksheetLocator). Sempre enumera TODAS as abas do workbook
    /// (Visible/Hidden/VeryHidden) — visibilidade nunca decide se uma aba é relevante, só é registrada
    /// aqui para diagnóstico. Ver AUDITORIA_FORGESYNC_NA/MAPA_DADOS_LDP_FINANCEIRO.md.
    /// </summary>
    public sealed class GestaoVistaWorksheetDiagnostico
    {
        public string NomeAba { get; set; }

        /// <summary>"Visible" | "Hidden" | "VeryHidden" — texto do enum eWorkSheetHidden do EPPlus.</summary>
        public string Visibilidade { get; set; }

        /// <summary>Papel identificado para esta aba: "Timesheet" | "CustoReal" | "CustoProfissionais" |
        /// "LDP" | "CUSTO_IGNORADA" | null (aba não reconhecida/não usada nesta etapa).</summary>
        public string PapelIdentificado { get; set; }

        public bool Encontrada { get; set; }

        public int? LinhaCabecalho { get; set; }

        public int QuantidadeLinhas { get; set; }

        public List<string> ColunasEncontradas { get; set; } = new();

        public List<string> ColunasAusentes { get; set; } = new();

        public List<string> Warnings { get; set; } = new();
    }
}
