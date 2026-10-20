using APS.WebApp.Services;
using APS.WebApp.Services.Http;
using APS.WebApp.Services.Logging;
using APS.WebApp.Services.LdpTipo2;
using APS.WebApp.Services.LdpRouting;
using APS.WebApp.Services.CronogramaTipo2;
using APS.WebApp.Services.GestaoVista;
using APS.WebApp.Services.Qualidade;
using APS.WebApp.Services.Planning;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Configuration;
using APS.WebApp.Services.OperationalAudit.Persistence;
using APS.WebApp.Services.OperationalAudit.Safety;
using APS.WebApp.Services.OperationalAudit.Reporting;
using APS.WebApp.Services.DocumentQuality;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using OfficeOpenXml;
using Microsoft.EntityFrameworkCore;
using AccIntegration.WebApp.Data;
using System.Data;
using NA_Connect.Services;
using System.IO;

var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";

// ===================================================================
// 🎯 PREPARAÇÃO PARA RODAR COMO SERVIÇO DO WINDOWS (BACKGROUND)
// ===================================================================
var options = new WebApplicationOptions
{
    Args = args,
    // Garante que o robô encontre o appsettings.json e as pastas corretas quando rodar invisível
    ContentRootPath = AppContext.BaseDirectory 
};

var builder = WebApplication.CreateBuilder(options);

// Ambiente de desenvolvimento: a configuração editada no repositório prevalece
// sobre a cópia antiga em bin/. O conteúdo (App_Data/serviços) permanece no bin.
// No serviço instalado, sem .csproj na pasta de trabalho, prevalece o appsettings
// publicado junto ao executável.
var workspaceConfig = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
if (builder.Environment.IsDevelopment()
    && File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "AccIntegration.WebApp.csproj"))
    && File.Exists(workspaceConfig))
{
    builder.Configuration.AddJsonFile(workspaceConfig, optional: false, reloadOnChange: true);
}

Console.WriteLine(
    "[PERMISSIONS_BOOT] Enforce=" + builder.Configuration.GetValue<bool>("Permissions:Enforce")
    + " PilotCount=" + builder.Configuration.GetSection("Permissions:PilotUserIds").Get<int[]>()?.Length
    + " MasterRecoveryConfigured=" + (builder.Configuration.GetSection("Permissions:MasterAdmins").Get<string[]>()?.Length > 0)
    + " ConfigSource=" + (builder.Environment.IsDevelopment() && File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "AccIntegration.WebApp.csproj"))
        ? "WORKSPACE" : "DEPLOYED"));

// Transforma oficialmente a aplicação num Serviço do Windows
builder.Host.UseWindowsService(windowsOptions =>
{
    windowsOptions.ServiceName = "NA_Connect_Robo";
});

// ===================================================================
// ✅ LOG DE INICIALIZAÇÃO
// ===================================================================
Console.WriteLine($"\n\n--- APLICAÇÃO INICIADA COM O CÓDIGO MAIS RECENTE --- (Build de {DateTime.Now}) ---\n\n");

// ===================================================================
// ✅ CONFIGURAÇÃO DE SERVIÇOS (DI Container)
// ===================================================================

