using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Configuration;
using APS.WebApp.Services.OperationalAudit.Safety;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NA_Connect.OperationalAudit.Cli;

// ============================================================================================
// Executor manual da Auditoria Operacional (Checkpoint 4/4.1). NUNCA integrado ao ciclo do N&A Connect —
// processo separado, próprio Main, própria composição de DI (OperationalAuditServiceCollectionExtensions).
// A cadeia real de --dry-run/--run é ArgumentParser -> CliModeOptionsApplier.Aplicar ->
// CliModeOptionsApplier.GarantirHabilitadoParaExecucao -> IOperationalAuditRunner.ExecutarAsync — código
// alcançável de verdade (nunca morto/inalcançável), provado por ExecutarAuditoriaAoVivoAsync abaixo e por
// ProgramCsContractTests (varre o texto-fonte deste arquivo). Mesmo assim, NINGUÉM invocou
// "dotnet run -- --dry-run/--run" neste repositório ainda — a primeira execução real aguarda aprovação
// explícita separada (Checkpoint 4.1, seção 18/pedido de verificação: "não execute auditoria real").
// ============================================================================================

string diretorioBase = AppContext.BaseDirectory;
string raizDoRepositorio = LocalizarRaizDoRepositorio(diretorioBase);

var parseResult = ArgumentParser.Parse(args);
if (!parseResult.Sucesso)
{
    Console.Error.WriteLine($"[RECUSADO] {parseResult.MotivoRecusa}");
    ImprimirUso();
    return 1;
}

