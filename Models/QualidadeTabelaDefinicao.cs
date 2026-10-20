using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Configuração explícita de UMA das três tabelas de Qualidade — separa nome da aba, tabela SQL de
    /// destino, aliases de cabeçalho e quais campos são obrigatórios/tipados. Usada para processar cada
    /// tabela como um pipeline independente (localizar aba → resolver cabeçalho → preparar linhas →
    /// persistir em transação própria — seções 11/12 do pedido).
    /// </summary>
    public sealed class QualidadeTabelaDefinicao
    {
        public string NomeAba { get; set; }
        public string TabelaDestino { get; set; }
        public Dictionary<string, string[]> Aliases { get; set; }
        public string[] CamposMinimosCabecalho { get; set; }
        public string[] CamposObrigatoriosNaLinha { get; set; }
        public string[] CamposData { get; set; }
        public string[] CamposInteiros { get; set; }

        /// <summary>
        /// Campos com conversão determinística própria (hoje só "Mes" de BASE LANÇAMENTO — nome do mês
        /// PT/EN para número 1-12, coluna SQL TINYINT). Nunca bloqueia a linha: valor não reconhecido vira
        /// DBNull com Warning, nunca erro/rejeição — ver QualidadeParsers.TentarParseMes.
        /// </summary>
        public string[] CamposMes { get; set; } = System.Array.Empty<string>();
    }
}
