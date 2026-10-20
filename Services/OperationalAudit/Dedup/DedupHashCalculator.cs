using System;
using System.Security.Cryptography;
using System.Text;
using APS.WebApp.Services.OperationalAudit;

namespace APS.WebApp.Services.OperationalAudit.Dedup
{
    /// <summary>
    /// Calcula os dois hashes usados pela deduplicação (seção 9 da correção da Fase 2). Puro — nenhuma
    /// dependência de I/O, SQL ou relógio além do <see cref="DateTime"/> já resolvido pelo chamador.
    ///
    /// <see cref="CalcularDedupHash"/> identifica o PROBLEMA LÓGICO (nunca inclui timestamp — dois achados
    /// com os mesmos campos de identidade, em execuções diferentes, devem produzir o MESMO DedupHash, para
    /// que o motor de dedup reconheça "é o mesmo problema de novo").
    ///
    /// <see cref="CalcularOccurrenceHash"/> identifica UMA EVIDÊNCIA dentro de uma execução — inclui
    /// <c>ExecutionId</c> e a localização da evidência, então a MESMA execução nunca grava a mesma evidência
    /// duas vezes, mas execuções diferentes sempre geram uma nova ocorrência.
    /// </summary>
    public static class DedupHashCalculator
    {
        // Separador de campo improvável de aparecer em dado real (Unit Separator, US, 0x1F) — evita que a
        // concatenação de dois campos vizinhos produza a mesma string canônica por coincidência de bordas.
        // Construído via código de caractere (nunca embutido cru no fonte) para não depender da codificação
        // do arquivo em nenhuma ferramenta que o abra.
        private static readonly string Separador = new string((char)0x1F, 1);

        public static string CalcularDedupHash(OperationalFinding finding)
        {
            if (finding == null) throw new ArgumentNullException(nameof(finding));

            string representacao = ConstruirRepresentacaoCanonica(
                finding.ProjectIdAcc,
                finding.Etapa,
                finding.CodigoProblema,
                finding.DocumentoNa,
                finding.DocumentoCliente,
                finding.NomeArquivo,
                finding.PastaEncontrada,
                finding.AbaEncontrada,
                finding.Linha,
                finding.Campo,
                finding.CustomAttributeName,
                finding.MotivoNormalizado);

            return Sha256Hex(representacao);
        }

        public static string CalcularOccurrenceHash(OperationalFinding finding, Guid executionId)
        {
            if (finding == null) throw new ArgumentNullException(nameof(finding));
            if (string.IsNullOrEmpty(finding.DedupHash))
                throw new InvalidOperationException("DedupHash precisa ser calculado antes do OccurrenceHash.");

            string representacao = string.Join(
                Separador,
                finding.DedupHash,
                executionId.ToString("N"),
                Normalizar(finding.ArquivoLog),
                finding.LinhaLogInicial?.ToString() ?? string.Empty,
                finding.LinhaLogFinal?.ToString() ?? string.Empty);

            return Sha256Hex(representacao);
        }

        /// <summary>Exposto internamente só para teste direto da representação canônica (sem recalcular hash).</summary>
        internal static string ConstruirRepresentacaoCanonica(
            string? projectIdAcc,
            string? stageNumber,
            string? problemCode,
            string? documentNa,
            string? documentClient,
            string? fileName,
            string? foundFolder,
            string? foundWorksheet,
            int? excelRow,
            string? fieldName,
            string? customAttributeName,
            string? motivoNormalizado)
        {
            return string.Join(
                Separador,
                Normalizar(projectIdAcc),
                Normalizar(stageNumber),
                Normalizar(problemCode),
                Normalizar(documentNa),
                Normalizar(documentClient),
                Normalizar(fileName),
                Normalizar(foundFolder),
                Normalizar(foundWorksheet),
                excelRow?.ToString() ?? string.Empty,
                Normalizar(fieldName),
                Normalizar(customAttributeName),
                Normalizar(motivoNormalizado));
        }

        private static string Normalizar(string? valor) =>
            string.IsNullOrWhiteSpace(valor) ? string.Empty : valor.Trim().ToUpperInvariant();

        private static string Sha256Hex(string valor)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(valor));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
