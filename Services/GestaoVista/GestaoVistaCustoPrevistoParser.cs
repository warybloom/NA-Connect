using System.Collections.Generic;
using APS.WebApp.Models;
using APS.WebApp.Services.LdpTipo2;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Extrai o BLOCO TABULAR PRINCIPAL da aba "Custo profissionais" — cabeçalho dinâmico (linha 2 nesta
    /// LDP de referência, nunca fixa). GrupoFuncao (coluna B) não tem texto de cabeçalho: resolvida por
    /// posição estrutural (a coluna entre Função e Disciplina, só quando há exatamente 1 coluna de gap —
    /// nunca por índice absoluto). O bloco "MIX" (mesma aba, layout não tabular) é apenas DETECTADO — a
    /// varredura do bloco principal para antes dele; nunca extraído nesta etapa (ver
    /// AUDITORIA_FORGESYNC_NA/MAPA_DADOS_LDP_FINANCEIRO.md seção 3).
    /// </summary>
    public static class GestaoVistaCustoPrevistoParser
    {
        public static void Processar(ExcelWorksheet ws, ILogger logger, GestaoVistaWorksheetDiagnostico diagnostico, List<GestaoVistaCustoPrevistoExtraido> destino)
        {
            int? linhaCabecalho = GestaoVistaHeaderResolver.LocalizarLinhaCabecalho(ws, GestaoVistaCabecalhos.AncorasCustoPrevisto);

            if (linhaCabecalho == null)
            {
                diagnostico.Encontrada = false;
                diagnostico.Warnings.Add("[GESTÃO À VISTA][CUSTO PROFISSIONAIS] Linha de cabeçalho não localizada (esperado FUNÇÃO + DISCIPLINA + CUSTO/HORA na mesma linha).");
                return;
            }

            diagnostico.LinhaCabecalho = linhaCabecalho.Value;
            var mapa = GestaoVistaHeaderResolver.LerCabecalhosDaLinha(ws, linhaCabecalho.Value);

            int? colFuncao = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Funcao);
            int? colDisciplina = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Disciplina);
            int? colRecurso = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Recurso);
            int? colRemuneracao = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Remuneracao);
            int? colEncargos = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Encargos);
            int? colSoftwares = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Softwares);
            int? colComputadores = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Computadores);

            int? colGrupoFuncao = null;
            if (colFuncao.HasValue && colDisciplina.HasValue && colDisciplina.Value == colFuncao.Value + 2)
            {
                colGrupoFuncao = colFuncao.Value + 1;
            }
            else
            {
                diagnostico.Warnings.Add("[GESTÃO À VISTA][CUSTO PROFISSIONAIS] Coluna de agrupamento (GrupoFuncao, sem cabeçalho textual) não pôde ser resolvida estruturalmente — GrupoFuncao ficará vazio.");
            }

            GestaoVistaCustoRealParser.RegistrarColunas(diagnostico, mapa,
                (GestaoVistaCabecalhos.Funcao, colFuncao), (GestaoVistaCabecalhos.Disciplina, colDisciplina), (GestaoVistaCabecalhos.Recurso, colRecurso),
                (GestaoVistaCabecalhos.Remuneracao, colRemuneracao), (GestaoVistaCabecalhos.Encargos, colEncargos),
                (GestaoVistaCabecalhos.Softwares, colSoftwares), (GestaoVistaCabecalhos.Computadores, colComputadores));

            if (colFuncao == null)
            {
                diagnostico.Warnings.Add("[GESTÃO À VISTA][CUSTO PROFISSIONAIS] Coluna FUNÇÃO não resolvida de forma única — aba não processada.");
                return;
            }

            int linhaInicial = linhaCabecalho.Value + 1;
            int linhaFinal = ws.Dimension?.End.Row ?? (linhaInicial - 1);
            int linhasLidas = 0;
            bool mixDetectado = false;

            for (int linha = linhaInicial; linha <= linhaFinal; linha++)
            {
                string funcao = LdpTipo2CellReader.LerTexto(ws.Cells[linha, colFuncao.Value]);
                string disciplinaBruta = colDisciplina.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colDisciplina.Value]) : null;

                bool ehLinhaMix = (funcao != null && funcao.Trim().Equals("MIX", System.StringComparison.OrdinalIgnoreCase))
                    || (disciplinaBruta != null && disciplinaBruta.Trim().Equals("MIX", System.StringComparison.OrdinalIgnoreCase));

                if (ehLinhaMix)
                {
                    mixDetectado = true;
                    diagnostico.Warnings.Add($"[GESTÃO À VISTA][CUSTO PROFISSIONAIS] Bloco MIX detectado a partir da linha {linha} — layout não tabular padronizado, não importado nesta etapa (candidato futuro).");
                    break; // bloco MIX começa aqui — para a varredura do bloco tabular principal.
                }

                linhasLidas++;
                if (string.IsNullOrWhiteSpace(funcao)) continue; // linha vazia — nunca conta como dado

                var registro = new GestaoVistaCustoPrevistoExtraido
                {
                    LinhaExcel = linha,
                    Funcao = funcao,
                    GrupoFuncao = colGrupoFuncao.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colGrupoFuncao.Value]) : null,
                    Disciplina = disciplinaBruta,
                    Recurso = colRecurso.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colRecurso.Value]) : null,
                    Remuneracao = colRemuneracao.HasValue ? LdpTipo2CellReader.LerDecimal(ws.Cells[linha, colRemuneracao.Value], logger, linha, GestaoVistaCabecalhos.Remuneracao) : null,
                    Encargos = colEncargos.HasValue ? LdpTipo2CellReader.LerDecimal(ws.Cells[linha, colEncargos.Value], logger, linha, GestaoVistaCabecalhos.Encargos) : null,
                    Softwares = colSoftwares.HasValue ? LdpTipo2CellReader.LerDecimal(ws.Cells[linha, colSoftwares.Value], logger, linha, GestaoVistaCabecalhos.Softwares) : null,
                    Computadores = colComputadores.HasValue ? LdpTipo2CellReader.LerDecimal(ws.Cells[linha, colComputadores.Value], logger, linha, GestaoVistaCabecalhos.Computadores) : null,
                };

                GestaoVistaCustoCalculo.CalcularTotalECustoHora(registro.Remuneracao, registro.Encargos, registro.Softwares, registro.Computadores,
                    out decimal? total, out decimal? custoHora);
                registro.CustoTotalMensal = total;
                registro.CustoHora = custoHora;

                if (custoHora == null)
                {
                    diagnostico.Warnings.Add($"[GESTÃO À VISTA][CUSTO PROFISSIONAIS] Linha {linha} ({funcao}): nenhum componente de custo preenchido — linha ignorada.");
                }

                destino.Add(registro);
            }

            diagnostico.QuantidadeLinhas = linhasLidas;
            if (mixDetectado) diagnostico.ColunasAusentes.Add("(bloco MIX — não extraído nesta etapa)");
        }
    }
}
