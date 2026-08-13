using Microsoft.EntityFrameworkCore;
using AccIntegration.WebApp.Models;

namespace AccIntegration.WebApp.Data
{
    // Este contexto gerencia apenas as SUAS tabelas no ForgeSync
    public class ConnectDbContext : DbContext
    {
        public ConnectDbContext(DbContextOptions<ConnectDbContext> options) : base(options)
        {
        }

        // A sua tabela de automação
        public DbSet<MetadadosProjeto> MetadadosProjetos { get; set; }
        
        // Dica: Futuramente, suas tabelas de OCR também podem vir para cá
    }
}