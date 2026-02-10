using APS.WebApp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using OfficeOpenXml;
using Microsoft.EntityFrameworkCore;
using AccIntegration.WebApp.Data;
using System.Data; 
using NA_Connect.Services; 

var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
var builder = WebApplication.CreateBuilder(args);

// ===================================================================
// ✅ LOG DE INICIALIZAÇÃO
// ===================================================================
Console.WriteLine($"\n\n--- APLICAÇÃO INICIADA COM O CÓDIGO MAIS RECENTE --- (Build de {DateTime.Now}) ---\n\n");

// ===================================================================
// ✅ CONFIGURAÇÃO DE SERVIÇOS (DI Container)
// ===================================================================

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
                      policy =>
                      {
                          policy.WithOrigins("http://localhost:5173") 
                                .AllowAnyHeader()
                                .AllowAnyMethod()
                                .AllowCredentials();
                      });
});

builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpClient();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
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
builder.Services.AddScoped<PdfMetadataService>();
builder.Services.AddScoped<GrdToLdpSyncService>();
builder.Services.AddScoped<LdpUpdateService>();
// No seu arquivo de configuração de serviços:
builder.Services.AddScoped<ProjectAutoDiscoveryService>();

// Registros para a Automação N&A Connect
builder.Services.AddScoped<DatabaseService>(); 
builder.Services.AddScoped<AccProjectSyncService>(); // ✅ Registrado aqui

builder.Services.AddHostedService<GrdAutomationWorker>();

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
app.UseRouting();
app.UseCors(MyAllowSpecificOrigins);
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllers();

// ===================================================================
// ✅ BLOCO DE TESTE E SINCRONIZAÇÃO (Antes do Run)
// ===================================================================
using (var scope = app.Services.CreateScope())
{
    var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
    var syncService = scope.ServiceProvider.GetRequiredService<AccProjectSyncService>(); // ✅ Captura o serviço
    
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

            // ✅ ADICIONADO AQUI: Inicia a sincronização real com o ACC
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


var db = new DatabaseService();

// Vamos dar uma identidade N&A para o projeto da UNIPAR
// O ID 'b.90621daf...' eu peguei do seu log anterior
db.SalvarMetadado("b.90621daf-972c-4291-af87-04843e717ec9", "UNP001", "Projeto Unipar - Gestão N&A");

Console.WriteLine(">>> TESTE DE METADADO CONCLUÍDO!");
// ===================================================================

app.Run();