var cliArgs = parseResult.Argumentos!;
if (cliArgs.Mode == CliMode.Help)
{
    ImprimirUso();
    return 0;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(diretorioBase)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

using var cts = new CtrlCCancellationSource();

var metadata = ExecutionMetadata.Criar(cliArgs.Mode, ResumirSelecaoDeProjeto(cliArgs), raizDoRepositorio);

try
{
    switch (cliArgs.Mode)
    {
        case CliMode.ValidateConfig:
            return ExecutarValidateConfig(configuration, metadata);

        case CliMode.ValidateConnections:
            return await ExecutarValidateConnectionsAsync(configuration, cliArgs, metadata, cts.Token);

        case CliMode.DryRun:
        case CliMode.LiveNoPersist:
        case CliMode.LivePersist:
            return await ExecutarAuditoriaAoVivoAsync(configuration, cliArgs, metadata, cts.Token);

        default:
            ImprimirUso();
            return 1;
    }
}
finally
{
    metadata.FinishedAtUtc = DateTime.UtcNow;
    metadata.Cancelled = cts.CancellationRequested;
}

// ============================================================================================
// Funções locais
// ============================================================================================

int ExecutarValidateConfig(IConfiguration config, ExecutionMetadata execMetadata)
{
    var resultado = ConfigSemanticValidator.Validar(config);

    Console.WriteLine($"[VALIDATE-CONFIG] AuditRunId={execMetadata.AuditRunId} | Modo={execMetadata.Mode}");
    foreach (var item in resultado.Itens)
    {
        string status = item.Presente ? "OK" : (item.Obrigatoria ? "AUSENTE (obrigatória)" : "ausente (opcional)");
        Console.WriteLine($"  {item.Chave,-55} {status,-25} {item.ResumoSeguro}");
    }

    Console.WriteLine(resultado.Sucesso
        ? "[VALIDATE-CONFIG] Configuração semanticamente completa."
        : "[VALIDATE-CONFIG] Configuração INCOMPLETA — preencha as chaves obrigatórias ausentes antes de tentar --validate-connections.");

    return resultado.Sucesso ? 0 : 3;
}

/// <summary>
/// Checkpoint 5, seção 2: cadeia REAL e ALCANÇÁVEL de <c>--validate-connections</c> —
/// ArgumentParser (já rodou antes de chegar aqui, e já exige seleção de projeto) -&gt; carregar configuração
/// (já rodou antes) -&gt; validar AuditSourceConnection -&gt; validar AuditRepositoryConnection -&gt; validar schema
/// audit por metadata -&gt; validar permissões por metadata -&gt; obter token APS -&gt; GET somente leitura no ACC
/// -&gt; relatório -&gt; fim. NUNCA chama <see cref="IOperationalAuditRunner"/> (seção 2: "Nunca, em hipótese
/// alguma, chamar OperationalAuditRunner dentro de --validate-connections"). Nenhum verbo SQL além de
/// SELECT/metadata sai daqui (<see cref="APS.WebApp.Services.OperationalAudit.Safety.SqlWriteVerbGuard"/>
/// dentro de <see cref="CliReadOnlySqlExecutor"/>), nenhum verbo HTTP além de GET/HEAD sai daqui
/// (<see cref="AuditHttpWriteGuardHandler"/> como handler mais externo do <c>HttpClient</c> do ACC).
/// </summary>
async Task<int> ExecutarValidateConnectionsAsync(IConfiguration config, CliArguments argumentos, ExecutionMetadata execMetadata, CancellationToken ct)
{
    string? projectId = argumentos.ProjectId;
    if (string.IsNullOrWhiteSpace(projectId))
    {
        Console.Error.WriteLine("[RECUSADO] --validate-connections exige --project-id explícito (seção 5: nunca --all-projects nesta primeira checagem).");
        return 1;
    }

    string? auditSourceConnectionString = config.GetConnectionString("AuditSourceConnection");
    string? auditRepositoryConnectionString = config.GetConnectionString("AuditRepositoryConnection");
    string? apsClientId = config["APS:ClientId"];
    string? apsClientSecret = config["APS:ClientSecret"];

    Console.WriteLine($"[VALIDATE-CONNECTIONS] AuditRunId={execMetadata.AuditRunId} | ProjectId={projectId}");
    Console.WriteLine();
    Console.WriteLine("AuditSource:");
    ImprimirResumoConexaoRedigido(auditSourceConnectionString);
    Console.WriteLine();
    Console.WriteLine("AuditRepository:");
    ImprimirResumoConexaoRedigido(auditRepositoryConnectionString);
    Console.WriteLine();
    Console.WriteLine("APS:");
    Console.WriteLine($"  ClientId presente = {(string.IsNullOrWhiteSpace(apsClientId) ? "NAO" : "SIM")}");
    Console.WriteLine($"  ClientSecret presente = {(string.IsNullOrWhiteSpace(apsClientSecret) ? "NAO" : "SIM")}");
    Console.WriteLine("  Token = NUNCA EXIBIR");
    Console.WriteLine();

    var services = new ServiceCollection();
    services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
    services.AddOperationalAudit(config);
    services.AddScoped<IAccTokenProvider, TokenManagerAccTokenProvider>();
    services.AddHttpClient<IAccReadOnlyConnectivityValidator, AccReadOnlyConnectivityValidator>()
        .AddHttpMessageHandler<AuditHttpWriteGuardHandler>();
    await using var provider = services.BuildServiceProvider();

    var auditSourceExecutor = new CliReadOnlySqlExecutor(auditSourceConnectionString ?? string.Empty);
    var auditRepositoryExecutor = new CliReadOnlySqlExecutor(auditRepositoryConnectionString ?? string.Empty);
    var accValidator = provider.GetRequiredService<IAccReadOnlyConnectivityValidator>();

    var relatorio = await ConnectionValidator.ValidarTudoAsync(
        auditSourceConnectionString, auditRepositoryConnectionString,
        auditSourceExecutor, auditRepositoryExecutor, accValidator, projectId, ct);

    Console.WriteLine("[VALIDATE-CONNECTIONS] Resultado:");
    Console.WriteLine($"  AUDIT_SOURCE_SQL     = {relatorio.AuditSourceSql}");
    Console.WriteLine($"  AUDIT_REPOSITORY_SQL = {relatorio.AuditRepositorySql}");
    Console.WriteLine($"  APS_AUTH             = {relatorio.ApsAuth}");
    Console.WriteLine($"  ACC_PROJECT_READ     = {relatorio.AccProjectRead}");
    Console.WriteLine();

    if (!relatorio.TudoOk)
    {
        Console.Error.WriteLine("[VALIDATE-CONNECTIONS][PARADO] Seção 7: ao menos uma das quatro checagens falhou — a auditoria NAO deve ser executada. Investigue a configuração antes de tentar novamente. Nenhuma escrita foi tentada.");
        return 5;
    }

    var schema = await SchemaReadOnlyValidator.ValidarAsync(auditRepositoryExecutor, ct);
    if (!string.IsNullOrEmpty(schema.Erro))
    {
        Console.Error.WriteLine($"[VALIDATE-CONNECTIONS][ERRO] Falha ao consultar metadata do schema audit: {schema.Erro}");
        Console.Error.WriteLine("[VALIDATE-CONNECTIONS][PARADO] A auditoria NAO deve ser executada.");
        return 6;
    }

    int tabelasEsperadas = schema.Objetos.Count(o => o.Tipo == "TABLE");
    int tabelasEncontradas = schema.Objetos.Count(o => o.Tipo == "TABLE" && o.Existe);
    int viewsEsperadas = schema.Objetos.Count(o => o.Tipo == "VIEW");
    int viewsEncontradas = schema.Objetos.Count(o => o.Tipo == "VIEW" && o.Existe);

    Console.WriteLine($"[VALIDATE-CONNECTIONS] Schema audit: {tabelasEncontradas}/{tabelasEsperadas} tabelas | {viewsEncontradas}/{viewsEsperadas} views");
    foreach (var objeto in schema.Objetos)
    {
        Console.WriteLine($"  {(objeto.Existe ? "OK" : "AUSENTE")}  {objeto.Tipo,-6} {objeto.NomeCompleto}");
    }
    Console.WriteLine();

    var permissoesOrigem = await PermissionReadOnlyValidator.ValidarPermissoesDeBancoAsync(auditSourceExecutor, ct);
    Console.WriteLine($"[VALIDATE-CONNECTIONS] Permissões AuditSourceConnection: SELECT={permissoesOrigem.PodeSelect} INSERT={permissoesOrigem.PodeInsert} UPDATE={permissoesOrigem.PodeUpdate} DELETE={permissoesOrigem.PodeDelete}");
    if (permissoesOrigem.TemPermissaoAlemDoNecessarioParaLeitura)
    {
        Console.WriteLine("  [ATENCAO] Seção 8: AuditSourceConnection tem permissão além de SELECT — reportado aqui, NUNCA corrigido automaticamente por este CLI.");
    }

    var permissoesRepositorio = await PermissionReadOnlyValidator.ValidarPermissoesDeBancoAsync(auditRepositoryExecutor, ct);
    Console.WriteLine($"[VALIDATE-CONNECTIONS] Permissões AuditRepositoryConnection: SELECT={permissoesRepositorio.PodeSelect} INSERT={permissoesRepositorio.PodeInsert} UPDATE={permissoesRepositorio.PodeUpdate} DELETE={permissoesRepositorio.PodeDelete}");
    Console.WriteLine();

    if (!schema.TodosPresentes)
    {
        Console.Error.WriteLine("[VALIDATE-CONNECTIONS][PARADO] Seção 7: nem todos os objetos do schema audit foram encontrados — a auditoria NAO deve ser executada. O schema é de responsabilidade manual do usuário; este CLI nunca cria/altera/executa OperationalAuditSchema.sql.");
        return 6;
    }

    Console.WriteLine("[VALIDATE-CONNECTIONS] Todas as checagens OK e schema completo. Nenhuma escrita foi realizada (nem SQL, nem ACC) — nenhuma execução de auditoria foi disparada neste modo.");
    return 0;
}

void ImprimirResumoConexaoRedigido(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.WriteLine("  Server=<não configurada>");
        Console.WriteLine("  Database=<não configurada>");
        Console.WriteLine("  Credentials=<REDACTED>");
        return;
    }

    try
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        Console.WriteLine($"  Server={builder.DataSource}");
        Console.WriteLine($"  Database={builder.InitialCatalog}");
        Console.WriteLine("  Credentials=<REDACTED>");
    }
    catch
    {
        // Nunca ecoa a string original quando o parse falha — o valor bruto poderia conter credenciais.
        Console.WriteLine("  Server=<formato não reconhecido para resumo seguro>");
        Console.WriteLine("  Database=<formato não reconhecido para resumo seguro>");
        Console.WriteLine("  Credentials=<REDACTED>");
    }
}

