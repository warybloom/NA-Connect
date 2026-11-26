using System;
using System.Collections.Generic;
using System.Linq;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Uma ocorrência de cabeçalho na linha 7: coluna, texto original (como está na planilha) e texto
    /// normalizado (ver LdpTipo2HeaderResolver.NormalizarCabecalho). Preserva o texto original apenas
    /// para diagnóstico/log — toda comparação de layout usa o texto normalizado.
    /// </summary>
    public sealed class LdpTipo2HeaderOccurrence
    {
        public int Coluna { get; set; }
        public string TextoOriginal { get; set; }
        public string TextoNormalizado { get; set; }
    }

    /// <summary>
    /// Mapa de cabeçalhos da planilha da LDP Tipo 2: nome normalizado do cabeçalho -> lista de todas as
    /// colunas/ocorrências onde ele aparece. Usa lista (e não uma única coluna) porque o arquivo possui
    /// cabeçalhos repetidos, como "Fase 30%" (aparece tanto no bloco manual do gate quanto no indicador
    /// de aplicabilidade consecutivo — ver LdpTipo2HeaderResolver) — nunca assumir que um cabeçalho é
    /// único sem verificar.
    /// </summary>
    public sealed class LdpTipo2HeaderMap
    {
        private readonly Dictionary<string, List<int>> _colunasPorCabecalho;
        private readonly Dictionary<string, List<LdpTipo2HeaderOccurrence>> _ocorrenciasPorCabecalho;

        /// <summary>
        /// Construtor original da Fase 1 — mantido para não remover API pública existente.
        /// </summary>
        public LdpTipo2HeaderMap(Dictionary<string, List<int>> colunasPorCabecalho)
        {
            _colunasPorCabecalho = colunasPorCabecalho
                ?? throw new ArgumentNullException(nameof(colunasPorCabecalho));
            _ocorrenciasPorCabecalho = new Dictionary<string, List<LdpTipo2HeaderOccurrence>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Construtor usado pelo parser (Fase 2): recebe a lista ordenada (por coluna) de TODAS as
        /// ocorrências não vazias da linha 7 e monta os dois índices (por cabeçalho -> colunas, e por
        /// cabeçalho -> ocorrências completas).
        /// </summary>
        public LdpTipo2HeaderMap(IEnumerable<LdpTipo2HeaderOccurrence> ocorrencias)
        {
            if (ocorrencias == null) throw new ArgumentNullException(nameof(ocorrencias));

            var todas = ocorrencias.OrderBy(o => o.Coluna).ToList();

            _ocorrenciasPorCabecalho = todas
                .GroupBy(o => o.TextoNormalizado, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            _colunasPorCabecalho = _ocorrenciasPorCabecalho
                .ToDictionary(kv => kv.Key, kv => kv.Value.Select(o => o.Coluna).ToList(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Retorna todas as colunas onde o cabeçalho aparece (pode ser mais de uma, ex.: cabeçalhos
        /// repetidos de gate). Nunca lança exceção para um cabeçalho ausente — retorna lista vazia.
        /// </summary>
        public IReadOnlyList<int> ObterColunas(string cabecalho)
        {
            if (string.IsNullOrWhiteSpace(cabecalho)) return Array.Empty<int>();

            return _colunasPorCabecalho.TryGetValue(cabecalho, out var colunas)
                ? colunas
                : Array.Empty<int>();
        }

        /// <summary>
        /// Retorna a coluna somente quando o cabeçalho aparecer EXATAMENTE uma vez — nunca escolhe
        /// arbitrariamente a primeira ocorrência de um cabeçalho repetido.
        /// </summary>
        public int? ObterColunaUnica(string cabecalho)
        {
            var colunas = ObterColunas(cabecalho);
            return colunas.Count == 1 ? colunas[0] : (int?)null;
        }

        /// <summary>
        /// Retorna todas as ocorrências completas (coluna + texto original + normalizado) de um
        /// cabeçalho — usado para diagnóstico e para a resolução de blocos/sequências.
        /// </summary>
        public IReadOnlyList<LdpTipo2HeaderOccurrence> ObterOcorrencias(string cabecalho)
        {
            if (string.IsNullOrWhiteSpace(cabecalho)) return Array.Empty<LdpTipo2HeaderOccurrence>();

            return _ocorrenciasPorCabecalho.TryGetValue(cabecalho, out var ocorrencias)
                ? ocorrencias
                : Array.Empty<LdpTipo2HeaderOccurrence>();
        }
    }
}
