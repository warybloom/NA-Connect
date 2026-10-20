namespace APS.WebApp.Models
{
    /// <summary>
    /// Uma linha do BLOCO TABULAR PRINCIPAL da aba "Custo profissionais" (oculta) da LDP — custo de
    /// REFERÊNCIA/PLANEJADO por função×disciplina, nunca confundido com custo real por pessoa
    /// (gestao.CustoProfissionalRealHistorico). O bloco "MIX" (percentuais Sênior/Pleno/Júnior) desta
    /// mesma aba NÃO é extraído nesta etapa (layout não tabular padronizado) — ver
    /// AUDITORIA_FORGESYNC_NA/MAPA_DADOS_LDP_FINANCEIRO.md seção 3.
    /// </summary>
    public sealed class GestaoVistaCustoPrevistoExtraido
    {
        public int LinhaExcel { get; set; }

        public string Funcao { get; set; }

        /// <summary>Coluna B da aba — sem texto de cabeçalho, mas populada (agrupamento tipo "ENGINEER"/
        /// "DESIGNER"). Resolvida por posição estrutural (coluna logo após Função, antes de Disciplina),
        /// nunca por índice fixo absoluto.</summary>
        public string GrupoFuncao { get; set; }

        public string Disciplina { get; set; }

        public string Recurso { get; set; }

        public decimal? Remuneracao { get; set; }

        public decimal? Encargos { get; set; }

        public decimal? Softwares { get; set; }

        public decimal? Computadores { get; set; }

        public decimal? CustoTotalMensal { get; set; }

        public decimal? CustoHora { get; set; }
    }
}
