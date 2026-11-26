using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace APS.WebApp.Services.OperationalAudit.Reporting
{
    /// <summary>
    /// CSV tabular (seção 22) — uma linha por <see cref="OperationalFinding"/>, para importação direta em
    /// planilha/Power BI sem depender de um parser JSON. Não resume nada: os mesmos achados do TXT/JSON,
    /// em forma de tabela plana.
    /// </summary>
    public sealed class OperationalAuditCsvReportWriter : IOperationalAuditReportWriter
    {
        private static readonly string[] Cabecalho =
        {
            "ExecutionId", "DataHoraUtc", "ProjectIdAcc", "Projeto", "Etapa", "Subetapa", "RegraId", "CodigoProblema",
            "Titulo", "ErrorDomain", "Classificacao", "Severidade", "Status", "DocumentoNa", "DocumentoCliente",
            "NomeArquivo", "PastaEsperada", "PastaEncontrada", "AbaEsperada", "AbaEncontrada", "Linha", "Campo",
            "ValorEsperado", "ValorEncontrado", "CustomAttributeName", "CustomAttributeState", "IsCustomAttributeHidden",
            "BloqueiaEtapa", "AutomacaoPodeCorrigir", "PrimeiraOcorrencia", "UltimaOcorrencia", "QuantidadeOcorrencias",
            "DedupHash"
        };

        public async Task<string> EscreverAsync(OperationalAuditResult resultado, string outputDirectory, CancellationToken cancellationToken = default)
        {
            string caminho = ReportFileNaming.GarantirDiretorioECaminho(outputDirectory, resultado.ExecutionId, resultado.IniciadoEmUtc, "csv");

            var sb = new StringBuilder();
            sb.Append(string.Join(",", Cabecalho)).Append("\r\n");

            foreach (var f in resultado.Findings)
            {
                cancellationToken.ThrowIfCancellationRequested();

                sb.Append(Campo(f.ExecutionId.ToString())).Append(',');
                sb.Append(Campo(f.DataHoraUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',');
                sb.Append(Campo(f.ProjectIdAcc)).Append(',');
                sb.Append(Campo(f.Projeto)).Append(',');
                sb.Append(Campo(f.Etapa)).Append(',');
                sb.Append(Campo(f.Subetapa)).Append(',');
                sb.Append(Campo(f.RegraId)).Append(',');
                sb.Append(Campo(f.CodigoProblema)).Append(',');
                sb.Append(Campo(f.Titulo)).Append(',');
                sb.Append(Campo(f.ErrorDomain.ToString())).Append(',');
                sb.Append(Campo(f.Classificacao.ToString())).Append(',');
                sb.Append(Campo(f.Severidade.ToString())).Append(',');
                sb.Append(Campo(f.Status.ToString())).Append(',');
                sb.Append(Campo(f.DocumentoNa)).Append(',');
                sb.Append(Campo(f.DocumentoCliente)).Append(',');
                sb.Append(Campo(f.NomeArquivo)).Append(',');
                sb.Append(Campo(f.PastaEsperada)).Append(',');
                sb.Append(Campo(f.PastaEncontrada)).Append(',');
                sb.Append(Campo(f.AbaEsperada)).Append(',');
                sb.Append(Campo(f.AbaEncontrada)).Append(',');
                sb.Append(Campo(f.Linha?.ToString(CultureInfo.InvariantCulture))).Append(',');
                sb.Append(Campo(f.Campo)).Append(',');
                sb.Append(Campo(f.ValorEsperado)).Append(',');
                sb.Append(Campo(f.ValorEncontrado)).Append(',');
                sb.Append(Campo(f.CustomAttributeName)).Append(',');
                sb.Append(Campo(f.CustomAttributeState?.ToString())).Append(',');
                sb.Append(Campo(f.IsCustomAttributeHidden?.ToString())).Append(',');
                sb.Append(Campo(f.BloqueiaEtapa.ToString())).Append(',');
                sb.Append(Campo(f.AutomacaoPodeCorrigir.ToString())).Append(',');
                sb.Append(Campo(f.PrimeiraOcorrencia?.ToString("O", CultureInfo.InvariantCulture))).Append(',');
                sb.Append(Campo(f.UltimaOcorrencia?.ToString("O", CultureInfo.InvariantCulture))).Append(',');
                sb.Append(Campo(f.QuantidadeOcorrencias.ToString(CultureInfo.InvariantCulture))).Append(',');
                sb.Append(Campo(f.DedupHash));
                sb.Append("\r\n");
            }

            await File.WriteAllTextAsync(caminho, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
            return caminho;
        }

        /// <summary>Escapa um campo conforme RFC 4180: envolve em aspas quando contém vírgula, aspas ou quebra de linha; aspas internas viram aspas duplas.</summary>
        private static string Campo(string? valor)
        {
            if (string.IsNullOrEmpty(valor)) return string.Empty;
            bool precisaAspas = valor.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            string escapado = valor.Replace("\"", "\"\"");
            return precisaAspas ? $"\"{escapado}\"" : escapado;
        }
    }
}
