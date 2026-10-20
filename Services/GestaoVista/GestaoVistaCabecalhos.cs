using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Nomes canônicos (normalizados) dos cabeçalhos das 3 abas internas da LDP usadas pela Gestão à
    /// Vista — nunca "números mágicos" textuais espalhados pelo código. Baseado em
    /// AUDITORIA_FORGESYNC_NA/MAPA_DADOS_LDP_FINANCEIRO.md (LDP legada auditada de fato). Aliases são
    /// SEMPRE explícitos (nunca fuzzy/Contains/Levenshtein) — só entram aqui variações comprovadamente
    /// prováveis (idioma/abreviação), nunca "parecidos".
    /// </summary>
    public static class GestaoVistaCabecalhos
    {
        // ---- Timesheet ----
        public const string Nome = "NOME";
        public const string Planejadas = "PLANEJADAS";
        public const string Trabalhadas = "TRABALHADAS";
        public const string Projeto = "PROJETO";
        public const string Funcao = "FUNCAO";
        public const string Disciplina = "DISCIPLINA";
        public const string DocumentoCodigo = "DOCUMENTO CODIGO";
        public const string Documento = "DOCUMENTO";
        public const string Descricao = "DESCRICAO";
        public const string Doc1 = "DOC1";
        public const string Doc2 = "DOC2";
        public const string Doc3 = "DOC3";
        public const string Doc4 = "DOC4";
        public const string Doc5 = "DOC5";
        public const string Data = "DATA";
        /// <summary>Coluna CUSTO dentro da própria aba Timesheet (não a aba/worksheet "CUSTO" — essa
        /// continua ignorada por GestaoVistaWorksheetLocator). Opcional: nem toda LDP legada tem esta
        /// coluna; quando ausente, CustoInformadoNaPlanilha fica NULL, nunca inventado.</summary>
        public const string CustoInformado = "CUSTO";

        // ---- Custo Real / Custo profissionais (compartilhados) ----
        public const string Recurso = "RECURSO";
        public const string Remuneracao = "REMUNERACAO";
        public const string Encargos = "ENCARGOS";
        public const string Softwares = "SOFTWARES";
        public const string Computadores = "COMPUTADORES";
        public const string Total = "TOTAL";
        public const string CustoHora = "CUSTO HORA";

        public static readonly IReadOnlyList<string> AncorasTimesheet = new[] { Nome, Trabalhadas };
        public static readonly IReadOnlyList<string> AncorasCustoReal = new[] { Nome, CustoHora };
        public static readonly IReadOnlyList<string> AncorasCustoPrevisto = new[] { Funcao, Disciplina, CustoHora };

        /// <summary>
        /// Aliases explícitos: grafia alternativa normalizada -> grafia canônica normalizada. Nunca
        /// fuzzy — só variações de idioma/abreviação comprovadamente prováveis.
        /// </summary>
        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NAME"] = Nome,
            ["PLANNED"] = Planejadas,
            ["WORKED"] = Trabalhadas,
            ["PROJECT"] = Projeto,
            ["FUNCTION"] = Funcao,
            ["FUNÇÃO"] = Funcao,
            ["ROLE"] = Funcao,
            ["DISCIPLINE"] = Disciplina,
            ["DOCUMENTO CÓDIGO"] = DocumentoCodigo,
            ["DOCUMENT CODE"] = DocumentoCodigo,
            ["DESCRIÇÃO"] = Descricao,
            ["DESCRIPTION"] = Descricao,
            ["DOC 1"] = Doc1,
            ["DOC 2"] = Doc2,
            ["DOC 3"] = Doc3,
            ["DOC 4"] = Doc4,
            ["DOC 5"] = Doc5,
            ["DATE"] = Data,
            ["RESOURCE"] = Recurso,
            ["REMUNERAÇÃO"] = Remuneracao,
            ["SALARY"] = Remuneracao,
            ["SALARIO"] = Remuneracao,
            ["SALÁRIO"] = Remuneracao,
            ["CHARGES"] = Encargos,
            ["SOFTWARE"] = Softwares,
            ["COMPUTERS"] = Computadores,
            ["HARDWARE"] = Computadores,
            ["CUSTO/HORA"] = CustoHora,
            ["CUSTO POR HORA"] = CustoHora,
            ["COST PER HOUR"] = CustoHora,
            ["HOURLY COST"] = CustoHora,
        };

        /// <summary>Remove um prefixo numérico de ordenação conhecido desta LDP (ex.: "2. Planejadas",
        /// "10. Doc1") — só o prefixo "N." ou "N)" no INÍCIO do texto, nunca no meio.</summary>
        private static readonly Regex PrefixoNumerico = new(@"^\d+[\.\)]\s*", RegexOptions.Compiled);

        /// <summary>
        /// Normaliza um cabeçalho para comparação: trim, colapsa espaços, maiusculiza, remove prefixo
        /// numérico de ordenação e resolve alias explícito. Nunca usa Contains/fuzzy.
        /// </summary>
        public static string Normalizar(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return string.Empty;

            string texto = valor.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            texto = Regex.Replace(texto, @"\s+", " ");
            texto = PrefixoNumerico.Replace(texto, string.Empty).Trim();
            texto = texto.Replace(" / ", "/").Replace("Custo/hora", "CUSTO HORA", StringComparison.OrdinalIgnoreCase);
            string normalizado = texto.ToUpperInvariant();

            // "CUSTO/HORA" sem os espaços ao redor da barra também deve resolver para o canônico "CUSTO HORA".
            normalizado = normalizado.Replace("CUSTO/HORA", CustoHora, StringComparison.OrdinalIgnoreCase);

            return Aliases.TryGetValue(normalizado, out var canonico) ? canonico : normalizado;
        }
    }
}
