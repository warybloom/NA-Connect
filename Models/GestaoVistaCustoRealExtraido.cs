namespace APS.WebApp.Models
{
    /// <summary>
    /// Uma linha lida da aba "Custo Real" (oculta) da LDP — custo REAL por profissional, específico deste
    /// projeto/LDP (o mesmo profissional pode ter custo diferente em outro projeto — nunca uma tabela
    /// global por pessoa). CustoTotalMensal/CustoHora são SEMPRE recalculados em C# a partir dos 4
    /// componentes (nunca lidos da fórmula da célula) — ver
    /// AUDITORIA_FORGESYNC_NA/MAPA_DADOS_LDP_FINANCEIRO.md seção 2: Custo/hora = (Remuneração + Encargos +
    /// Softwares + Computadores) / 176.
    /// </summary>
    public sealed class GestaoVistaCustoRealExtraido
    {
        public int LinhaExcel { get; set; }

        public string Funcao { get; set; }

        public string Disciplina { get; set; }

        public string NomeProfissional { get; set; }

        public decimal? Remuneracao { get; set; }

        public decimal? Encargos { get; set; }

        public decimal? Softwares { get; set; }

        public decimal? Computadores { get; set; }

        /// <summary>Recalculado = soma dos 4 componentes acima (null tratado como 0 na soma, exatamente
        /// como a fórmula original do Excel avalia célula vazia) — null somente quando NENHUM componente
        /// estiver presente (linha vazia/incompleta, nunca persistida).</summary>
        public decimal? CustoTotalMensal { get; set; }

        /// <summary>Recalculado = CustoTotalMensal / 176. Null quando CustoTotalMensal for null.</summary>
        public decimal? CustoHora { get; set; }
    }
}