builder.Services.AddDbContext<ApplicationDbContext>(dbOptions =>
    dbOptions.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

builder.Services.AddCors(corsOptions =>
{
    corsOptions.AddPolicy(name: MyAllowSpecificOrigins,
                      policy =>
                      {
                          policy.WithOrigins("http://localhost:5173") 
                                .AllowAnyHeader()
                                .AllowAnyMethod()
                                .AllowCredentials();
                      });
});

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IProjectReportAccess, APS.WebApp.Services.Reports.AccProjectReportAccess>();
// Permissões granulares (camada ADICIONAL a IProjectReportAccess). Permissions:Enforce=false preserva o comportamento atual
// até a migração Migrations/Permissions/20260929_001_Permissions.sql ser aprovada e aplicada.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<APS.WebApp.Services.Identity.ICurrentUserAccessor, APS.WebApp.Services.Identity.SessionCurrentUserAccessor>();
builder.Services.AddScoped<APS.WebApp.Services.Identity.INaCurrentUserAccessor, APS.WebApp.Services.Identity.SessionNaCurrentUserAccessor>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IProductionAuditRepository, APS.WebApp.Services.Reports.ProductionAuditRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Costs.ICostManagementService, APS.WebApp.Services.Costs.CostManagementService>();
builder.Services.AddScoped<APS.WebApp.Services.Costs.IProjectCostHistoryService, APS.WebApp.Services.Costs.ProjectCostHistoryService>();
// Token Autodesk 3-legged do usuário (separado do TokenManager 2-legged). Coordinator singleton = single-flight do refresh entre requisições;
// o POST de refresh usa HttpClient nomeado SEM AutodeskRetryHandler (ver AutodeskUserTokenServiceCollectionExtensions).
APS.WebApp.Services.AutodeskAuth.AutodeskUserTokenServiceCollectionExtensions.AddAutodeskUserTokenProvider(builder.Services);
// Login N&A local (e-mail + senha; auth.UserCredentials). Independente da Autodesk.
builder.Services.AddScoped<APS.WebApp.Services.LocalAuth.INaLocalCredentialRepository, APS.WebApp.Services.LocalAuth.SqlNaLocalCredentialRepository>();
builder.Services.AddScoped<APS.WebApp.Services.LocalAuth.INaLocalAuthService, APS.WebApp.Services.LocalAuth.NaLocalAuthService>();
builder.Services.AddScoped<APS.WebApp.Services.Sidebar.ISidebarStore, APS.WebApp.Services.Sidebar.SqlSidebarStore>();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-Sidebar-CSRF");
builder.Services.AddScoped<APS.WebApp.Services.Permissions.IPermissionRepository, APS.WebApp.Services.Permissions.SqlPermissionRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Permissions.PermissionService>();
builder.Services.AddScoped<APS.WebApp.Services.Tv.Portal.TvPortalPermissionService>();
builder.Services.AddScoped<APS.WebApp.Services.Permissions.IPermissionService>(sp => sp.GetRequiredService<APS.WebApp.Services.Tv.Portal.TvPortalPermissionService>());
builder.Services.AddScoped<APS.WebApp.Services.Tv.Portal.ITvPortalProjectRegistry, APS.WebApp.Services.Tv.Portal.SqlTvPortalProjectRegistry>();
builder.Services.AddScoped<APS.WebApp.Services.Tv.Portal.TvPortalIdentityService>();
builder.Services.AddScoped<APS.WebApp.Services.Tv.Portal.ITvPortalRoleDesignation, APS.WebApp.Services.Tv.Portal.SqlTvPortalRoleDesignation>();
builder.Services.AddSingleton(sp => new APS.WebApp.Services.Tv.Portal.TvPortalAssignmentStore(Path.Combine(sp.GetRequiredService<IConfiguration>()["TvConfiguration:Directory"] ?? Path.Combine(sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath, "App_Data"), "tv-portal-assignments.json")));
builder.Services.AddScoped<APS.WebApp.Services.Permissions.IProjectAccessService>(sp =>
    sp.GetRequiredService<APS.WebApp.Services.Tv.Portal.TvPortalPermissionService>());
// Sincronização ACC -> usuários/projetos/perfil padrão (login: só o próprio usuário; admin: por projeto).
builder.Services.AddScoped<APS.WebApp.Services.Permissions.Sync.IPermissionSyncRepository, APS.WebApp.Services.Permissions.Sync.SqlPermissionSyncRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Permissions.Sync.IAccMembershipSource, APS.WebApp.Services.Permissions.Sync.AccMembershipSource>();
builder.Services.AddScoped<APS.WebApp.Services.Permissions.Sync.PermissionSyncService>();
builder.Services.AddSingleton<APS.WebApp.Services.Permissions.Sync.PermissionLoginSync>();
// Operações administrativas em lote (perfis em vários usuários/projetos; cópia de acesso entre projetos).
builder.Services.AddScoped<APS.WebApp.Services.Permissions.Bulk.IBulkPermissionRepository, APS.WebApp.Services.Permissions.Bulk.SqlBulkPermissionRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Permissions.Bulk.BulkPermissionService>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IWeeklyReportRepository, APS.WebApp.Services.Reports.WeeklyReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IQualityReportRepository, APS.WebApp.Services.Reports.QualityReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IProductionDocumentsReportRepository, APS.WebApp.Services.Reports.ProductionDocumentsReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IProductionStatusReportRepository, APS.WebApp.Services.Reports.ProductionStatusReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IHoldReportRepository, APS.WebApp.Services.Reports.HoldReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IScopeDeviationReportRepository, APS.WebApp.Services.Reports.ScopeDeviationReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IInternalActionReportRepository, APS.WebApp.Services.Reports.InternalActionReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IExternalActionReportRepository, APS.WebApp.Services.Reports.ExternalActionReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IWeeklyDetailsReportRepository, APS.WebApp.Services.Reports.WeeklyDetailsReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IScheduleReportRepository, APS.WebApp.Services.Reports.ScheduleReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.ILdpDetailsReportRepository, APS.WebApp.Services.Reports.LdpDetailsReportRepository>();
// CDE / Lista de Documentos — visão da LDP Espelho (Tipo 1/Tipo 2), somente leitura.
builder.Services.AddScoped<APS.WebApp.Services.Reports.ICdeDocumentListRepository, APS.WebApp.Services.Reports.CdeDocumentListRepository>();
// Relatório de Produção — dashboards interativos (Fase 3), somente leitura.
builder.Services.AddScoped<APS.WebApp.Services.Reports.Dashboards.IProductionDashboardRepository, APS.WebApp.Services.Reports.Dashboards.ProductionDashboardRepository>();
// Relatório de Produção (Controle de Produção) — somente leitura sobre fontes existentes.
builder.Services.AddScoped<APS.WebApp.Services.Reports.IChangeOrderReportRepository, APS.WebApp.Services.Reports.ChangeOrderReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IQualityDeviationReportRepository, APS.WebApp.Services.Reports.QualityDeviationReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IRncReportRepository, APS.WebApp.Services.Reports.RncReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IDocumentControlReportRepository, APS.WebApp.Services.Reports.DocumentControlReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IScheduleLevelingReportRepository, APS.WebApp.Services.Reports.ScheduleLevelingReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IScheduleControlReportRepository, APS.WebApp.Services.Reports.ScheduleControlReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Planning.IPmoPlanningFactsProvider, APS.WebApp.Services.Planning.PmoPlanningFactsProvider>();
builder.Services.AddScoped<NA_Connect.Services.Coordination.CoordinationStandardRepository>();
builder.Services.AddScoped<NA_Connect.Services.Coordination.CoordinationDiagnosticsService>();
builder.Services.AddScoped<APS.WebApp.Services.Planning.IPmoActualFactsProvider, APS.WebApp.Services.Planning.PmoActualFactsProvider>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IGestaoVistaReportRepository, APS.WebApp.Services.Reports.GestaoVistaReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IAvanco4DReportRepository, APS.WebApp.Services.Reports.Avanco4DReportRepository>();
builder.Services.AddScoped<APS.WebApp.Services.Reports.IFinanceiroHorasReportRepository, APS.WebApp.Services.Reports.FinanceiroHorasReportRepository>();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();

// -------------------------------------------------------------------
// 📝 LOG DE ARQUIVO POR EXECUÇÃO (Execucao_Completa_*.txt)
// -------------------------------------------------------------------
// ExecutionFileLoggerProvider é registrado tanto como ILoggerProvider (para que TODO ILogger
// resolvido pelo container, em qualquer serviço/escopo, grave nele) quanto como singleton comum
// (para que GrdAutomationWorker consiga chamar BeginExecution/DisposeAsync e delimitar o arquivo
// de uma execução em lote específica). Ver Services/Logging/ExecutionFileLoggerProvider.cs.
var executionFileLoggerProvider = new ExecutionFileLoggerProvider();
builder.Services.AddSingleton(executionFileLoggerProvider);
builder.Logging.AddProvider(executionFileLoggerProvider);

// -------------------------------------------------------------------
// 🔁 RETRY / BACKOFF EXPONENCIAL PARA A API DA AUTODESK (APS)
// -------------------------------------------------------------------
// O AutodeskRetryHandler (Services/Http/AutodeskRetryHandler.cs) intercepta TODAS as
// chamadas HTTP feitas via IHttpClientFactory (incluindo o HttpClient "puro" injetado em
// AutodeskApiService, TokenManager e demais serviços) e faz retry automático em respostas
// HTTP 429 (Too Many Requests), respeitando o cabeçalho "Retry-After" retornado pela APS,
// além de erros transitórios 5xx/falhas de rede com backoff exponencial + jitter.
builder.Services.Configure<AutodeskRetryOptions>(builder.Configuration.GetSection(AutodeskRetryOptions.SectionName));
builder.Services.AddTransient<AutodeskRetryHandler>();

builder.Services.AddHttpClient();
builder.Services.ConfigureHttpClientDefaults(httpClientBuilder =>
{
    httpClientBuilder.AddHttpMessageHandler<AutodeskRetryHandler>();
});

builder.Services.AddSession(sessionOptions =>
{
    sessionOptions.IdleTimeout = TimeSpan.FromMinutes(60);
    sessionOptions.Cookie.HttpOnly = true;
    sessionOptions.Cookie.IsEssential = true;
});

// -------------------------------------------------------------------
// 🛠 REGISTRO DE SERVIÇOS MODULARES
// -------------------------------------------------------------------
builder.Services.AddScoped<AutodeskApiService>();
builder.Services.AddScoped<TokenManager>();
builder.Services.AddScoped<AccountUsersService>(); 
builder.Services.AddScoped<NomenclaturaService>();
builder.Services.AddScoped<PlaceholderCreationService>();
builder.Services.AddScoped<AccScheduleSyncService>();
builder.Services.AddScoped<ProjectAutoDiscoveryService>();
builder.Services.AddScoped<AccMetadataService>();
builder.Services.AddScoped<TimesheetDumpService>();
builder.Services.AddScoped<LdpToCustoSyncService>();
builder.Services.AddScoped<AccStatusResolverService>();
builder.Services.AddScoped<AccReviewWorkflowService>();
builder.Services.AddScoped<AccReviewStageSnapshotService>();
builder.Services.AddScoped<PdfMetadataService>();
builder.Services.AddScoped<GrdToLdpSyncService>();
builder.Services.AddScoped<LdpUpdateService>();

// Registros para a Automação N&A Connect
builder.Services.AddScoped<DatabaseService>(); 
builder.Services.AddScoped<AccProjectSyncService>(); 
builder.Services.AddScoped<PlanningControlSyncService>();
builder.Services.AddScoped<DocumentMatchingService>();
builder.Services.AddScoped<QualityControlSyncService>();
builder.Services.AddScoped<IQualidadePreflightService, QualidadePreflightService>();
builder.Services.AddScoped<IQualidadeRepository, QualidadeRepository>();
builder.Services.AddScoped<IPlanningPreflightService, PlanningPreflightService>();
builder.Services.AddScoped<IPlanningRepository, PlanningRepository>();

// Novo Motor de LDP (fundação) — isolado, ver AUDITORIA_FORGESYNC_NA/17-*.md. Schema ldp.* proposto,
// não executado; nenhum destes serviços é chamado pelo Worker/pipeline atual.
builder.Services.AddScoped<NA_Connect.Services.Ldp.LdpConfigDatabaseService>();
builder.Services.AddSingleton<NA_Connect.Services.Ldp.LdpArquivoStorageService>();
builder.Services.AddScoped<NA_Connect.Services.Ldp.LdpMppParserService>();
builder.Services.AddScoped<NA_Connect.Services.Ldp.LdpEspelhoGeradorService>();

// -------------------------------------------------------------------
// 🧩 LDP TIPO 2 (infraestrutura) + ROTEAMENTO TIPO 1 / TIPO 2
// -------------------------------------------------------------------
// A partir do checkpoint pré-canário, LdpProcessingRouterService É chamado por
// GrdAutomationWorker.ProcessarCicloCompleto (logo após o download/descoberta da LDP, para projetos
// com Tipo_LDP = 2) — o Router, por sua vez, orquestra as 4 etapas do Tipo 2 (LDP -> Cronograma ->
// matching -> publicação condicional). Mesmo lifetime (Scoped) usado pelos demais serviços acima.
builder.Services.AddScoped<ILdpTipo2GateRepository, LdpTipo2GateRepository>();
builder.Services.AddScoped<ILdpTipo2Parser, LdpTipo2Parser>();
builder.Services.AddScoped<ILdpTipo2Service, LdpTipo2Service>();
builder.Services.AddScoped<ILdpProjetoRepository, LdpProjetoRepository>();
builder.Services.AddScoped<ILdpProcessingRouterService, LdpProcessingRouterService>();

// Fase 3: persistência transacional (documento + 3 gates).
builder.Services.AddScoped<ILdpTipo2Repository, LdpTipo2Repository>();
builder.Services.AddScoped<ILdpTipo2PersistenceService, LdpTipo2PersistenceService>();

// Fase 4A: cronograma Tipo 2 (leitura do MPP + persistência transacional + matching somente leitura).
// Módulo separado do cronograma Tipo 1 (AccScheduleSyncService, inalterado).
builder.Services.AddScoped<ICronogramaTipo2Parser, CronogramaTipo2Parser>();
builder.Services.AddScoped<ICronogramaTipo2Repository, CronogramaTipo2Repository>();
builder.Services.AddScoped<ICronogramaTipo2Service, CronogramaTipo2Service>();

// Fase 4B: sincronização Cronograma Tipo 2 -> gates da LDP Tipo 2 (matching + UPDATE transacional).
builder.Services.AddScoped<ICronogramaTipo2LdpSyncRepository, CronogramaTipo2LdpSyncRepository>();
builder.Services.AddScoped<ICronogramaTipo2LdpSyncService, CronogramaTipo2LdpSyncService>();

// Publicação condicional da LDP Tipo 2 no ACC (checkpoint pré-canário) — só cria nova versão quando
// há alteração real (diff célula-a-célula), mesmo padrão de LdpUpdateService.UpdateLdpFromDatabaseAsync.
builder.Services.AddScoped<ICronogramaTipo2LdpPublicacaoService, CronogramaTipo2LdpPublicacaoService>();

// Gestão à Vista (ingestão LDP financeiro/HH — Custo Real/Custo profissionais, schema [gestao]).
// Módulo aditivo e desacoplado: nunca lê/altera Services/CronogramaTipo2 ou Services/LdpTipo2. Nunca
// executa DDL — GestaoVistaRepository só detecta se as migrations manuais
// (Migrations/GestaoVista/01,05,06,07) já foram aplicadas. Timesheet NÃO é mais persistido por este
// fluxo (correção 17/09/2026) — ver ETAPA 03 abaixo.
builder.Services.AddScoped<IGestaoVistaExtractor, GestaoVistaExtractor>();
builder.Services.AddScoped<IGestaoVistaRepository, GestaoVistaRepository>();
builder.Services.AddScoped<ILdpGestaoVistaSyncService, LdpGestaoVistaSyncService>();

// ETAPA 03 — Snapshot Timesheet para Gestão à Vista (correção 17/09/2026). Lê a aba TIMESHEET da LDP já
// reconstruída pela Etapa 01 e substitui o snapshot atual em gestao.TimesheetSnapshotAtual (tabela NOVA,
// distinta do ledger histórico gestao.TimesheetLancamento — nunca executa DDL, migration manual
// Migrations/GestaoVista/08_*).
builder.Services.AddScoped<IGestaoVistaTimesheetSnapshotRepository, GestaoVistaTimesheetSnapshotRepository>();
builder.Services.AddScoped<IEtapa03TimesheetSnapshotService, Etapa03TimesheetSnapshotService>();
// Etapa 4B do robô: classificação gerencial de emissão (somente leitura).
builder.Services.AddScoped<APS.WebApp.Services.Documents.IDocumentEmissionAuditService, APS.WebApp.Services.Documents.DocumentEmissionAuditService>();

// -------------------------------------------------------------------
// 🕵️ AUDITORIA OPERACIONAL (infraestrutura, Fase 2 / Checkpoint 1)
// -------------------------------------------------------------------
// Subsistema paralelo e SOMENTE-LEITURA sobre os dados operacionais existentes — nenhuma
// dependência dos serviços acima, nenhuma dependência inversa (nada abaixo é chamado por eles).
// Registro passivo, mantido por compatibilidade/diagnóstico: nenhum destes 6 serviços é resolvido
// diretamente pelo site (GrdAutomationWorker não os conhece). O runner/regras/repository/writers
// REAIS usados em produção vêm de um container isolado, próprio, montado dentro de
// OperationalAuditExecutionCoordinator — via a extensão de composição da auditoria, ver
// Services\OperationalAudit\OperationalAuditServiceCollectionExtensions.cs — nunca deste container
// principal, para nunca duplicar os 3 writers abaixo (TXT/CSV/JSON seriam gerados duas vezes se os
// dois registros coexistissem no mesmo container).
builder.Services.AddSingleton(OperationalAuditOptions.LerConfiguracao(builder.Configuration));
builder.Services.AddScoped<IAuditSourceReader, AuditSourceReader>();
// O repositório exige AuditRepositoryConnection e só pode ser materializado durante uma
// persistência solicitada. Endpoints de configuração de Auditoria de Qualidade não o resolvem.
builder.Services.AddTransient<AuditRepository>();
builder.Services.AddTransient<Func<IAuditRepository>>(sp => () => sp.GetRequiredService<AuditRepository>());
builder.Services.AddTransient<AuditHttpWriteGuardHandler>();
builder.Services.AddScoped<IOperationalAuditReportWriter, OperationalAuditTxtReportWriter>();
builder.Services.AddScoped<IOperationalAuditReportWriter, OperationalAuditCsvReportWriter>();
builder.Services.AddScoped<IOperationalAuditReportWriter, OperationalAuditJsonReportWriter>();

// -------------------------------------------------------------------
// 🕵️ AUDITORIA OPERACIONAL — INTEGRAÇÃO DE PRODUÇÃO
// -------------------------------------------------------------------
// Único ponto de contato entre GrdAutomationWorker e a Auditoria Operacional (seção 6/14 do
// checkpoint de integração): o Worker conhece SOMENTE IOperationalAuditExecutionCoordinator, nunca
// o executor/as 18 regras da auditoria diretamente. O coordinator nunca lança para quem chama —
// qualquer falha (config ausente, SQL fora do ar, ACC fora do ar, regra com bug) vira
// OperationalAuditCoordinatorResult.ErroFatal, nunca uma exceção que poderia interromper o robô.
builder.Services.AddScoped<IOperationalAuditExecutionCoordinator, OperationalAuditExecutionCoordinator>();

// Auditoria manual de Qualidade de Documentos: somente leitura em ACC/LDP; templates em config.* e achados em audit.*.
builder.Services.AddScoped<IOcrTemplateRepository, SqlOcrTemplateRepository>();
// Catálogo DINÂMICO de campos auditáveis: colunas reais de dbo.Ldp_Unificada (metadados do SQL Server, cache de 10 min).
builder.Services.AddSingleton<ILdpColumnCatalog, SqlLdpColumnCatalog>();
builder.Services.AddScoped<IPdfRegionTextReader, PdfPigRegionTextReader>();
builder.Services.AddScoped<IDocumentQualityOperationalAuditPersistence, DocumentQualityOperationalAuditPersistence>();
builder.Services.AddScoped<IDocumentQualityAuditService, DocumentQualityAuditService>();
builder.Services.AddScoped<DocumentPresenceAuditService>();
builder.Services.AddScoped<DisciplineStandardRepository>();
builder.Services.AddScoped<DisciplineAuditService>();
builder.Services.AddScoped<InformationQualityRedistributionService>();
builder.Services.AddScoped<InformationQualityProgressService>();
builder.Services.AddScoped<InformationQualityAuditService>();
builder.Services.AddSingleton(sp => new DocumentPresenceAuditStore(
    Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "App_Data", "document-presence-audits")));
