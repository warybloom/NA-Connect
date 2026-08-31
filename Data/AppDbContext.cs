using Microsoft.EntityFrameworkCore;
using APS.WebApp.Models;

namespace APS.WebApp.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Usuario> Usuarios { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Usuario>(entity =>
            {
                entity.ToTable("Usuarios", schema: "dbo");
                entity.HasKey(e => e.UserID);
            });
        }
    }
}