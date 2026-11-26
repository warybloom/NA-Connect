using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace NA_Connect.OperationalAudit.Cli
{
    public sealed class ConfigCheckItem
    {
        public string Chave { get; init; } = string.Empty;
        public bool Presente { get; init; }
        public bool Obrigatoria { get; init; }
        /// <summary>Só um RESUMO seguro (ex.: via <see cref="SecretRedactor"/>) — nunca o valor bruto de uma chave sensível.</summary>
        public string? ResumoSeguro { get; init; }
    }

    public sealed class ConfigValidationResult
    {
        public IReadOnlyList<ConfigCheckItem> Itens { get; init; } = System.Array.Empty<ConfigCheckItem>();
        public bool Sucesso => Itens.Count > 0 && System.Linq.Enumerable.All(Itens, i => !i.Obrigatoria || i.Presente);
    }

    /// <summary>
    /// Checkpoint 4, seção 7/18: <c>--validate-config</c> — NUNCA abre conexão SQL, NUNCA faz GET ao ACC,
    /// NUNCA chama <see cref="APS.WebApp.Services.OperationalAudit.Persistence.AuditConnectionStringResolver"/>
    /// (que é <c>internal</c> ao assembly principal, e mesmo se fosse acessível, resolver já é uma ação a
    /// mais do que "existe semanticamente"). Só olha <see cref="IConfiguration"/> em memória.
    /// </summary>
    public static class ConfigSemanticValidator
    {
        public static ConfigValidationResult Validar(IConfiguration configuration)
        {
            var itens = new List<ConfigCheckItem>
            {
                Verificar(configuration, "ConnectionStrings:AuditSourceConnection", obrigatoria: true, ehConnectionString: true),
                Verificar(configuration, "ConnectionStrings:AuditRepositoryConnection", obrigatoria: true, ehConnectionString: true),
                Verificar(configuration, "APS:ClientId", obrigatoria: true),
                Verificar(configuration, "APS:ClientSecret", obrigatoria: true),
                Verificar(configuration, "APS:Scope", obrigatoria: true),
                Verificar(configuration, "OperationalAudit:OutputDirectory", obrigatoria: false)
            };
            return new ConfigValidationResult { Itens = itens };
        }

        private static ConfigCheckItem Verificar(IConfiguration configuration, string chave, bool obrigatoria, bool ehConnectionString = false)
        {
            string? valor = configuration[chave];
            bool presente = !string.IsNullOrWhiteSpace(valor);

            string? resumo = !presente
                ? null
                : ehConnectionString
                    ? SecretRedactor.ResumirConnectionString(valor)
                    : SecretRedactor.PareceChaveSensivel(chave)
                        ? SecretRedactor.Mascarar(valor)
                        : valor;

            return new ConfigCheckItem { Chave = chave, Presente = presente, Obrigatoria = obrigatoria, ResumoSeguro = resumo };
        }
    }
}