// "Auditar agora" roda FORA da requisição HTTP (POST → 202 + executionId; GET runs/{id} = progresso): evita o HTTP 524 do proxy.
// Uma auditoria por vez no servidor; cada execução usa um escopo próprio de DI (mesmo serviço/regra acima).
builder.Services.AddSingleton(sp =>
{
    var scopes = sp.GetRequiredService<IServiceScopeFactory>();
    return new DocumentQualityRunQueue(async (request, executionId, onProgress, ct) =>
        {
            using var scope = scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IDocumentQualityAuditService>()
                .RunAsync(request.HubId, request.ProjectIdAcc, request.AccessToken, request.TriggeredBy, ct, executionId, onProgress);
        },
        sp.GetRequiredService<ILogger<DocumentQualityRunQueue>>(),
        sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping,
        maxConcurrent: 1);
});
builder.Services.AddSingleton<IDocumentQualityReportStore>(sp => new DocumentQualityReportStore(
    Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "App_Data", "document-quality-reports")));

// O MOTOR PRINCIPAL (ROBÔ)
// Mesma instância do Worker automático serve ao disparo manual (botão Iniciar Robô): sem segundo Worker e sem novo processo.
builder.Services.AddSingleton<GrdAutomationWorker>();
builder.Services.AddSingleton(sp => new APS.WebApp.Services.RobotRun.RobotConfigurationStore(
    Path.Combine(sp.GetRequiredService<IConfiguration>()["RobotConfiguration:Directory"] ??
        Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "App_Data"), "robot-configuration.json")));
