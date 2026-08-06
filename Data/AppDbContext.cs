using Microsoft.EntityFrameworkCore;
using CMIS_IyaSoft.Entities;

namespace CMIS_IyaSoft.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<CmisType> Types => Set<CmisType>();
    public DbSet<CmisObject> Objects => Set<CmisObject>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Self-Referencing Relationship
        modelBuilder.Entity<CmisObject>()
            .HasOne(o => o.Parent)
            .WithMany(o => o.Children)
            .HasForeignKey(o => o.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}