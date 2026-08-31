#nullable disable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccIntegration.WebApp.Models; 
using AccIntegration.WebApp.Data; 
using APS.WebApp.Services; // ✅ Namespace onde está o seu GrdAutomationService

namespace AccIntegration.WebApp.Services
{
    public class SyncWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public SyncWorker(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("🤖 Robô Forge Sync: Monitoramento de ALTA PRECISÃO iniciado (Checagem a cada 20s).");
            
            string ultimoMinutoProcessado = ""; 

            while (!stoppingToken.IsCancellationRequested)
            {
                try 
                {
                    var agora = DateTime.Now.ToString("HH:mm");
                    
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<ConnectDbContext>();

                        // Só entra se for um minuto novo que ainda não foi rodado
                        if (agora != ultimoMinutoProcessado)
                        {
                            var projetosParaSincronizar = await context.MetadadosProjetos
                                .Where(p => p.HorariosSync != null && p.HorariosSync.Contains(agora))
                                .ToListAsync(stoppingToken);

                            if (projetosParaSincronizar.Any())
                            {
                                foreach (var projeto in projetosParaSincronizar)
                                {
                                    // 🚀 CHAMA O MOTOR DE AUTOMAÇÃO
                                    await ExecutarTarefaAutodesk(projeto, scope);
                                }
                                
                                // Trava para não repetir no mesmo minuto (visto que checamos a cada 20s)
                                ultimoMinutoProcessado = agora;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO ROBÔ] Falha na verificação de agendamento: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            }
        }

        private async Task ExecutarTarefaAutodesk(MetadadosProjeto projeto, IServiceScope scope)
        {
            // Define o nome amigável para o Log
            string nomeParaExibir = projeto.Descricao_Corporativa ?? projeto.Nome_LDP ?? "Projeto Sem Nome";

            Console.WriteLine($"\n=======================================================");
            Console.WriteLine($"[AUTOMAÇÃO DISPARADA] ⏰ {DateTime.Now:HH:mm:ss} | Projeto: {nomeParaExibir}");
            Console.WriteLine($"[INFO] MODO DE TESTE: Iniciando Sincronismo Isolado do Cronograma...");
            Console.WriteLine($"=======================================================\n");
            
            try
            {
                // 🛠️ ACIONANDO O MOTOR REAL (GrdAutomationService)
                var grdService = scope.ServiceProvider.GetRequiredService<GrdAutomationService>();

                // 🎯 ALTERAÇÃO CRÍTICA REALIZADA AQUI:
                // Trocamos a chamada do método 'ExecuteFullProjectSyncAsync' pelo método de teste isolado
                // 'ExecuteOnlyScheduleSyncAsync' que criamos na sua Service. Isso impede o robô de rodar 
                // as etapas pesadas de OCR, Custo e GRD, focando 100% no debug do arquivo .mpp.
                await grdService.ExecuteOnlyScheduleSyncAsync(
                    projeto.Project_ID_ACC, 
                    projeto.Account_ID_ACC, 
                    nomeParaExibir
                );

                Console.WriteLine($"\n✅ [SUCESSO] O Robô finalizou o teste isolado de cronograma para {nomeParaExibir}!\n");
            }
            catch(Exception ex)
            {
                Console.WriteLine($"\n❌ [ERRO CRÍTICO] O motor de automação isolado falhou para {nomeParaExibir}: {ex.Message}\n");
            }
        }
    }
}