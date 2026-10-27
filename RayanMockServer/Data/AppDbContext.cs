using Microsoft.EntityFrameworkCore;
using RayanMockServer.Models;

namespace RayanMockServer.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<MockApiEndpoint> MockApiEndpoints => Set<MockApiEndpoint>();
    public DbSet<GlobalVariable> GlobalVariables => Set<GlobalVariable>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MockApiEndpoint>(entity =>
        {
            entity.ToTable("MockApiEndpoints");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Title).HasMaxLength(300).IsRequired();
            entity.Property(e => e.HttpMethod).HasMaxLength(15).IsUnicode(false).IsRequired();
            entity.Property(e => e.UrlPath).HasMaxLength(500).IsRequired();
            entity.Property(e => e.CategoryTag).HasMaxLength(100).IsRequired();
            entity.Property(e => e.SuccessResponseJson).IsRequired();
            entity.Property(e => e.DefaultStatusCode).HasDefaultValue(200);
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasIndex(e => new { e.HttpMethod, e.UrlPath, e.IsActive })
                  .HasDatabaseName("IX_MockApiEndpoints_Lookup");

            entity.HasIndex(e => e.IndexNumber)
                  .HasDatabaseName("IX_MockApiEndpoints_IndexNumber");
        });

        modelBuilder.Entity<GlobalVariable>(entity =>
        {
            entity.ToTable("GlobalVariables");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Key).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Value).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(250);

            entity.HasIndex(e => e.Key).IsUnique()
                  .HasDatabaseName("IX_GlobalVariables_Key");
        });
    }
}
