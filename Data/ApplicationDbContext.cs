using Microsoft.EntityFrameworkCore;
using GPIMSWebServer.Models;

namespace GPIMSWebServer.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Username).IsUnique();
                entity.Property(e => e.Role)
                    .HasConversion<string>();
            });

            // 🔧 시드 데이터 제거 - Program.cs에서 처리하므로 불필요
            // HasData는 마이그레이션과 충돌할 수 있으므로 제거
        }
    }
}