using CMIS_IyaSoft.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CMIS_IyaSoft.Data;

public class AppDbContext : IdentityDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<CmisObject> Objects { get; set; }

    public DbSet<CmisType> Types { get; set; }

    public DbSet<ObjectProperty> ObjectProperties { get; set; }

    public DbSet<TypePropertyDefinition> TypePropertyDefinitions { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // CMIS type inheritance:
        // one type can inherit from another type.
        builder.Entity<CmisType>()
            .HasOne(t => t.ParentType)
            .WithMany(t => t.ChildTypes)
            .HasForeignKey(t => t.ParentTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // A type cannot have itself as its parent at DB/application level.
        builder.Entity<CmisType>()
            .HasIndex(t => t.ParentTypeId);

        // TypePropertyDefinition belongs to a CMIS type.
        builder.Entity<TypePropertyDefinition>()
            .HasIndex(p => p.TypeId);

        builder.Entity<ObjectProperty>()
            .HasIndex(p => p.ObjectId);
    }
}