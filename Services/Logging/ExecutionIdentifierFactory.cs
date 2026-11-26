using System;
using System.Linq;
using System.Security.Cryptography;

namespace APS.WebApp.Services.Logging
{
    /// <summary>
    /// Gera os identificadores estruturados de log (ExecutionId por lote, ProjectExecutionId por projeto,
    /// OperationId por operação relevante — ver seção 6 da manutenção técnica). Nunca gera um Guid
    /// completo nem um identificador por linha de log; um ExecutionId serve para o lote inteiro, um
    /// ProjectExecutionId para o processamento inteiro de um projeto, um OperationId para UMA chamada de
    /// operação importante (não por linha/registro dentro dela).
    /// </summary>
    public static class ExecutionIdentifierFactory
    {
        private static readonly char[] AlfabetoSufixo = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

        public static string GerarExecutionId(DateTime momento) =>
            $"{momento:yyyyMMdd-HHmmss}-{GerarSufixoAleatorio(4)}";

        public static string GerarProjectExecutionId(string executionId, string projectIdAcc, int sequencial) =>
            $"{executionId}-{LimparProjectId(projectIdAcc)}-{sequencial:00}";

        public static string GerarOperationId(string prefixo, int sequencial) =>
            $"{prefixo}-{sequencial:0000}";

        private static string LimparProjectId(string projectIdAcc)
        {
            if (string.IsNullOrWhiteSpace(projectIdAcc)) return "PROJETO";
            string semPrefixo = projectIdAcc.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
                ? projectIdAcc.Substring(2)
                : projectIdAcc;
            string apenasAlfanumerico = new string(semPrefixo.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            return string.IsNullOrEmpty(apenasAlfanumerico) ? "PROJETO" : apenasAlfanumerico;
        }

        private static string GerarSufixoAleatorio(int tamanho)
        {
            var buffer = new char[tamanho];
            for (int i = 0; i < tamanho; i++)
            {
                buffer[i] = AlfabetoSufixo[RandomNumberGenerator.GetInt32(AlfabetoSufixo.Length)];
            }
            return new string(buffer);
        }
    }
}
