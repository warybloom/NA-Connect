using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using APS.WebApp.Models;
using APS.WebApp.Services.Qualidade;

namespace APS.WebApp.Services.Planning
{
    /// <summary>Preflight por assinatura nas primeiras dez linhas de todas as abas. Valida registros antes do SQL.</summary>
    public sealed class PlanningPreflightService : IPlanningPreflightService
    {
        public const string CodigoAbaNaoSuportada = "ABA_INCORRETA";
        public const string CodigoColunaObrigatoriaAusenteNoExcel = "PLANNING_COLUNA_OBRIGATORIA_AUSENTE_NO_EXCEL";

        private const string ClassificacaoVazia = "VAZIA";
        private const string ClassificacaoPlaceholder = "PLACEHOLDER";
        private const string ClassificacaoFormulaSemResultado = "FORMULA_SEM_RESULTADO";
        private const string ClassificacaoSemIdentificador = "SEM_IDENTIFICADOR";

        private readonly ILogger<PlanningPreflightService> _logger;

        public PlanningPreflightService(ILogger<PlanningPreflightService> logger)
        {
            _logger = logger;
        }

        public ResultadoPreflightPlanejamento ExecutarPreflight(
            ExcelPackage package,
            PlanningSheetDefinition definicao,
            string projectId,
            string pastaEncontrada,
            string arquivoSelecionado)
        {
            var resultado = new ResultadoPreflightPlanejamento
            {
                ProjectIdAcc = projectId,
                PastaEncontrada = pastaEncontrada,
                ArquivoSelecionado = arquivoSelecionado
            };

            _logger.LogInformation(
                "[PLANNING_SYNC][PREFLIGHT][INICIO] Processo={Processo} | Project_ID_ACC={ProjectIdAcc} | Pasta={Pasta} | Arquivo={Arquivo}",
                definicao.ProcessName, projectId, pastaEncontrada, arquivoSelecionado);

            ExcelWorksheet ws;
            PlanningCabecalhoResolvido cabecalho;
            try
            {
                var selected = PlanningHeaderScan.Select(package.Workbook.Worksheets, definicao, _logger, arquivoSelecionado);
                ws = selected.Sheet;
                cabecalho = selected.Header;
                resultado.AbaSelecionada = ws.Name;
            }
            catch (PlanningLayoutException ex)
            {
                resultado.Sucesso = false;
                resultado.CodigoResultado = ex.CodigoResultado;
                resultado.Mensagem = ex.Message;
                resultado.TipoRegistro = ex.CodigoResultado;
                resultado.StatusImportacao = ex.CodigoResultado;
                resultado.DiagnosticoImportacao = ex.Message;
                new PlanningValidationEvent(LogLevel.Warning, ex.CodigoResultado, definicao.ProcessName, arquivoSelecionado, Message: "Projeto=" + projectId + "; " + ex.Message).Write(_logger);
                return resultado;
            }

            resultado.HeaderRow = cabecalho.HeaderRow;
            resultado.ColunasEncontradas = cabecalho.Mapa.Keys.OrderBy(k => k).ToList();
            resultado.ColunasAusentes = definicao.Columns.Select(c => c.LogicalName)
                .Except(cabecalho.Mapa.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k)
                .ToList();

            _logger.LogInformation(
                "[PLANNING_SYNC][MAPA_COLUNAS] Processo={Processo} | Aba={Aba} | HeaderRow={HeaderRow} | ColunasEncontradas=[{Encontradas}] | ColunasAusentes=[{Ausentes}]",
                definicao.ProcessName, ws.Name, cabecalho.HeaderRow, string.Join(", ", resultado.ColunasEncontradas), string.Join(", ", resultado.ColunasAusentes));

            MontarTabela(ws, definicao, cabecalho, projectId, resultado);

            resultado.Sucesso = true;
            resultado.StatusImportacao = resultado.RowsWithErrors > 0 ? "COM_ALERTAS" : "OK";
            resultado.CodigoResultado = resultado.StatusImportacao;
            resultado.Mensagem =
                $"{resultado.LinhasValidas} linha(s) válida(s) de {resultado.LinhasAnalisadas} analisada(s) ({resultado.LinhasIgnoradas.Count} ignorada(s)).";

            _logger.LogInformation(
                "[PLANNING_SYNC][PREFLIGHT] Processo={Processo} | Aba={Aba} | LinhasFisicas={LinhasFisicas} | LinhasAnalisadas={LinhasAnalisadas} | " +
                "LinhasValidas={LinhasValidas} | LinhasIgnoradas={LinhasIgnoradas}",
                definicao.ProcessName, ws.Name, resultado.LinhasFisicas, resultado.LinhasAnalisadas, resultado.LinhasValidas, resultado.LinhasIgnoradas.Count);

            return resultado;
        }

