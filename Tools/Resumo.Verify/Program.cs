using System.Text.Json;
using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Reports;
using APS.WebApp.Services.Reports.Dashboards;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.Services.Coordination;

// Local read-only verification. No application host, workers, HTTP, mutations or Autodesk calls.
var root=Directory.GetCurrentDirectory();
var config=new ConfigurationBuilder().AddJsonFile(Path.Combine(root,"appsettings.json")).Build();
using var connection=new SqlConnection(config.GetConnectionString("LocalConnection"));
await connection.OpenAsync();
using var cmd=new SqlCommand("SELECT Project_ID_ACC FROM dbo.Projetos WHERE Is_Ativo=1 AND (Nome_Projeto LIKE '%BML011.016%' OR Tipo_LDP=2)",connection);
var ids=new List<string>();
using(var reader=await cmd.ExecuteReaderAsync())while(await reader.ReadAsync())ids.Add(reader.GetString(0));
var planning=new PmoPlanningFactsProvider(config);
var actual=new PmoActualFactsProvider(config,new CoordinationStandardRepository(config,NullLogger<CoordinationStandardRepository>.Instance));
var repo=new GestaoVistaReportRepository(config,planning,actual);
var reports=new List<GestaoVistaReport>();
var dashboards=new Dictionary<string,DashboardResult>();
var output=Path.Combine(root,"_build_verify","resumo");Directory.CreateDirectory(output);
foreach(var id in ids){
  var report=await repo.ReadAsync(id,default);
  // Constructor is the preexisting consolidated service; the phase reader adds no changes to these fields.
  var phaseBefore=JsonSerializer.Serialize(report.Documentos);
  _=ResumoPhaseRules.Read(report.TipoLdp,report.DataReferencia,report.Documentos);
  if(phaseBefore!=JsonSerializer.Serialize(report.Documentos))throw new Exception("Phase reader mutated official documents");
  if(report.HhRealizado.Total!=report.Actual.Total.Hh || report.HhRealizado.CustoTotal!=report.Actual.Total.KnownCalculatedCostSubtotal)
    throw new Exception("Consolidated official totals changed");
  reports.Add(report);
  var definition=ProductionDashboards.Todos["ldp"];
  dashboards[id]=DashboardEngine.Avaliar(definition,await new ProductionDashboardRepository(config).ReadRowsAsync(definition,id,default),new Dictionary<string,IReadOnlySet<string>>(),1,1);
  Console.WriteLine($"{report.NomeProjeto}: Tipo {report.TipoLdp}; HH previsto={report.Planning.PlannedHhTotal}; HH realizado={report.HhRealizado.Total}; Custo previsto={report.Planning.DocumentPlannedCost}; Custo realizado={report.HhRealizado.CustoTotal}; docs={report.Documentos.Count(d=>d.IsDocument)}; gates={report.ResumoFases.Fases.Count}; historico={report.Actual.HistoricalMeasurement?.ComplementHours}");
}
await File.WriteAllTextAsync(Path.Combine(output,"reports.json"),JsonSerializer.Serialize(reports,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true}));
await File.WriteAllTextAsync(Path.Combine(output,"dashboards.json"),JsonSerializer.Serialize(dashboards,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true}));
Console.WriteLine("Official totals and immutable phase read verified. Payload: _build_verify/resumo/reports.json");
