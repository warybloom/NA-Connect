using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using net.sf.mpxj;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>
    /// Resolve dinamicamente os campos customizados de um MPP (ProjectFile do MPXJ) pelo alias visível
    /// configurado pelo Planejamento — nunca por um campo interno fixo (TextN/NumberN). Se o alias
    /// "Fase" for movido de um TextN para outro TextN, a resolução continua funcionando, porque a busca
    /// é sempre por alias, nunca por posição/índice.
    ///
    /// A comparação de alias é por IGUALDADE após normalização (Trim, espaços duplicados, quebras de
    /// linha e espaço não separável colapsados, comparação case-insensitive) — nunca por Contains solto,
    /// que poderia casar aliases parecidos por coincidência de substring.
    /// </summary>
    public sealed class CronogramaTipo2FieldResolver
    {
        private readonly Dictionary<string, FieldType> _porAliasNormalizado = new Dictionary<string, FieldType>();
        private readonly Dictionary<string, FieldType> _aliasesOriginais = new Dictionary<string, FieldType>();

        public CronogramaTipo2FieldResolver(ProjectFile projectFile)
        {
            var customFields = projectFile?.getCustomFields();
            if (customFields == null) return;

            var iterator = customFields.iterator();
            while (iterator.hasNext())
            {
                var field = (CustomField)iterator.next();
                string aliasOriginal = field.getAlias()?.ToString();
                if (string.IsNullOrWhiteSpace(aliasOriginal)) continue;

                string aliasNormalizado = NormalizarAlias(aliasOriginal);

                // Em caso de dois campos customizados com o mesmo alias (não deveria acontecer, mas o
                // MPP é um arquivo externo), mantém o primeiro encontrado — nunca sobrescreve
                // silenciosamente uma resolução já feita.
                if (!_porAliasNormalizado.ContainsKey(aliasNormalizado))
                {
                    _porAliasNormalizado[aliasNormalizado] = field.getFieldType();
                }

                if (!_aliasesOriginais.ContainsKey(aliasOriginal))
                {
                    _aliasesOriginais[aliasOriginal] = field.getFieldType();
                }
            }
        }

        /// <summary>Quantidade de campos customizados com alias preenchido encontrados no MPP.</summary>
        public int TotalCamposComAlias => _aliasesOriginais.Count;

        /// <summary>Todos os aliases ORIGINAIS (texto exibido, não normalizado) -> campo interno resolvido.</summary>
        public IReadOnlyDictionary<string, FieldType> AliasesOriginais => _aliasesOriginais;

        public bool TentarResolver(string aliasEsperado, out FieldType fieldType)
        {
            return _porAliasNormalizado.TryGetValue(NormalizarAlias(aliasEsperado), out fieldType);
        }

        /// <summary>
        /// Tenta resolver, em ordem, cada alias candidato (seção 5 do pedido da Fase 4B: dois nomes
        /// controlados podem significar a mesma informação, ex.: "NA DOCUMENT NUMBER" / "N&amp;A DOCUMENT
        /// NUMBER", ou "TÉRMINO" / "TERMINO"). Devolve o primeiro que resolver e qual alias exatamente
        /// bateu — nunca combina nem faz fuzzy matching entre eles.
        /// </summary>
        public bool TentarResolverQualquer(IReadOnlyList<string> aliasesCandidatos, out FieldType fieldType, out string aliasResolvido)
        {
            foreach (var alias in aliasesCandidatos)
            {
                if (_porAliasNormalizado.TryGetValue(NormalizarAlias(alias), out fieldType))
                {
                    aliasResolvido = alias;
                    return true;
                }
            }

            fieldType = default;
            aliasResolvido = null;
            return false;
        }

        /// <summary>Um TÍTULO de coluna de Table encontrado durante <see cref="TentarResolverPorTituloDeTabela"/> —
        /// devolvido mesmo quando a resolução falha (ambíguo/nenhum), só para diagnóstico/log.</summary>
        public sealed class TituloColunaEncontrado
        {
            public FieldType FieldType { get; init; }
            public string Tabela { get; init; }
            public string TituloOriginal { get; init; }
        }

        /// <summary>
        /// Segunda via de resolução (HOTFIX 4 — checkpoint "Resolução de % Realizado por título de
        /// coluna"): quando NENHUM CustomField tem alias igual a um dos candidatos, procura em TODAS as
        /// Table de TAREFA do MPP (as mesmas definições que o MS Project usa para nomear colunas visíveis
        /// em qualquer view — comprovado no MPP real: "% Realizado" existe em CTable/Var2Data como título
        /// de coluna da Table "Entry", apontando para TaskField.TEXT3, que não tem alias nenhum em
        /// TBkndTask/Props) uma coluna cujo título normalizado seja EXATAMENTE igual a um dos candidatos.
        /// Mesma regra do Tipo 1 (AccScheduleSyncService.ResolverRealizadoViaTituloColunaTabela): só
        /// aceita quando exatamente UM FieldType distinto bate — nunca escolhe arbitrariamente entre
        /// vários, nunca decide por valor de tarefa. Tables de RECURSO (getResourceFlag()==true) são
        /// ignoradas — o conceito é sempre de tarefa.
        /// </summary>
        public bool TentarResolverPorTituloDeTabela(
            ProjectFile projectFile,
            IReadOnlyList<string> titulosCandidatos,
            out FieldType fieldType,
            out IReadOnlyList<TituloColunaEncontrado> candidatosEncontrados)
        {
            fieldType = default;
            var candidatos = new List<TituloColunaEncontrado>();
            candidatosEncontrados = candidatos;

            var titulosNormalizados = new HashSet<string>(titulosCandidatos.Select(NormalizarAlias));

            var tables = projectFile?.getTables();
            if (tables == null) return false;

            var itTables = tables.iterator();
            while (itTables.hasNext())
            {
                var tabela = itTables.next() as net.sf.mpxj.Table;
                if (tabela == null || tabela.getResourceFlag()) continue; // conceito é sempre de TAREFA

                var colunas = tabela.getColumns();
                if (colunas == null) continue;

                var itColunas = colunas.iterator();
                while (itColunas.hasNext())
                {
                    var coluna = itColunas.next() as Column;
                    string titulo = coluna?.getTitle();
                    if (string.IsNullOrWhiteSpace(titulo)) continue;
                    if (!titulosNormalizados.Contains(NormalizarAlias(titulo))) continue;

                    candidatos.Add(new TituloColunaEncontrado
                    {
                        FieldType = coluna.getFieldType(),
                        Tabela = tabela.getName(),
                        TituloOriginal = titulo
                    });
                }
            }

            var distintos = candidatos.Select(c => c.FieldType).Distinct().ToList();
            if (distintos.Count != 1) return false; // 0 = não encontrado; >1 = ambíguo — nunca escolhe arbitrariamente

            fieldType = distintos[0];
            return true;
        }

        public static string NormalizarAlias(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias)) return string.Empty;

            string semQuebras = alias.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            string semNbsp = semQuebras.Replace(' ', ' ');
            string colapsado = Regex.Replace(semNbsp, @"\s+", " ").Trim();

            return colapsado.ToUpperInvariant();
        }
    }
}
