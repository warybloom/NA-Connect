using System;
using System.Collections.Generic;

namespace APS.WebApp.Services.Qualidade
{
    /// <summary>
    /// Nomes canônicos das três abas obrigatórias do arquivo de Qualidade (seção 7 do pedido). A
    /// comparação de nome de aba tolera maiúsculas/minúsculas, espaços nas pontas e acentos — nunca
    /// Contains/FirstWorksheet/Worksheets[0] (ver QualidadeAbaLocator).
    /// </summary>
    public static class QualidadeAbas
    {
        public const string BaseLancamento = "BASE LANÇAMENTO";
        public const string Rnc = "RNC";
        public const string DesvioQualidade = "DESVIO DE QUALIDADE";
    }

    /// <summary>
    /// Mapas explícitos Campo -&gt; cabeçalho(s) de Excel para as três abas, e os campos obrigatórios de
    /// cada uma (bloqueiam o preflight se ausentes do cabeçalho, seção 9/11). Cada chave é usada tanto
    /// como nome de coluna no DataTable preparado quanto como destino em SqlBulkCopy.ColumnMappings —
    /// nunca posição ordinal.
    /// </summary>
    public static class QualidadeCabecalhos
    {
        /// <summary>
        /// Valor único aplicado a um campo textual obrigatório vazio numa linha real (seção 2/3 do
        /// pedido de preenchimento automático). Centralizado aqui — nunca espalhado como literal em
        /// outros arquivos. Grafia exata: com espaço e acentuação, "FALTA INFORMAÇÃO".
        /// </summary>
        public const string ValorCampoObrigatorioAusente = "FALTA INFORMAÇÃO";

        /// <summary>
        /// BASE LANÇAMENTO preserva o mapeamento de negócio já existente e validado em produção (antigo
        /// AliasesCamposNcr) — só o destino SQL muda, de [dbo].[NCR NONCONFORMITY REPORT] para
        /// [dbo].[NCR BASE LANCAMENTO] (seção 6 do pedido). Continua tolerando múltiplos aliases por
        /// campo (ex.: variações EN/PT), mas sempre por correspondência EXATA após normalização —
        /// nenhum Contains.
        /// </summary>
        public static readonly Dictionary<string, string[]> BaseLancamento = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Cliente"] = new[] { "CLIENTE", "CLIENT" },
            ["CR"] = new[] { "CR" },
            // "CÓDIGO N&A" é o cabeçalho real encontrado na BML011.013 (arquivo CONTROLE EMISSÕES.xlsx,
            // aba BASE LANÇAMENTO, linha 2) — normaliza para "CODIGO N A" (três tokens), diferente de
            // "N&A" isolado (que normaliza para "N A", dois tokens). Os dois aliases convivem porque
            // arquivos de outros projetos podem usar qualquer uma das duas formas.
            ["CodigoNA"] = new[] { "CODIGO NA", "CÓDIGO NA", "CÓDIGO N&A", "CODIGO N&A", "N&A", "CODIGO", "CÓDIGO", "NA DOCUMENT NUMBER", "N&A DOCUMENT NUMBER" },
            ["CodigoCliente"] = new[] { "CODIGO CLIENTE", "CÓDIGO CLIENTE", "CLIENT DOCUMENT NUMBER", "NUMERO DOCUMENTO CLIENTE", "NÚMERO DOCUMENTO CLIENTE" },
            ["Descricao"] = new[] { "DESCRICAO", "DESCRIÇÃO", "DESCRIPTION" },
            ["Disciplina"] = new[] { "DISCIPLINA", "DISCIPLINE" },
            ["Revisao"] = new[] { "REVISAO", "REVISÃO", "REV", "REV." },
            // "Nº VIAS" é o cabeçalho real encontrado na BML011.013 — mantido junto com as variações
            // já cadastradas ("NÚMERO DE VIAS" etc.) para outros projetos.
            ["NumeroVias"] = new[] { "NUMERO DE VIAS", "NÚMERO DE VIAS", "Nº VIAS", "N° VIAS", "VIAS", "NUMBER OF COPIES" },
            ["Mes"] = new[] { "MES", "MÊS", "MONTH" },
            ["DataEnvio"] = new[] { "DATA DE ENVIO", "DATA ENVIO", "SEND DATE", "DATE SENT" },
            ["Emitente"] = new[] { "EMITENTE", "ISSUER", "ISSUED BY" },
            ["GRD"] = new[] { "GRD" }
        };

        // CodigoNA É obrigatório (readicionado): a validação real mostrou que a coluna SQL real
        // [dbo].[NCR BASE LANCAMENTO].[CodigoNA] é NOT NULL — gravar DBNull.Value nela faz o BulkCopy
        // inteiro falhar ("Column 'CodigoNA' does not allow DBNull.Value"). ~4046 linhas legítimas
        // (Cliente/CR/Descricao preenchidos) não têm "CÓDIGO N&A" atribuído ainda, só "CÓDIGO CLIENTE".
        // Antes desta sessão isso bloqueava a linha inteira; agora "obrigatório" significa preenchimento
        // automático com "FALTA INFORMAÇÃO" quando vazio (nunca rejeição) — resolve as duas restrições
        // (SQL NOT NULL e dado de negócio real incompleto) sem alterar o schema.
        public static readonly string[] BaseLancamentoObrigatorios = { "Cliente", "CR", "CodigoNA", "Descricao" };

