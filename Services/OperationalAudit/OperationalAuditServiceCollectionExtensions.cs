using System;
using System.Net.Http;
using APS.WebApp.Services;
using APS.WebApp.Services.OperationalAudit.Configuration;
using APS.WebApp.Services.OperationalAudit.Persistence;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.OperationalAudit.Reporting;
using APS.WebApp.Services.OperationalAudit.Safety;
using APS.WebApp.Services.Planning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Composition root DEDICADO da auditoria operacional (Checkpoint 4, seção 4 do pedido) — registra
    /// SOMENTE o necessário para <see cref="IOperationalAuditRunner"/> funcionar: as 17 regras, os 7
    /// adaptadores somente-leitura, o repositório (via factory, nunca resolvido eagerly — mesmo motivo de
    /// <see cref="OperationalAuditRunner"/> não injetar <see cref="IAuditRepository"/> direto), os 3 writers
    /// e <see cref="IPlanningPreflightService"/>. NUNCA registra:
    ///
    /// - <c>GrdAutomationWorker</c> nem qualquer <c>IHostedService</c>;
    /// - <c>AutodeskApiService</c>/<c>LdpUpdateService</c>/<c>AccReviewWorkflowService</c>/etc. (os 13
    ///   arquivos operacionais protegidos) — nem sequer referenciados aqui;
    /// - <c>DbContext</c>/<c>DatabaseService</c> operacional;
    /// - nenhum timer, nenhuma sincronização, nenhum upload.
    ///
    /// Não é chamado por <c>Program.cs</c> do site (Checkpoint 4, seção 2: a auditoria continua
    /// completamente separada do ciclo operacional) — só pelo executor manual em
    /// <c>Tools\NA_Connect.OperationalAudit.Cli</c>.
    /// </summary>
    public static class OperationalAuditServiceCollectionExtensions
    {
        public static IServiceCollection AddOperationalAudit(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton(configuration);
            services.AddSingleton(OperationalAuditOptions.LerConfiguracao(configuration));
            services.AddMemoryCache();

            // Autenticação OAuth do robô (só GET consome o token — TokenManager em si faz um único POST
            // para o endpoint de token do Autodesk, que não é um endpoint de dado operacional; não passa
            // pelo AuditHttpWriteGuardHandler porque não é um adaptador de leitura de dados do ACC).
            services.AddHttpClient<TokenManager>();

            // Adaptadores somente-leitura — todo HttpClient contra a API do ACC usa
            // AuditHttpWriteGuardHandler como handler mais externo (seção 18: nenhum verbo != GET/HEAD/OPTIONS
            // pode sair do processo).
            services.AddTransient<AuditHttpWriteGuardHandler>();
            services.AddHttpClient<IAuditAccReadOnlyAdapter, AuditAccReadOnlyAdapter>()
                .AddHttpMessageHandler<AuditHttpWriteGuardHandler>();
            services.AddHttpClient<IAuditWorkflowReadOnlyAdapter, AuditWorkflowReadOnlyAdapter>()
                .AddHttpMessageHandler<AuditHttpWriteGuardHandler>();

            services.AddSingleton<IAuditWorkbookReadOnlyAdapter, AuditWorkbookReadOnlyAdapter>();
            services.AddSingleton<IAuditScheduleReadOnlyAdapter, AuditScheduleReadOnlyAdapter>();
            services.AddSingleton<IAuditQualityReadOnlyAdapter, AuditQualityReadOnlyAdapter>();
            services.AddSingleton<IAuditPlanningReadOnlyAdapter, AuditPlanningReadOnlyAdapter>();
            services.AddSingleton<IPlanningPreflightService, PlanningPreflightService>();

            // SQL — sempre via IAuditSourceReader (SqlWriteVerbGuard + AuditSourceConnection, nunca
            // DatabaseService/DefaultConnection).
            services.AddScoped<IAuditSourceReader, AuditSourceReader>();
            services.AddScoped<IAuditDatabaseReadOnlyAdapter, AuditDatabaseReadOnlyAdapter>();

            // IAuditRepository só via factory (Func<IAuditRepository>) — OperationalAuditRunner só chama a
            // factory quando OperationalAuditOptions.PersistToDatabase=true (seção 6/18: quando --persist
            // não foi pedido, AuditRepositoryConnection nunca precisa nem existir).
            services.AddTransient<AuditRepository>();
            services.AddTransient<Func<IAuditRepository>>(sp => () => sp.GetRequiredService<AuditRepository>());

            services.AddScoped<IOperationalAuditReportWriter, OperationalAuditTxtReportWriter>();
            services.AddScoped<IOperationalAuditReportWriter, OperationalAuditCsvReportWriter>();
            services.AddScoped<IOperationalAuditReportWriter, OperationalAuditJsonReportWriter>();

            RegistrarRegras(services);

            services.AddScoped<IOperationalAuditRunner, OperationalAuditRunner>();

            return services;
        }

        /// <summary>As 17 regras reais (Etapas 0,1,2,4,5,5B,6,7,8,9,10,11,12,13,14,15,16,17) — nunca uma lista duplicada em outro lugar; se uma nova Stage*AuditRule for criada, só precisa ser adicionada aqui.</summary>
        private static void RegistrarRegras(IServiceCollection services)
        {
            services.AddScoped<IOperationalAuditRule, Rules.Stage00ProjectDnaAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage01TimesheetAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage02HoursRevisionAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage04GrdAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage05WorkflowAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage05BReviewSnapshotAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage06CostAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage07AttributesAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage08ApprovalVerdictAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage09ScheduleAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage10MirrorAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage11LdpSqlAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage12ExternalActionAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage13InternalActionAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage14ScopeDeviationAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage15WeeklyScheduleAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage16HoldControlAuditRule>();
            services.AddScoped<IOperationalAuditRule, Rules.Stage17QualityAuditRule>();
        }
    }
}
