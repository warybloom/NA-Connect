using System.Text.Json;
using APS.WebApp.Services.GestaoVista;
using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Reports;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// Read-only harness. No Host/Application startup, worker, sync, SQL mutation or ACC call.
string? Argument(string name) { var i=Array.IndexOf(args,name); return i>=0 && i+1<args.Length ? args[i+1] : null; }

if (args.Length > 0 && args[0] == "--check-schema")
{
    var cfgPath = Path.GetFullPath(Argument("--config") ?? "appsettings.json");
    var cfg = new ConfigurationBuilder().AddJsonFile(cfgPath).Build();
    using var conn = new SqlConnection(cfg.GetConnectionString("LocalConnection"));
    await conn.OpenAsync();
    var repo = new GestaoVistaRepository(new ConsoleLogger<GestaoVistaRepository>());
    var disponivel = await repo.SchemaDisponivelAsync(conn);
    Console.WriteLine($"SchemaDisponivelAsync retornou: {disponivel}");
    return;
}

var id=Argument("--project-id");
if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Use --project-id <Project_ID_ACC> [--config appsettings.json] [--output file.json], ou --check-schema [--config appsettings.json]");
var configPath=Path.GetFullPath(Argument("--config") ?? "appsettings.json");
var configuration=new ConfigurationBuilder().AddJsonFile(configPath).Build();
var planning=new PmoPlanningFactsProvider(configuration);
var actual=new PmoActualFactsProvider(configuration);
var report=await new GestaoVistaReportRepository(configuration,planning,actual).ReadAsync(id,default);
var payload=new {
    report.ProjectId,report.NomeProjeto,report.TipoLdp,report.Planning,report.Actual,
    Consumo=report.Planning.PlannedHhTotal>0 ? report.Actual.Total.Hh/report.Planning.PlannedHhTotal*100 : null,
    Documentos=new {Total=report.Documentos.Count,Emitidos=report.Documentos.Count(d=>d.Emitido),
        NoPrazo=report.Documentos.Count(d=>d.StatusPrazo=="NO_PRAZO"),Atrasados=report.Documentos.Count(d=>d.StatusPrazo=="ATRASADO"),
        Cancelados=report.Documentos.Count(d=>d.Cancelado),SemPrazo=report.Documentos.Count(d=>d.StatusPrazo=="SEM_DADO"),
        ComFisico=report.Documentos.Count(d=>d.AvancoFisico.HasValue)},Weekly=report.WeeklyAtividades.Count
};
var json=JsonSerializer.Serialize(payload,new JsonSerializerOptions {WriteIndented=true});
if (Argument("--output") is { } output) { await File.WriteAllTextAsync(Path.GetFullPath(output),json); Console.WriteLine("Auditoria somente leitura gravada: "+Path.GetFullPath(output)); }
else Console.WriteLine(json);

// Minimal ILogger writing to Console — avoids adding a package reference just for this ad-hoc harness.
sealed class ConsoleLogger<T> : ILogger<T>
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Console.WriteLine($"[{logLevel}] {formatter(state, exception)}");
    sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
}