        // "Mes" é TINYINT em [dbo].[NCR BASE LANCAMENTO], mas o Excel real preenche com abreviação do
        // mês ("JAN"/"FEV"...) — descoberto na validação real quando o SqlBulkCopy rejeitou a tabela
        // inteira com "The given value 'JAN' ... cannot be converted to type tinyint". Convertido via
        // QualidadeParsers.TentarParseMes (nome → 1-12); não reconhecido vira NULL com Warning, nunca
        // bloqueia a linha.
        public static readonly string[] BaseLancamentoCamposMes = { "Mes" };

        /// <summary>
        /// Mapeamento exato pedido para a aba RNC (seção 4) — um cabeçalho por campo, sem fallback
        /// fuzzy. "RESPONSAVEL" nunca casa com "RESPONSAVEL AÇÃO" porque a comparação é de igualdade
        /// exata após normalização, nunca Contains.
        /// </summary>
        public static readonly Dictionary<string, string[]> Rnc = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DataRegistro"] = new[] { "DATA REGISTRO" },
            ["CodigoFormulario"] = new[] { "CÓDIGO DO FORMULÁRIO" },
            ["Projeto"] = new[] { "PROJETO" },
            ["CR"] = new[] { "CR" },
            ["Disciplina"] = new[] { "DISCIPLINA" },
            ["Responsavel"] = new[] { "RESPONSÁVEL" },
            ["DescricaoSituacao"] = new[] { "DESCRIÇÃO DA SITUAÇÃO" },
            ["ImpactoProjeto"] = new[] { "IMPACTO NO PROJETO" },
            ["TipoRNC"] = new[] { "TIPO DA RNC" },
            ["TipoAcao"] = new[] { "TIPO DE AÇÃO" },
            ["ResponsavelAcao"] = new[] { "RESPONSÁVEL AÇÃO" },
            ["Acao"] = new[] { "AÇÃO" },
            ["Status"] = new[] { "STATUS" },
            ["PrazoConclusao"] = new[] { "PRAZO DE CONCLUSÃO" },
            ["DataConclusao"] = new[] { "DATA DE CONCLUSÃO" },
            ["DiasEmAberto"] = new[] { "DIAS EM ABERTO" }
        };

        /// <summary>
        /// Campos mínimos exigidos na linha de cabeçalho para localizá-la sem ambiguidade (seção 8).
        /// </summary>
        public static readonly string[] RncCabecalhoMinimo = { "CodigoFormulario", "Projeto", "CR", "DescricaoSituacao" };

        /// <summary>
        /// Campos de negócio obrigatórios em CADA linha (não podem ficar vazios) — mesmo conjunto usado
        /// para localizar a linha de cabeçalho, por serem os identificadores de negócio do registro.
        /// </summary>
        public static readonly string[] RncObrigatorios = { "CodigoFormulario", "Projeto", "CR", "DescricaoSituacao" };

        /// <summary>
        /// Campos de data (parser controlado — nunca DateTime.MinValue silencioso, seção 10).
        /// </summary>
        public static readonly string[] RncCamposData = { "DataRegistro", "PrazoConclusao", "DataConclusao" };

        /// <summary>
        /// Campos inteiros (vazio -&gt; DBNull; inválido -&gt; erro de preflight, seção 10).
        /// </summary>
        public static readonly string[] RncCamposInteiros = { "DiasEmAberto" };

        /// <summary>
        /// Mapeamento exato pedido para a aba DESVIO DE QUALIDADE (seção 5).
        /// </summary>
        public static readonly Dictionary<string, string[]> DesvioQualidade = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Projeto"] = new[] { "PROJETO" },
            ["CR"] = new[] { "CR" },
            ["Disciplina"] = new[] { "DISCIPLINA" },
            ["NumeroDocumento"] = new[] { "NÚMERO DO DOCUMENTO" },
            ["QuantidadeDocumentos"] = new[] { "QTD. DE DOCUMENTOS" },
            ["DescricaoInconsistencia"] = new[] { "DESCRIÇÃO DA INCONSISTÊNCIA" },
            ["TipoInconsistencia"] = new[] { "TIPO DE INCONSISTÊNCIAS" },
            ["Responsavel"] = new[] { "RESPONSÁVEL" },
            ["DataConclusao"] = new[] { "DATA DA CONCLUSÃO" }
        };

        public static readonly string[] DesvioQualidadeCabecalhoMinimo = { "Projeto", "CR", "NumeroDocumento", "DescricaoInconsistencia" };

        public static readonly string[] DesvioQualidadeObrigatorios = { "Projeto", "CR", "NumeroDocumento", "DescricaoInconsistencia" };

        public static readonly string[] DesvioQualidadeCamposData = { "DataConclusao" };

        public static readonly string[] DesvioQualidadeCamposInteiros = { "QuantidadeDocumentos" };
    }
}
