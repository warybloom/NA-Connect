using APS.WebApp.Models;

namespace APS.WebApp.Services.Qualidade
{
    /// <summary>
    /// Monta a <see cref="QualidadeTabelaDefinicao"/> de cada uma das três tabelas de Qualidade a partir
    /// dos aliases/campos já aprovados em <see cref="QualidadeCabecalhos"/> e dos nomes reais de tabela em
    /// <see cref="QualidadeTabelas"/> — nenhum alias/mapa foi alterado aqui, só reorganizado para permitir
    /// que cada tabela seja processada como um pipeline independente (seção 11 do pedido).
    /// </summary>
    public static class QualidadeTabelaDefinicoes
    {
        public static QualidadeTabelaDefinicao BaseLancamento() => new QualidadeTabelaDefinicao
        {
            NomeAba = QualidadeAbas.BaseLancamento,
            TabelaDestino = QualidadeTabelas.NcrBaseLancamento,
            Aliases = QualidadeCabecalhos.BaseLancamento,
            CamposMinimosCabecalho = QualidadeCabecalhos.BaseLancamentoObrigatorios,
            CamposObrigatoriosNaLinha = QualidadeCabecalhos.BaseLancamentoObrigatorios,
            CamposData = System.Array.Empty<string>(),
            CamposInteiros = System.Array.Empty<string>(),
            CamposMes = QualidadeCabecalhos.BaseLancamentoCamposMes
        };

        public static QualidadeTabelaDefinicao Rnc() => new QualidadeTabelaDefinicao
        {
            NomeAba = QualidadeAbas.Rnc,
            TabelaDestino = QualidadeTabelas.NcrNonconformityReport,
            Aliases = QualidadeCabecalhos.Rnc,
            CamposMinimosCabecalho = QualidadeCabecalhos.RncCabecalhoMinimo,
            CamposObrigatoriosNaLinha = QualidadeCabecalhos.RncObrigatorios,
            CamposData = QualidadeCabecalhos.RncCamposData,
            CamposInteiros = QualidadeCabecalhos.RncCamposInteiros
        };

        public static QualidadeTabelaDefinicao DesvioQualidade() => new QualidadeTabelaDefinicao
        {
            NomeAba = QualidadeAbas.DesvioQualidade,
            TabelaDestino = QualidadeTabelas.DesvioQualidade,
            Aliases = QualidadeCabecalhos.DesvioQualidade,
            CamposMinimosCabecalho = QualidadeCabecalhos.DesvioQualidadeCabecalhoMinimo,
            CamposObrigatoriosNaLinha = QualidadeCabecalhos.DesvioQualidadeObrigatorios,
            CamposData = QualidadeCabecalhos.DesvioQualidadeCamposData,
            CamposInteiros = QualidadeCabecalhos.DesvioQualidadeCamposInteiros
        };
    }
}
