using CMIS_IyaSoft.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CMIS_IyaSoft.Data;

public class AppDbContext : IdentityDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<CmisObject> Objects { get; set; }

    // ➕ Ajoutez cette ligne pour les types CMIS :
    public DbSet<CmisType> Types { get; set; }
}