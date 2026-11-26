// BI CHS001 — sincronização isolada do cronograma (somente schema bi_chs001).
// Uso:
//   BiChs001.Sync acc     [--config <pasta com appsettings.json>] [--simular]   → baixa cópia do .mpp mais recente do ACC
//   BiChs001.Sync arquivo <caminho.mpp> [--config ...] [--simular]              → lê uma cópia local
//   --simular  = só lê e mostra o diagnóstico; não grava nada.
//   --banco <nome> = grava em outro banco (ex.: clone de teste) em vez do da connection string.
//   --guardar-copia <pasta> = guarda a cópia baixada do ACC (evidência/auditoria), nomeada pelo SHA-256.
using System.Text.Json;
using APS.WebApp.Services;
using APS.WebApp.Services.BiChs001Schedule;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NA_Connect.Services;

if (args.Length == 0 || (args[0] != "acc" && args[0] != "arquivo")) { Console.Error.WriteLine("uso: acc | arquivo <caminho.mpp> [--config <pasta>] [--simular] [--banco <nome>]"); return 2; }
string Opcao(string nome, string padrao) { int i = Array.IndexOf(args, nome); return i >= 0 && i + 1 < args.Length ? args[i + 1] : padrao; }
bool simular = args.Contains("--simular");
string pastaConfig = Opcao("--config", @"C:\Dev\N-A-Connect aa\N-A-Connect");

var configuration = new ConfigurationBuilder()
    .SetBasePath(pastaConfig)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets("na-connect-webapp-7d3c1e52-6a0b-4f3e-9c41-2b8e5f0a9d17")
    .AddEnvironmentVariables()
    .Build();

string conexao = configuration.GetConnectionString("LocalConnection") ?? throw new InvalidOperationException("ConnectionStrings:LocalConnection ausente.");
string banco = Opcao("--banco", "");
if (banco.Length > 0) conexao = new SqlConnectionStringBuilder(conexao) { InitialCatalog = banco }.ConnectionString;

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("BiChs001.Sync");
var repo = new BiChs001ScheduleRepository(conexao);

var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var api = new AutodeskApiService(http, new TokenManager(new HttpClient(), configuration, new MemoryCache(new MemoryCacheOptions())), configuration,
    loggerFactory.CreateLogger<AutodeskApiService>(), new DocumentMatchingService());
var servico = new BiChs001ScheduleSyncService(api, repo, logger);

string executor = $"{Environment.UserName} (BiChs001.Sync)";
var resultado = args[0] == "acc"
    ? await servico.SincronizarDoAccAsync(BiChs001Escopo.ProjectIdChs001, executor, gravar: !simular, CancellationToken.None,
        Opcao("--guardar-copia", "") is { Length: > 0 } pasta ? pasta : null)
    : await servico.SincronizarDeArquivoAsync(BiChs001Escopo.ProjectIdChs001, args[1], executor, gravar: !simular, CancellationToken.None);

Console.WriteLine(JsonSerializer.Serialize(resultado, new JsonSerializerOptions { WriteIndented = true }));
return resultado.Sucesso ? 0 : 1;
