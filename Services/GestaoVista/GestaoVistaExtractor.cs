using System;
using System.IO;
using APS.WebApp.Models;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Orquestra a leitura PURA (sem banco) do workbook da LDP para a Gestão à Vista: enumera TODAS as
    /// worksheets (Visible/Hidden/VeryHidden — GestaoVistaWorksheetLocator), localiza Timesheet/Custo
    /// Real/Custo profissionais por nome e extrai cada uma com seu parser dedicado. Falha em UMA aba
    /// opcional nunca derruba as demais nem lança exceção — cada parser só registra Warnings no
    /// diagnóstico daquela aba (seção 6 do modelo de dados: "falha em aba opcional nunca derruba o
    /// processo"). Só um Stream ilegível (workbook corrompido) vira Erro crítico.
    /// </summary>
    public sealed class GestaoVistaExtractor : IGestaoVistaExtractor
    {
        private readonly ILogger<GestaoVistaExtractor> _logger;

        public GestaoVistaExtractor(ILogger<GestaoVistaExtractor> logger)
        {
            _logger = logger;
        }

        public GestaoVistaExtracaoResultado Processar(Stream workbook, string projectIdAcc, string nomeArquivo)
        {
            var resultado = new GestaoVistaExtracaoResultado
            {
                ProjectIdAcc = projectIdAcc,
                NomeArquivo = nomeArquivo,
            };

            if (workbook == null)
            {
                resultado.Erros.Add("[GESTÃO À VISTA] Stream do workbook nulo.");
                return resultado;
            }

            ExcelPackage package;
            try
            {
                package = new ExcelPackage(workbook);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GESTÃO À VISTA] Falha ao abrir o workbook da LDP — extração abortada.");
                resultado.Erros.Add($"[GESTÃO À VISTA] Falha ao abrir o workbook: {ex.Message}");
                return resultado;
            }

            using (package)
            {
                var (diagnosticos, localizadas) = GestaoVistaWorksheetLocator.EnumerarEEncontrar(package);
                resultado.Worksheets = diagnosticos;

                _logger.LogInformation(
                    "[GESTÃO À VISTA] Workbook '{Arquivo}' | {Total} worksheet(s) enumeradas | Timesheet={Timesheet} | CustoReal={CustoReal} | CustoProfissionais={CustoProfissionais}",
                    nomeArquivo, diagnosticos.Count,
                    localizadas.ContainsKey("Timesheet"), localizadas.ContainsKey("CustoReal"), localizadas.ContainsKey("CustoProfissionais"));

                if (localizadas.TryGetValue("CustoReal", out var wsCustoReal))
                {
                    var diagCustoReal = diagnosticos.Find(d => d.NomeAba == wsCustoReal.Name);
                    GestaoVistaCustoRealParser.Processar(wsCustoReal, _logger, diagCustoReal, resultado.CustoReal);
                    diagCustoReal.Encontrada = diagCustoReal.LinhaCabecalho.HasValue;
                }
                else
                {
                    resultado.Avisos.Add("[GESTÃO À VISTA] Aba 'Custo Real' não encontrada neste workbook — fase Custo Real será pulada.");
                }

                if (localizadas.TryGetValue("CustoProfissionais", out var wsCustoPrevisto))
                {
                    var diagCustoPrevisto = diagnosticos.Find(d => d.NomeAba == wsCustoPrevisto.Name);
                    GestaoVistaCustoPrevistoParser.Processar(wsCustoPrevisto, _logger, diagCustoPrevisto, resultado.CustoPrevisto);
                    diagCustoPrevisto.Encontrada = diagCustoPrevisto.LinhaCabecalho.HasValue;
                }
                else
                {
                    resultado.Avisos.Add("[GESTÃO À VISTA] Aba 'Custo profissionais' não encontrada neste workbook — fase Custo Previsto será pulada.");
                }

                if (localizadas.TryGetValue("Timesheet", out var wsTimesheet))
                {
                    var diagTimesheet = diagnosticos.Find(d => d.NomeAba == wsTimesheet.Name);
                    GestaoVistaTimesheetParser.Processar(wsTimesheet, _logger, diagTimesheet, resultado.Timesheet);
                    diagTimesheet.Encontrada = diagTimesheet.LinhaCabecalho.HasValue;
                }
                else
                {
                    resultado.Avisos.Add("[GESTÃO À VISTA] Aba 'Timesheet' não encontrada neste workbook — fase Timesheet será pulada.");
                }

                foreach (var diag in diagnosticos)
                {
                    resultado.Avisos.AddRange(diag.Warnings);
                }
            }

            return resultado;
        }
    }
}
