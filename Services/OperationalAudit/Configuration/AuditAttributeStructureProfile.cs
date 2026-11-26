using System;

namespace APS.WebApp.Services.OperationalAudit.Configuration
{
    /// <summary>
    /// Projeção IMUTÁVEL e somente-leitura de uma árvore de pastas auditável pela Etapa 7 (correção
    /// pós-Checkpoint 2, item 2). NUNCA uma lista inventada pela auditoria: os valores usados para
    /// PRODUCAO_MODELOS_3D_07/_08 são cópia literal da configuração operacional REAL —
    /// <c>Services\LdpUpdateService.cs</c>, campo estático <c>PastasAtributos</c>, entrada com
    /// <c>Chave = CHAVE_PRODUCAO_MODELOS_3D</c> (na correção presente, aproximadamente linhas 403-424:
    /// <c>CaminhosRaiz</c> com os 6 caminhos completos sob "07 - 3D AUTHORING SOFTWARE" e
    /// "08 - FEDERATED MODEL" / "08.01 - 3D AUTHORING SOFTWARE"). <c>LdpUpdateService.cs</c> é um arquivo
    /// operacional protegido — nunca alterado, nunca lido em tempo de execução; esta projeção é mantida
    /// manualmente em sincronia por revisão de código, com a origem exata documentada aqui, exatamente como
    /// pedido ("a auditoria deve consumir/projetar essa configuração... nunca de uma whitelist inventada").
    /// </summary>
    public sealed class AuditAttributeStructureProfile
    {
        /// <summary>Chave usada por <see cref="AttributeStructureRequirementResolver"/> (RequiredAttributeStructures/OptionalAttributeStructures/AlternativeStructures do perfil do projeto).</summary>
        public string ChaveConfiguracao { get; init; } = string.Empty;

        /// <summary>Alias(es) para localizar a RAIZ desta árvore especificamente — nunca compartilhado como "alternativa" implícita de outra árvore (isso é decidido pelo perfil, não pela lista de aliases).</summary>
        public string[] AliasesRaiz { get; init; } = Array.Empty<string>();

        public bool Recursivo { get; init; }

        public string[] AtributosObrigatorios { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Caminhos completos PERMITIDOS abaixo da raiz já resolvida (mesmo formato de
        /// <c>AccItemRef.CaminhoRelativoAPastaRaiz</c> — nomes de pasta reais separados por "/", nunca só o
        /// prefixo numérico). Vazio = SEM_CONFIGURACAO_SUFICIENTE para ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA —
        /// a regra nunca inventa "não permitido" sem esta configuração explícita.
        /// </summary>
        public string[] CaminhosPermitidosRelativos { get; init; } = Array.Empty<string>();
    }
}
