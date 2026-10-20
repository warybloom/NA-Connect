using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace APS.WebApp.Services.OperationalAudit.Configuration
{
    /// <summary>
    /// Configuração da seção <c>OperationalAudit</c> do appsettings.json (seção 4 da correção da Fase 2).
    /// Lida manualmente via <see cref="LerConfiguracao"/> — mesmo padrão de
    /// <c>WorkerTestSelector.LerConfiguracao</c> — nunca via <c>IConfiguration.Get&lt;T&gt;()</c>, para não
    /// depender de um pacote de binding adicional.
    ///
    /// <see cref="Enabled"/>=false (padrão): nenhuma chamada adicional, nenhum acesso a ACC/SQL, nenhum
    /// relatório — a auditoria simplesmente não roda.
    ///
    /// Correção Checkpoint 4.1, seção 3 — semântica REAL confirmada por pesquisa de código:
    /// <see cref="Configuration.OperationalAuditOptions.Enabled"/> NUNCA é lida por
    /// <c>OperationalAuditRunner</c> nem por nenhum outro tipo dentro de <c>Services\OperationalAudit\</c> —
    /// é responsabilidade exclusiva de quem CHAMA o runner decidir, a partir dela, se deve chamar ou não
    /// (Solução A do pedido: "Enabled é apenas gate do host/site"). No site (<c>Program.cs</c>),
    /// <c>Enabled</c> hoje não tem nenhum efeito operante porque <c>Program.cs</c> nunca registra nem chama
    /// <c>IOperationalAuditRunner</c> de forma alguma — o "gate" real do site é estrutural (nada é
    /// registrado), não um `if (Enabled)` em tempo de execução. O executor manual
    /// (<c>Tools\NA_Connect.OperationalAudit.Cli</c>) lê sua PRÓPRIA cópia desta classe, a partir do seu
    /// PRÓPRIO <c>appsettings.json</c> (processo, config e instância de DI inteiramente separados do site) —
    /// <c>CliModeOptionsApplier.Aplicar</c> marca essa instância local como <c>Enabled=true</c> só quando o
    /// usuário autoriza explicitamente via <c>ArgumentParser</c>, e isso nunca escreve no appsettings do
    /// site nem em nenhum estado global compartilhado.
    /// </summary>
    public sealed class OperationalAuditOptions
    {
        public bool Enabled { get; set; }

        /// <summary>true: só roda depois que TODAS as etapas normais do ciclo terminarem, nunca em paralelo. false: execução isolada via <c>OperationalAuditRunner</c>.</summary>
        public bool RunAfterAutomation { get; set; }

        public bool GenerateTxt { get; set; } = true;
        public bool GenerateJson { get; set; } = true;
        public bool GenerateCsv { get; set; } = true;

        /// <summary>Quando true, persiste em <c>audit.*</c> via <see cref="RepositoryConnectionStringName"/>. Falha aqui nunca impede os arquivos (seção 17).</summary>
        public bool PersistToDatabase { get; set; }

        /// <summary>Nome da entrada em <c>ConnectionStrings</c> usada para LEITURA (somente SELECT) dos dados operacionais.</summary>
        public string SourceConnectionStringName { get; set; } = "AuditSourceConnection";

        /// <summary>Nome da entrada em <c>ConnectionStrings</c> usada para ler/escrever exclusivamente no schema <c>audit</c>.</summary>
        public string RepositoryConnectionStringName { get; set; } = "AuditRepositoryConnection";

        public string OutputDirectory { get; set; } = string.Empty;

        public bool IncludeInformationalFindings { get; set; }
        public int MaxEvidencePerFinding { get; set; } = 10;

        /// <summary>Vazia = todas as etapas. Quando preenchida, filtra quais Stage*AuditRule rodam (independente do WorkerTest operacional).</summary>
        public List<string> Stages { get; set; } = new();

        /// <summary>
        /// Perfis de projeto (seção 10 da correção de Checkpoint 2) — chave "default" cobre projetos sem
        /// perfil explícito. NUNCA usado como regra universal hardcoded: uma estrutura sem entrada em nenhum
        /// perfil aplicável é <see cref="Configuration.AttributeStructureRequirementOutcome.SemConfiguracaoSuficiente"/>,
        /// nunca "obrigatória por padrão".
        /// </summary>
        public Dictionary<string, AuditProjectProfile> ProjectProfiles { get; set; } = new();

        public static OperationalAuditOptions LerConfiguracao(IConfiguration configuration)
        {
            var options = new OperationalAuditOptions
            {
                Enabled = configuration.GetValue("OperationalAudit:Enabled", false),
                RunAfterAutomation = configuration.GetValue("OperationalAudit:RunAfterAutomation", false),
                GenerateTxt = configuration.GetValue("OperationalAudit:GenerateTxt", true),
                GenerateJson = configuration.GetValue("OperationalAudit:GenerateJson", true),
                GenerateCsv = configuration.GetValue("OperationalAudit:GenerateCsv", true),
                PersistToDatabase = configuration.GetValue("OperationalAudit:PersistToDatabase", false),
                SourceConnectionStringName = configuration["OperationalAudit:SourceConnectionStringName"] ?? "AuditSourceConnection",
                RepositoryConnectionStringName = configuration["OperationalAudit:RepositoryConnectionStringName"] ?? "AuditRepositoryConnection",
                OutputDirectory = configuration["OperationalAudit:OutputDirectory"] ?? string.Empty,
                IncludeInformationalFindings = configuration.GetValue("OperationalAudit:IncludeInformationalFindings", false),
                MaxEvidencePerFinding = configuration.GetValue("OperationalAudit:MaxEvidencePerFinding", 10),
                Stages = configuration.GetSection("OperationalAudit:Stages").Get<List<string>>() ?? new List<string>(),
                ProjectProfiles = configuration.GetSection("OperationalAudit:ProjectProfiles").Get<Dictionary<string, AuditProjectProfile>>()
                    ?? new Dictionary<string, AuditProjectProfile>()
            };
            return options;
        }

        /// <summary>Perfil nomeado, com fallback para "default"; <c>null</c> se nem o nomeado nem "default" existirem (tratado como configuração insuficiente pelas regras, nunca como "tudo obrigatório").</summary>
        public AuditProjectProfile? ObterPerfil(string? nomeDoPerfil)
        {
            if (!string.IsNullOrWhiteSpace(nomeDoPerfil) && ProjectProfiles.TryGetValue(nomeDoPerfil, out var perfil))
            {
                return perfil;
            }
            return ProjectProfiles.TryGetValue("default", out var perfilPadrao) ? perfilPadrao : null;
        }
    }
}