builder.Services.AddSingleton<APS.WebApp.Services.RobotRun.IRobotProjectDirectory, APS.WebApp.Services.RobotRun.RobotProjectDirectory>();
// Configuração de Telas para TV (Gestão à Vista): JSON em App_Data, mesmo padrão do robot-configuration.json.
// Projetos elegíveis para as Telas de TV (já sincronizados; fonte interna, sem Autodesk).
builder.Services.AddScoped<APS.WebApp.Services.Tv.ITvProjetosElegiveisRepository, APS.WebApp.Services.Tv.SqlTvProjetosElegiveisRepository>();
builder.Services.AddSingleton(sp => new APS.WebApp.Services.Tv.TvConfigurationStore(
    Path.Combine(sp.GetRequiredService<IConfiguration>()["TvConfiguration:Directory"] ??
        Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "App_Data"), "tv-configuration.json")));
// Configurações → Normalizador de Siglas (schema próprio normalizador.*; migração manual em Migrations/Normalizador).
builder.Services.AddScoped<APS.WebApp.Services.DisciplineAlias.ISiglaDisciplinaStore, APS.WebApp.Services.DisciplineAlias.SqlSiglaDisciplinaStore>();
builder.Services.AddScoped<APS.WebApp.Services.DisciplineAlias.SiglaDisciplinaService>();
// Envio de documentos governado pela LDP: configuração de pastas por projeto e log em App_Data (sem DDL).
builder.Services.AddSingleton(sp => new APS.WebApp.Services.UploadGovernado.PastasDocumentosStore(
    Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "App_Data", "document-folders.json")));
