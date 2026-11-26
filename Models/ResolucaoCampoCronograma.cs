using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Origem da resolução de um campo de percentual do Cronograma Tipo 1 — nunca um fallback silencioso
    /// para TaskField.TEXT2/TEXT3. Ver seção 3 da manutenção técnica (incidente BML011.013/017/021/022/023:
    /// REALIZADO não encontrado caindo silenciosamente em TEXT2, o mesmo campo que PREVISTO resolveu
    /// corretamente).
    /// </summary>
    public enum OrigemResolucaoCampoCronograma
    {
        CUSTOM_ALIAS_EXATO,
        CUSTOM_ALIAS_CONTROLADO,

        /// <summary>
        /// Campo (nativo ou customizado sem alias) comprovado por METADADO ESTRUTURAL: uma coluna de
        /// alguma Table/View do próprio MPP tem título (Column.getTitle(), independente de valores de
        /// tarefas) igual, após normalização, a um dos aliases controlados do conceito. Substitui o
        /// antigo fallback por "5 amostras reais" (nunca prova de identidade — 5 zeros passavam como
        /// comprovados; ver incidente do log Execucao_Completa_20260729_133615).
        /// </summary>
        TITULO_COLUNA_TABELA_COMPROVADO,

        NAO_ENCONTRADO,
        AMBIGUO
    }

    /// <summary>
    /// Resultado estruturado da resolução de UM conceito (REALIZADO ou PREVISTO) contra os campos
    /// customizados/nativos do MPP. Nunca devolve um FieldType "adivinhado" — quando não encontrado ou
    /// ambíguo, Encontrado=false e o chamador é obrigado a tratar o erro (nunca persistir/gravar LDP).
    /// </summary>
    public sealed class ResolucaoCampoCronograma
    {
        public string Conceito { get; set; }
        public bool Encontrado { get; set; }
        public string FieldType { get; set; }
        public string AliasOriginal { get; set; }
        public string AliasNormalizado { get; set; }
        public OrigemResolucaoCampoCronograma OrigemResolucao { get; set; }
        public string RegraUtilizada { get; set; }
        public int QuantidadeCandidatos { get; set; }
        public List<string> Candidatos { get; set; } = new List<string>();
        public string CodigoErro { get; set; }
        public string Mensagem { get; set; }

        public override string ToString() =>
            Encontrado
                ? $"{Conceito}: FieldType={FieldType} | Alias='{AliasOriginal}' | Origem={OrigemResolucao} | Regra={RegraUtilizada}"
                : $"{Conceito}: NAO ENCONTRADO | Codigo={CodigoErro} | Candidatos={QuantidadeCandidatos} [{string.Join(", ", Candidatos)}] | {Mensagem}";
    }
}
