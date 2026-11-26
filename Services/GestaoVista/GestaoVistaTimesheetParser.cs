using System.Collections.Generic;
using APS.WebApp.Models;
using APS.WebApp.Services.LdpTipo2;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Extrai a aba "Timesheet" — cabeçalho dinâmico (linha 1 nesta LDP de referência, nunca fixa).
    /// Lê a célula CUSTO como CustoInformadoNaPlanilha (fonte prioritária do CustoCalculado da Etapa 03 —
    /// ver Etapa03CustoRules); fórmulas sem valor em cache são avaliadas em memória
    /// (GestaoVistaFormulaCache). A coluna Data/Date e a coluna CUSTO
    /// são opcionais: esta LDP legada tem CUSTO mas nem toda LDP terá; quando ausentes, os campos ficam
    /// null (nunca inventados) — ver AUDITORIA_FORGESYNC_NA/MAPA_DADOS_LDP_FINANCEIRO.md seção 1.
    /// </summary>
    public static class GestaoVistaTimesheetParser
    {
        public static void Processar(ExcelWorksheet ws, ILogger logger, GestaoVistaWorksheetDiagnostico diagnostico, List<GestaoVistaTimesheetLancamentoExtraido> destino)
        {
            int? linhaCabecalho = GestaoVistaHeaderResolver.LocalizarLinhaCabecalho(ws, GestaoVistaCabecalhos.AncorasTimesheet);

            if (linhaCabecalho == null)
            {
                diagnostico.Encontrada = false;
                diagnostico.Warnings.Add("[GESTÃO À VISTA][TIMESHEET] Linha de cabeçalho não localizada (esperado NOME + TRABALHADAS na mesma linha).");
                return;
            }

            diagnostico.LinhaCabecalho = linhaCabecalho.Value;
            var mapa = GestaoVistaHeaderResolver.LerCabecalhosDaLinha(ws, linhaCabecalho.Value);

            int? colNome = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Nome);
            int? colPlanejadas = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Planejadas);
            int? colTrabalhadas = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Trabalhadas);
            int? colProjeto = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Projeto);
            int? colFuncao = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Funcao);
            int? colDisciplina = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Disciplina);
            int? colDocumentoCodigo = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.DocumentoCodigo);
            int? colDocumento = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Documento);
            int? colDescricao = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Descricao);
            int? colDoc1 = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Doc1);
            int? colDoc2 = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Doc2);
            int? colDoc3 = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Doc3);
            int? colDoc4 = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Doc4);
            int? colDoc5 = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Doc5);
            int? colData = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Data);
            int? colCustoInformado = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.CustoInformado);

            GestaoVistaCustoRealParser.RegistrarColunas(diagnostico, mapa,
                (GestaoVistaCabecalhos.Nome, colNome), (GestaoVistaCabecalhos.Planejadas, colPlanejadas), (GestaoVistaCabecalhos.Trabalhadas, colTrabalhadas),
                (GestaoVistaCabecalhos.Projeto, colProjeto), (GestaoVistaCabecalhos.Funcao, colFuncao), (GestaoVistaCabecalhos.Disciplina, colDisciplina),
                (GestaoVistaCabecalhos.DocumentoCodigo, colDocumentoCodigo), (GestaoVistaCabecalhos.Documento, colDocumento), (GestaoVistaCabecalhos.Descricao, colDescricao));

            if (colData.HasValue) diagnostico.ColunasEncontradas.Add(GestaoVistaCabecalhos.Data);
            else diagnostico.Warnings.Add("[GESTÃO À VISTA][TIMESHEET] Coluna Data/Date ausente nesta LDP — DataLancamento ficará NULL para todos os lançamentos (comportamento esperado em LDPs legadas).");

            if (colCustoInformado.HasValue) diagnostico.ColunasEncontradas.Add(GestaoVistaCabecalhos.CustoInformado);
            else diagnostico.Warnings.Add("[GESTÃO À VISTA][TIMESHEET] Coluna CUSTO ausente nesta LDP — CustoInformadoNaPlanilha ficará NULL para todos os lançamentos (evidência indisponível, nunca inventada).");

            if (colNome == null)
            {
                diagnostico.Warnings.Add("[GESTÃO À VISTA][TIMESHEET] Coluna NOME não resolvida de forma única — aba não processada.");
                return;
            }

            int linhaInicial = linhaCabecalho.Value + 1;
            int linhaFinal = ws.Dimension?.End.Row ?? (linhaInicial - 1);
            int linhasLidas = 0;

            if (colCustoInformado.HasValue)
            {
                int avaliadas = GestaoVistaFormulaCache.AvaliarFormulasSemCache(ws, colCustoInformado.Value, linhaInicial, linhaFinal);
                if (avaliadas > 0)
                    diagnostico.Warnings.Add($"[GESTÃO À VISTA][TIMESHEET] Coluna CUSTO: {avaliadas} fórmula(s) sem valor em cache avaliada(s) em memória (LDP gravada pela Etapa 01 sem cálculo).");
            }

            for (int linha = linhaInicial; linha <= linhaFinal; linha++)
            {
                linhasLidas++;

                string nome = LdpTipo2CellReader.LerTexto(ws.Cells[linha, colNome.Value]);
                string documentoCodigo = colDocumentoCodigo.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colDocumentoCodigo.Value]) : null;

                if (string.IsNullOrWhiteSpace(nome) && string.IsNullOrWhiteSpace(documentoCodigo)) continue; // linha vazia

                var registro = new GestaoVistaTimesheetLancamentoExtraido
                {
                    LinhaExcel = linha,
                    NomeProfissional = nome,
                    HorasPlanejadas = colPlanejadas.HasValue ? LdpTipo2CellReader.LerDecimal(ws.Cells[linha, colPlanejadas.Value], logger, linha, GestaoVistaCabecalhos.Planejadas) : null,
                    HorasTrabalhadas = colTrabalhadas.HasValue ? LdpTipo2CellReader.LerDecimal(ws.Cells[linha, colTrabalhadas.Value], logger, linha, GestaoVistaCabecalhos.Trabalhadas) : null,
                    Projeto = colProjeto.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colProjeto.Value]) : null,
                    Funcao = colFuncao.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colFuncao.Value]) : null,
                    Disciplina = colDisciplina.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colDisciplina.Value]) : null,
                    DocumentoCodigo = documentoCodigo,
                    Documento = colDocumento.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colDocumento.Value]) : null,
                    Descricao = colDescricao.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colDescricao.Value]) : null,
                    Doc1 = colDoc1.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colDoc1.Value]) : null,
                    Doc2 = colDoc2.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colDoc2.Value]) : null,
                    Doc3 = colDoc3.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colDoc3.Value]) : null,
                    Doc4 = colDoc4.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colDoc4.Value]) : null,
                    Doc5 = colDoc5.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colDoc5.Value]) : null,
                    DataLancamento = colData.HasValue ? LdpTipo2CellReader.LerData(ws.Cells[linha, colData.Value], logger, linha, GestaoVistaCabecalhos.Data) : null,
                    CustoInformadoNaPlanilha = colCustoInformado.HasValue
                        ? LdpTipo2CellReader.LerDecimal(ws.Cells[linha, colCustoInformado.Value], logger, linha, GestaoVistaCabecalhos.CustoInformado)
                        : null,
                };

                if (string.IsNullOrWhiteSpace(registro.NomeProfissional))
                {
                    diagnostico.Warnings.Add($"[GESTÃO À VISTA][TIMESHEET] Linha {linha}: sem NOME preenchido — lançamento ignorado.");
                    continue;
                }

                destino.Add(registro);
            }

            diagnostico.QuantidadeLinhas = linhasLidas;
        }
    }
}
