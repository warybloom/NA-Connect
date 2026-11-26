namespace APS.WebApp.Services.Qualidade
{
    /// <summary>
    /// Nomes SQL de destino das três tabelas de Qualidade — únicos valores aceitos, nunca vindos de
    /// entrada externa (seção 3/6 do pedido: [dbo].[NCR NONCONFORMITY REPORT] passa a ser exclusivo da
    /// aba RNC; a antiga estrutura, renomeada para [dbo].[NCR BASE LANCAMENTO], recebe a aba BASE
    /// LANÇAMENTO; [dbo].[DESVIO DE QUALIDADE] é nova).
    /// </summary>
    public static class QualidadeTabelas
    {
        public const string NcrBaseLancamento = "[dbo].[NCR BASE LANCAMENTO]";
        public const string NcrNonconformityReport = "[dbo].[NCR NONCONFORMITY REPORT]";
        public const string DesvioQualidade = "[dbo].[DESVIO DE QUALIDADE]";
    }
}