/// <summary>
/// Checkpoint 4.1 (verificação final, seção 2): cadeia REAL e ALCANÇÁVEL —
/// ArgumentParser (já rodou antes de chegar aqui) -&gt; CliModeOptionsApplier.Aplicar -&gt;
/// CliModeOptionsApplier.GarantirHabilitadoParaExecucao -&gt; IOperationalAuditRunner.ExecutarAsync — nesta
/// ORDEM exata, nunca invertida, nunca com código morto entre elas. <see cref="ProtectedFilesList"/> e os
/// 13 arquivos protegidos nunca são referenciados aqui. Ninguém neste repositório invocou este caminho
/// contra infraestrutura real ainda (nenhum "dotnet run -- --dry-run/--run" foi executado) — só compilado
/// e coberto por teste de contrato de texto-fonte (<c>ProgramCsContractTests</c>).
/// </summary>
async Task<int> ExecutarAuditoriaAoVivoAsync(IConfiguration config, CliArguments argumentos, ExecutionMetadata execMetadata, CancellationToken ct)
{
    var services = new ServiceCollection();
    services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
    services.AddOperationalAudit(config);
    await using var provider = services.BuildServiceProvider();

    var options = provider.GetRequiredService<OperationalAuditOptions>();
    CliModeOptionsApplier.Aplicar(options, argumentos);
    CliModeOptionsApplier.GarantirHabilitadoParaExecucao(options);

    string diretorioBaseRelatorios = AuditReportPathResolver.ResolverDiretorioBase(raizDoRepositorio);
    string subpastaDaExecucao = AuditReportPathResolver.ResolverSubpastaDaExecucao(diretorioBaseRelatorios, execMetadata.AuditRunId, execMetadata.StartedAtUtc);
    options.OutputDirectory = subpastaDaExecucao;

    Console.WriteLine($"[{execMetadata.Mode}] AuditRunId={execMetadata.AuditRunId} | Projetos={ResumirSelecaoDeProjeto(argumentos)} | PersistToDatabase={options.PersistToDatabase} | Saida={subpastaDaExecucao}");

    var runner = provider.GetRequiredService<IOperationalAuditRunner>();
    var resultado = await runner.ExecutarAsync(argumentos.ProjectIds(), triggeredBy: "NA_Connect.OperationalAudit.Cli", ct);

    Console.WriteLine($"[{execMetadata.Mode}] Status={resultado.Status} | Findings={resultado.Findings.Count} | Requisitos={resultado.Requirements.Count} | PersistenciaSqlFalhou={resultado.PersistenciaSqlFalhou}");
    foreach (var erro in resultado.ErrosDeExecucao)
    {
        Console.Error.WriteLine($"[{execMetadata.Mode}][ERRO] {erro}");
    }

    return resultado.ErrosDeExecucao.Count == 0 ? 0 : 4;
}