builder.Services.AddSingleton(sp => new APS.WebApp.Services.UploadGovernado.UploadGovernadoLog(
    Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "App_Data", "upload-governado-log.jsonl"),
    sp.GetRequiredService<ILogger<APS.WebApp.Services.UploadGovernado.UploadGovernadoLog>>()));
builder.Services.AddScoped<APS.WebApp.Services.UploadGovernado.ILdpDocumentosAtuaisRepository, APS.WebApp.Services.UploadGovernado.LdpDocumentosAtuaisRepository>();
// Logo do Projeto (Identidade Visual): dbo.Projetos não tem coluna de imagem — arquivos em App_Data/project-identity (sem DDL).
builder.Services.AddSingleton(sp => new APS.WebApp.Services.ProjectIdentity.ProjectIdentityStore(
    sp.GetRequiredService<IConfiguration>()["ProjectIdentity:Directory"] ??
        Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "App_Data", "project-identity")));
builder.Services.AddSingleton(sp => new APS.WebApp.Services.RobotRun.RobotQueueRunner(
    sp.GetRequiredService<GrdAutomationWorker>().Gate,
    sp.GetRequiredService<APS.WebApp.Services.RobotRun.IRobotProjectDirectory>(),
    sp.GetRequiredService<GrdAutomationWorker>().ExecutarCicloConfiguradoAsync,
    sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
builder.Services.AddHostedService(sp => sp.GetRequiredService<GrdAutomationWorker>());
builder.Services.AddSingleton(sp =>
{
    var worker = sp.GetRequiredService<GrdAutomationWorker>();
    return new APS.WebApp.Services.RobotRun.ManualRobotRunner(worker.Gate, worker.ExecutarCicloManualAsync,
        sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
});

var app = builder.Build();

// ===================================================================
// ✅ PIPELINE DE REQUISIÇÕES HTTP
// ===================================================================

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage(); 
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseSession();
app.UseMiddleware<APS.WebApp.Services.Tv.Portal.TvPortalMiddleware>();
app.UseRouting();
app.UseCors(MyAllowSpecificOrigins);
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllers();

// ===================================================================
// ⛔ BLOCO DE TESTE E SINCRONIZAÇÃO — DESATIVADO PARA O CANÁRIO TIPO 2 (2026-08-25)
// ===================================================================
// Este bloco rodava incondicionalmente em TODO startup, antes do GrdAutomationWorker:
// (1) sincronizava TODOS os projetos de TODOS os hubs cadastrados via API real da Autodesk,
// (2) gravava metadado hardcoded no projeto "Unipar" (b.90621daf-972c-4291-af87-04843e717ec9).
// Ambos violam o isolamento exigido pelo canário (somente o projeto canário pode ser tocado).
// Desativado por decisão explícita do usuário para esta execução — não é uma correção de bug
// do canário, é scaffolding de debug pré-existente que não pertence ao fluxo operacional real.
// Reverter (descomentar) após o canário, se ainda for necessário para outro fim.
/*
using (var scope = app.Services.CreateScope())
{
    var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
    var syncService = scope.ServiceProvider.GetRequiredService<AccProjectSyncService>();

    Console.WriteLine("--------------------------------------------------");
    Console.WriteLine(">>> SQL TEST: Verificando Hubs no banco local...");

    try
    {
        DataTable hubsDataTable = dbService.GetHubs();
        if (hubsDataTable.Rows.Count > 0)
        {
            foreach (DataRow row in hubsDataTable.Rows)
            {
                Console.WriteLine($">>> [SUCESSO] Hub Ativo: {row["Nome_Hub"]}");
            }

            Console.WriteLine(">>> [INFO] Iniciando Sincronização ACC -> SQL...");
            await syncService.SincronizarProjetosDosHubs();
            Console.WriteLine(">>> [SUCESSO] Sincronização finalizada.");
        }
        else
        {
            Console.WriteLine(">>> [AVISO] Banco conectado, mas a tabela Hubs_Conta está vazia.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($">>> [ERRO] Falha na operação SQL/ACC: {ex.Message}");
    }
    Console.WriteLine("--------------------------------------------------\n");
}

using (var scopeTeste = app.Services.CreateScope())
{
    var db = scopeTeste.ServiceProvider.GetRequiredService<DatabaseService>();
    db.SalvarMetadado("b.90621daf-972c-4291-af87-04843e717ec9", "UNP001", "Projeto Unipar - Gestão N&A");
    Console.WriteLine(">>> TESTE DE METADADO CONCLUÍDO!");
}
*/

// ===================================================================
app.Run();
