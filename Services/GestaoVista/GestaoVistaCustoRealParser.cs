using System.Collections.Generic;
using System.Linq;
using APS.WebApp.Models;
using APS.WebApp.Services.LdpTipo2;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Extrai a aba "Custo Real" — colunas função/disciplina/nome/Remuneração/Encargos/Softwares/
    /// Computadores/Custo-hora (cabeçalho dinâmico, linha 1 nesta LDP de referência, mas nunca fixa).
    /// Reaproveita LdpTipo2CellReader (texto/decimal) — utilitário já existente e testado, nunca
    /// duplicado.
    ///
    /// Correção 2026-09-22 (BUG 3 — Custo Real do Timesheet perdendo profissionais inteiros): CUSTO HORA
    /// é uma das âncoras que já localizam a linha de cabeçalho desta aba (AncorasCustoReal), mas até
    /// 2026-09-21 a própria coluna nunca era lida por linha — Custo/hora era SEMPRE recalculado por
    /// (Remuneração+Encargos+Softwares+Computadores)/176. Auditoria real BML011.013 provou que
    /// profissionais cujo custo/hora é digitado DIRETO nesta coluna (contratados/especialistas sem
    /// composição salarial formal — Antonio Clareti R$250, Everton Eustaquio R$100, Tim Chowdhury R$130)
    /// têm os 4 componentes em branco: a composição calculava 0/null e o valor real desaparecia. Regra
    /// oficial agora: o valor DIRETO da coluna CUSTO HORA tem prioridade; a composição continua sendo o
    /// fallback (comportamento anterior preservado) quando a coluna estiver ausente/vazia na linha.
    /// </summary>
    public static class GestaoVistaCustoRealParser
    {
        public static void Processar(ExcelWorksheet ws, ILogger logger, GestaoVistaWorksheetDiagnostico diagnostico, List<GestaoVistaCustoRealExtraido> destino)
        {
            int? linhaCabecalho = GestaoVistaHeaderResolver.LocalizarLinhaCabecalho(ws, GestaoVistaCabecalhos.AncorasCustoReal);

            if (linhaCabecalho == null)
            {
                diagnostico.Encontrada = false;
                diagnostico.Warnings.Add("[GESTÃO À VISTA][CUSTO REAL] Linha de cabeçalho não localizada (esperado NOME + CUSTO/HORA na mesma linha).");
                return;
            }

            diagnostico.LinhaCabecalho = linhaCabecalho.Value;
            var mapa = GestaoVistaHeaderResolver.LerCabecalhosDaLinha(ws, linhaCabecalho.Value);

            int? colFuncao = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Funcao);
            int? colDisciplina = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Disciplina);
            int? colNome = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Nome);
            int? colRemuneracao = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Remuneracao);
            int? colEncargos = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Encargos);
            int? colSoftwares = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Softwares);
            int? colComputadores = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.Computadores);
            int? colCustoHora = GestaoVistaHeaderResolver.ObterColunaUnica(mapa, GestaoVistaCabecalhos.CustoHora);

            RegistrarColunas(diagnostico, mapa,
                (GestaoVistaCabecalhos.Nome, colNome), (GestaoVistaCabecalhos.Funcao, colFuncao), (GestaoVistaCabecalhos.Disciplina, colDisciplina),
                (GestaoVistaCabecalhos.Remuneracao, colRemuneracao), (GestaoVistaCabecalhos.Encargos, colEncargos),
                (GestaoVistaCabecalhos.Softwares, colSoftwares), (GestaoVistaCabecalhos.Computadores, colComputadores),
                (GestaoVistaCabecalhos.CustoHora, colCustoHora));

            if (colNome == null)
            {
                diagnostico.Warnings.Add("[GESTÃO À VISTA][CUSTO REAL] Coluna NOME não resolvida de forma única — aba não processada.");
                return;
            }

            int linhaInicial = linhaCabecalho.Value + 1;
            int linhaFinal = ws.Dimension?.End.Row ?? (linhaInicial - 1);
            int linhasLidas = 0;

            for (int linha = linhaInicial; linha <= linhaFinal; linha++)
            {
                linhasLidas++;

                string nome = LdpTipo2CellReader.LerTexto(ws.Cells[linha, colNome.Value]);
                if (string.IsNullOrWhiteSpace(nome)) continue; // linha vazia — nunca conta como dado

                var registro = new GestaoVistaCustoRealExtraido
                {
                    LinhaExcel = linha,
                    NomeProfissional = nome,
                    Funcao = colFuncao.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colFuncao.Value]) : null,
                    Disciplina = colDisciplina.HasValue ? LdpTipo2CellReader.LerTexto(ws.Cells[linha, colDisciplina.Value]) : null,
                    Remuneracao = colRemuneracao.HasValue ? LdpTipo2CellReader.LerDecimal(ws.Cells[linha, colRemuneracao.Value], logger, linha, GestaoVistaCabecalhos.Remuneracao) : null,
                    Encargos = colEncargos.HasValue ? LdpTipo2CellReader.LerDecimal(ws.Cells[linha, colEncargos.Value], logger, linha, GestaoVistaCabecalhos.Encargos) : null,
                    Softwares = colSoftwares.HasValue ? LdpTipo2CellReader.LerDecimal(ws.Cells[linha, colSoftwares.Value], logger, linha, GestaoVistaCabecalhos.Softwares) : null,
                    Computadores = colComputadores.HasValue ? LdpTipo2CellReader.LerDecimal(ws.Cells[linha, colComputadores.Value], logger, linha, GestaoVistaCabecalhos.Computadores) : null,
                };

                GestaoVistaCustoCalculo.CalcularTotalECustoHora(registro.Remuneracao, registro.Encargos, registro.Softwares, registro.Computadores,
                    out decimal? total, out decimal? custoHoraComposicao);
                registro.CustoTotalMensal = total;

                decimal? custoHoraInformado = colCustoHora.HasValue
                    ? LdpTipo2CellReader.LerDecimal(ws.Cells[linha, colCustoHora.Value], logger, linha, GestaoVistaCabecalhos.CustoHora)
                    : null;
                registro.CustoHora = custoHoraInformado ?? custoHoraComposicao;

                if (registro.CustoHora == null)
                {
                    diagnostico.Warnings.Add($"[GESTÃO À VISTA][CUSTO REAL] Linha {linha} ({nome}): nem custo/hora direto nem componentes de composição preenchidos — linha ignorada.");
                }

                destino.Add(registro);
            }

            diagnostico.QuantidadeLinhas = linhasLidas;
        }

        internal static void RegistrarColunas(GestaoVistaWorksheetDiagnostico diagnostico, Dictionary<string, List<int>> mapa, params (string Cabecalho, int? Coluna)[] campos)
        {
            foreach (var (cabecalho, coluna) in campos)
            {
                if (coluna.HasValue) diagnostico.ColunasEncontradas.Add(cabecalho);
                else diagnostico.ColunasAusentes.Add(cabecalho);
            }
        }
    }
}
