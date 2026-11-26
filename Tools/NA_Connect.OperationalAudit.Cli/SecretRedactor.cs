using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;

namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>
    /// Checkpoint 4, seção 5: nunca imprime connection string completa, senha, client secret ou bearer
    /// token. Todo log/relatório de diagnóstico passa por aqui — nunca formata uma connection string "na
    /// mão" em outro lugar do CLI.
    /// </summary>
    public static class SecretRedactor
    {
        /// <summary>Só Server/Database/tipo de autenticação — nunca User ID, Password, nem nenhuma outra chave da connection string.</summary>
        public static string ResumirConnectionString(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return "(não configurada)";

            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                string autenticacao = !string.IsNullOrEmpty(builder.UserID) ? "SQL Login" : builder.IntegratedSecurity ? "Integrated Security" : "(não determinado)";
                return $"Servidor={builder.DataSource} | Banco={builder.InitialCatalog} | Autenticacao={autenticacao} | Encrypt={builder.Encrypt}";
            }
            catch
            {
                // Nunca ecoa a string original quando o parse falha — o valor bruto poderia conter credenciais.
                return "(connection string configurada — formato não reconhecido para resumo seguro)";
            }
        }

        /// <summary>Mascara qualquer valor de configuração cujo NOME pareça sensível — usado ao logar diagnóstico genérico de chaves.</summary>
        public static bool PareceChaveSensivel(string chaveConfiguracao)
        {
            string k = chaveConfiguracao.ToUpperInvariant();
            return k.Contains("PASSWORD") || k.Contains("SECRET") || k.Contains("TOKEN") ||
                   k.Contains("PWD") || k.Contains("CONNECTIONSTRING") || k.Contains("APIKEY") || k.Contains("API_KEY");
        }

        public static string Mascarar(string? valor)
        {
            if (string.IsNullOrEmpty(valor)) return "(vazio)";
            return "***REDACTED***";
        }

        /// <summary>Varre um texto livre (ex.: mensagem de exceção) e garante que nenhum valor sensível conhecido apareça — última linha de defesa antes de logar.</summary>
        public static string RedigirSeAparecer(string texto, IEnumerable<string?> valoresSensiveisConhecidos)
        {
            string resultado = texto;
            foreach (var valor in valoresSensiveisConhecidos.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                resultado = resultado.Replace(valor!, "***REDACTED***", StringComparison.Ordinal);
            }
            return resultado;
        }
    }
}