        private void MontarTabela(ExcelWorksheet ws, PlanningSheetDefinition definicao, PlanningCabecalhoResolvido cabecalho,
            string projectId, ResultadoPreflightPlanejamento resultado)
        {
            var dt = new DataTable();
            dt.Columns.Add("Project_ID_ACC", typeof(string));
            dt.Columns.Add("Data_Sincronizacao", typeof(DateTime));
            foreach (var field in definicao.Columns)
                dt.Columns.Add(field.SqlColumnName, field.StoreAsText ? typeof(string) : field.DataType switch
                { PlanningColumnDataType.Data => typeof(DateTime), PlanningColumnDataType.Inteiro => typeof(int), PlanningColumnDataType.Decimal => typeof(decimal), _ => typeof(string) });
            var present = definicao.Columns.Where(f => cabecalho.Mapa.ContainsKey(f.LogicalName)).ToArray();
            var syncTime = DateTime.Now;
            int start = cabecalho.HeaderEndRow + 1;
            resultado.LinhasFisicas = Math.Max(0, (ws.Dimension?.End.Row ?? 0) - start + 1);
            for (int row = start; row <= (ws.Dimension?.End.Row ?? 0); row++)
            {
                resultado.LinhasAnalisadas++;
                // Mesclas de dados (HOLD: duas linhas; Desvio: três) só são lidas na âncora.
                if (present.All(f => string.IsNullOrWhiteSpace(ws.Cells[row, cabecalho.Mapa[f.LogicalName]].Text))) continue;
                // Rodapé placeholder comprovado no CAP oficial (linha 5348), preservando a regra histórica.
                if (present.All(f => string.IsNullOrWhiteSpace(ws.Cells[row, cabecalho.Mapa[f.LogicalName]].Text) || ws.Cells[row, cabecalho.Mapa[f.LogicalName]].Text.Trim() == "-"))
                { resultado.LinhasIgnoradas.Add($"Linha {row}: PLACEHOLDER"); continue; }
                var record = dt.NewRow(); record["Project_ID_ACC"] = projectId; record["Data_Sincronizacao"] = syncTime;
                string identity = string.Join(" | ", present.Where(f => f.IsRowIdentifier).Select(f => f.LogicalName + "=" + ws.Cells[row, cabecalho.Mapa[f.LogicalName]].Text));
                var diagnostics = new List<string>();
                foreach (var missing in definicao.Columns.Where(f => !cabecalho.Mapa.ContainsKey(f.LogicalName)))
                    diagnostics.Add(missing.AcceptedHeaders[0] + ": COLUNA NÃO EXISTE NO EXCEL.");
                foreach (var field in present)
                {
                    int col = cabecalho.Mapa[field.LogicalName];
                    var cell = ws.Cells[row, col];
                    string raw = cell.Text;
                    string header = cabecalho.OriginalHeaders[col];
                    bool empty = cell.Value == null || string.IsNullOrWhiteSpace(raw);
                    string expected = field.DataType switch { PlanningColumnDataType.Data => "data válida", PlanningColumnDataType.Inteiro => "número inteiro", PlanningColumnDataType.Decimal => "número decimal", _ => "texto" };
                    if (empty)
                    {
                        diagnostics.Add(header + ": CÉLULA VAZIA NO EXCEL.");
                        new PlanningValidationEvent(LogLevel.Warning, "VALOR_OBRIGATORIO_VAZIO", definicao.ProcessName, resultado.ArquivoSelecionado,
                            ws.Name, row, col, header, raw, "valor preenchido", "Projeto=" + projectId + "; Ação=NULL gravado; restante da linha preservado").Write(_logger);
                        continue; // DataRow permanece DBNull.
                    }
                    try
                    {
                        if (cell.Value is ExcelErrorValue) throw new FormatException("Erro de fórmula Excel: " + raw);
                        object value = raw.Trim();
                        if (field.DataType == PlanningColumnDataType.Data)
                        {
                            if (!QualidadeParsers.TentarParseData(cell, out var date, out var error)) throw new FormatException(error);
                            value = field.StoreAsText ? date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : date;
                        }
                        else if (field.DataType == PlanningColumnDataType.Inteiro)
                        {
                            if (!QualidadeParsers.TentarParseInteiro(raw.Trim(), out var number, out var error)) throw new FormatException(error);
                            value = field.StoreAsText ? number?.ToString(System.Globalization.CultureInfo.InvariantCulture) : number;
                        }
                        else if (field.DataType == PlanningColumnDataType.Decimal)
                        {
                            var number = WeeklyScheduleContract.Number(cell.Value);
                            value = field.StoreAsText ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : number;
                        }
                        record[field.SqlColumnName] = value ?? DBNull.Value;
                    }
                    catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
                    {
                        diagnostics.Add($"{header}: VALOR INVÁLIDO. Recebido='{raw}'; esperado={expected}.");
                        new PlanningValidationEvent(LogLevel.Error, "PREENCHIMENTO_INCORRETO", definicao.ProcessName, resultado.ArquivoSelecionado,
                            ws.Name, row, col, header, raw, expected, "Projeto=" + projectId + "; Ação=NULL gravado; restante da linha preservado. " + ex.Message + " Registro: " + identity).Write(_logger);
                    }
                }
                if (diagnostics.Count > 0) resultado.RowsWithErrors++;
                resultado.DiagnosticosLinhas[row] = diagnostics;
                // Diagnósticos ficam no domínio até as quatro tabelas receberem suporte SQL explícito.
                dt.Rows.Add(record);
            }
            resultado.Tabela = dt;
            resultado.LinhasValidas = dt.Rows.Count;
        }
    }
}
