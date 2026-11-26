using System;
using Microsoft.Extensions.Configuration;

namespace APS.WebApp.Services.OperationalAudit.Persistence
{
    /// <summary>
    /// Resolve o NOME de uma connection string (seção 4: <c>AuditSourceConnection</c> /
    /// <c>AuditRepositoryConnection</c>) para o valor real em <c>ConnectionStrings</c>. Nunca cai de volta
    /// para <c>LocalConnection</c> ou qualquer outra credencial operacional — se a entrada nomeada não
    /// existir, falha explicitamente, porque usar uma credencial de escrita "só desta vez" é exatamente o
    /// risco que as duas conexões separadas existem para eliminar.
    /// </summary>
    internal static class AuditConnectionStringResolver
    {
        public static string Resolver(IConfiguration configuration, string connectionStringName, string finalidade)
        {
            string? cs = configuration.GetConnectionString(connectionStringName);
            if (string.IsNullOrWhiteSpace(cs))
            {
                throw new InvalidOperationException(
                    $"[OperationalAudit] ConnectionStrings:{connectionStringName} não está configurada (necessária para {finalidade}). " +
                    "Nunca use ConnectionStrings:LocalConnection (ou qualquer outra credencial operacional) como substituto — " +
                    "configure uma credencial dedicada antes de habilitar OperationalAudit:Enabled.");
            }
            return cs;
        }
    }
}
