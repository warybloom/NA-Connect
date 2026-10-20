using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace APS.WebApp.Services.Qualidade
{
    /// <summary>
    /// Resultado da resolução do cabeçalho de UMA aba: linha real do cabeçalho e o mapa completo
    /// Campo -&gt; Coluna Excel, por NOME (nunca por posição/ORDINAL_POSITION).
    /// </summary>
    public sealed class QualidadeCabecalhoResolvido
    {
        public int HeaderRow { get; set; }
        public Dictionary<string, int> Mapa { get; set; }
        public List<string> CabecalhosEncontrados { get; set; }
    }

    /// <summary>
    /// Localiza a linha real de cabeçalho de UMA aba de Qualidade dentro da faixa obrigatória
    /// 1 &lt;= HeaderRow &lt;= 10 — diferente da regra 3-10 das Etapas 12/13/14/16 (Planning): algumas
    /// planilhas de Qualidade começam o cabeçalho diretamente na linha 1, então as linhas 1 e 2 NÃO são
    /// excluídas aqui (só o Planning tem essa exclusão — ver PlanningHeaderResolver, intocado). Cada aba
    /// (BASE LANÇAMENTO/RNC/DESVIO DE QUALIDADE) chama este método separadamente, com seu próprio
    /// Worksheet e seu próprio dicionário de aliases — nunca compartilha HeaderRow nem mapa de colunas
    /// entre abas (ver QualidadePreflightService.ProcessarAba, chamado uma vez por aba). Zero candidatas
    /// -&gt; CABECALHO_NAO_ENCONTRADO. Mais de uma -&gt; CABECALHO_AMBIGUO. Nunca escolhe silenciosamente.
    /// </summary>
    public static class QualidadeHeaderResolver
    {
        public const int PrimeiraLinhaVarredura = 1;
        public const int UltimaLinhaVarredura = 10;

        public const string CodigoNaoEncontrado = "CABECALHO_NAO_ENCONTRADO";
        public const string CodigoAmbiguo = "CABECALHO_AMBIGUO";

        private sealed class OcorrenciaLinha
        {
            public Dictionary<string, List<int>> ColunasPorCampo { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> TextosEncontrados { get; } = new();
            public List<(int Coluna, string Texto)> TextosComPosicao { get; } = new();
        }

        public static QualidadeCabecalhoResolvido Resolver(
            ExcelWorksheet ws,
            string nomeAbaParaLog,
            Dictionary<string, string[]> aliasesPorCampo,
            string[] camposMinimosParaCandidatura,
            ILogger logger = null)
        {
            var aliasesNormalizadosPorCampo = aliasesPorCampo.ToDictionary(
                kv => kv.Key,
                kv => new HashSet<string>(kv.Value.Select(QualidadeNormalizador.NormalizarCabecalho)),
                StringComparer.OrdinalIgnoreCase);

            int maxCol = ws.Dimension?.End.Column ?? 0;
            int maxRow = Math.Min(UltimaLinhaVarredura, ws.Dimension?.End.Row ?? 0);

            // Diagnóstico somente-leitura (seção 6 do pedido): dump bruto de cada linha varrida, incluindo
            // regiões mescladas relevantes, ANTES de qualquer decisão de candidatura — nunca usado para
            // inventar alias, só para dar visibilidade do arquivo real durante o diagnóstico.
            var regioesMescladasNaFaixa = ws.MergedCells
                .Select(endereco => ws.Cells[endereco])
                .Where(range => range.Start.Row >= PrimeiraLinhaVarredura && range.Start.Row <= maxRow)
                .Select(range => $"{range.Start.Address}:{range.End.Address}=\"{range.Text?.Trim()}\"")
                .ToList();

            if (regioesMescladasNaFaixa.Count > 0)
            {
                logger?.LogInformation(
                    "[QUALIDADE][HEADER_DIAGNOSTICO] Aba={Aba} | CelulasMescladasNaFaixa=[{Mescladas}]",
                    nomeAbaParaLog, string.Join(", ", regioesMescladasNaFaixa));
            }

            var candidatas = new List<(int Linha, OcorrenciaLinha Ocorrencia)>();

            for (int row = PrimeiraLinhaVarredura; row <= maxRow; row++)
            {
                var ocorrencia = new OcorrenciaLinha();

                for (int col = 1; col <= maxCol; col++)
                {
                    string textoOriginal = ws.Cells[row, col].Text?.Trim();
                    if (string.IsNullOrWhiteSpace(textoOriginal)) continue;

                    ocorrencia.TextosEncontrados.Add(textoOriginal);
                    ocorrencia.TextosComPosicao.Add((col, textoOriginal));
                    string textoNormalizado = QualidadeNormalizador.NormalizarCabecalho(textoOriginal);

                    foreach (var campo in aliasesNormalizadosPorCampo)
                    {
                        if (!campo.Value.Contains(textoNormalizado)) continue;

                        if (!ocorrencia.ColunasPorCampo.TryGetValue(campo.Key, out var colunas))
                        {
                            colunas = new List<int>();
                            ocorrencia.ColunasPorCampo[campo.Key] = colunas;
                        }
                        colunas.Add(col);
                    }
                }

                logger?.LogInformation(
                    "[QUALIDADE][HEADER_DIAGNOSTICO] Aba={Aba} | Linha={Linha} | TextosEncontrados=[{Textos}]",
                    nomeAbaParaLog, row, string.Join(", ", ocorrencia.TextosComPosicao.Select(t => $"Col{t.Coluna}=\"{t.Texto}\"")));

                // Um campo que aparece mais de uma vez NA MESMA LINHA é ambíguo dentro dela — a linha
                // só é candidata se TODOS os campos mínimos aparecerem exatamente uma vez.
                var camposMinimosAusentes = camposMinimosParaCandidatura
                    .Where(campo => !ocorrencia.ColunasPorCampo.TryGetValue(campo, out var colunas) || colunas.Count != 1)
                    .ToList();

                bool satisfazMinimos = camposMinimosAusentes.Count == 0;
                int pontuacao = ocorrencia.ColunasPorCampo.Count(kv => kv.Value.Count == 1);

                logger?.LogInformation(
                    "[QUALIDADE][HEADER_SCAN] Aba={Aba} | Linha={Linha} | CamposReconhecidos={Pontuacao} | CamposMinimosAusentes=[{Ausentes}] | Candidata={Candidata}",
                    nomeAbaParaLog, row, pontuacao,
                    string.Join(", ", camposMinimosAusentes.Select(c => aliasesPorCampo[c].First())), satisfazMinimos);

                if (satisfazMinimos)
                {
                    candidatas.Add((row, ocorrencia));
                }
            }

            if (candidatas.Count == 0)
            {
                throw new QualidadeLayoutException(
                    CodigoNaoEncontrado,
                    $"[{nomeAbaParaLog}] Nenhuma linha entre {PrimeiraLinhaVarredura} e {UltimaLinhaVarredura} contém todos os cabeçalhos mínimos exigidos: " +
                    $"[{string.Join(", ", camposMinimosParaCandidatura.Select(c => aliasesPorCampo[c].First()))}].");
            }

            if (candidatas.Count > 1)
            {
                throw new QualidadeLayoutException(
                    CodigoAmbiguo,
                    $"[{nomeAbaParaLog}] Mais de uma linha candidata a cabeçalho encontrada nas linhas: " +
                    $"[{string.Join(", ", candidatas.Select(c => c.Linha))}]. Corrija o arquivo para que apenas uma linha contenha os cabeçalhos.");
            }

            var (linhaEscolhida, ocorrenciaEscolhida) = candidatas[0];

            // Mapa final: só campos com EXATAMENTE uma ocorrência na linha escolhida entram no mapa —
            // campos com múltiplas ocorrências na mesma linha ficam de fora (não são inventados).
            var mapa = ocorrenciaEscolhida.ColunasPorCampo
                .Where(kv => kv.Value.Count == 1)
                .ToDictionary(kv => kv.Key, kv => kv.Value[0], StringComparer.OrdinalIgnoreCase);

            logger?.LogInformation(
                "[QUALIDADE][HEADER_SELECIONADO] Aba={Aba} | HeaderRow={HeaderRow} | CamposMapeados=[{Mapeados}]",
                nomeAbaParaLog, linhaEscolhida, string.Join(", ", mapa.Keys));

            return new QualidadeCabecalhoResolvido
            {
                HeaderRow = linhaEscolhida,
                Mapa = mapa,
                CabecalhosEncontrados = ocorrenciaEscolhida.TextosEncontrados
            };
        }
    }
}