string ResumirSelecaoDeProjeto(CliArguments a) =>
    a.AllProjects ? "TODOS OS PROJETOS (--all-projects --confirm-all-projects)" : a.ProjectId ?? "(nenhum)";

string LocalizarRaizDoRepositorio(string partidaDir)
{
    var dir = new DirectoryInfo(partidaDir);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccIntegration.WebApp.sln")))
    {
        dir = dir.Parent;
    }
    return dir?.FullName ?? partidaDir;
}

void ImprimirUso()
{
    Console.WriteLine(@"
NA_Connect.OperationalAudit.Cli — executor manual da Auditoria Operacional (Checkpoint 4)

Uso:
  dotnet run --project Tools\NA_Connect.OperationalAudit.Cli -- [flags]

Modos (escolha exatamente um):
  --validate-config          Só valida presença semântica de configuração. Nunca abre conexão.
  --validate-connections     Testa abertura de conexões (SELECT/metadata), autenticação APS e GET/HEAD ao
                              projeto ACC selecionado. Nunca escreve.
  --dry-run                  Leitura REAL de ACC/SQL — exige --allow-live-read (igual a --run).
                              PersistToDatabase sempre false, gera TXT/CSV/JSON, nunca grava audit.*.
  --run                      Execução real — exige --allow-live-read.

Modificadores de --run/--dry-run:
  --allow-live-read           Obrigatório para --run OU --dry-run produzirem efeito (os dois leem ACC/SQL reais).
  --persist                   Só junto de --run --allow-live-read: além disso, grava em audit.* (nunca em
                              tabela operacional). Nunca válido junto de --dry-run.

Seleção de projeto (obrigatória para todo modo exceto --validate-config):
  --project-id <ProjectIdAcc> Um único projeto.
  --all-projects --confirm-all-projects   Todos os projetos — as DUAS flags são obrigatórias juntas.

Exemplos:
  dotnet run --project Tools\NA_Connect.OperationalAudit.Cli -- --validate-config
  dotnet run --project Tools\NA_Connect.OperationalAudit.Cli -- --project-id ""<PROJECT_ID>"" --run --allow-live-read
");
}
