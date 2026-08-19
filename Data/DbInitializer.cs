using CMIS_IyaSoft.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMIS_IyaSoft.Data;

public static class DbInitializer
{
    public static void Initialize(AppDbContext context)
    {
        // Ensure SQL Server database is created
        context.Database.EnsureCreated();

        if (!context.Types.Any())
        {
            context.Types.AddRange(
                new CmisType { Id = "cmis:folder", BaseId = "cmis:folder", DisplayName = "Folder", Description = "CMIS Folder Type" },
                new CmisType { Id = "cmis:document", BaseId = "cmis:document", DisplayName = "Document", Description = "CMIS Document Type" }
            );
            context.SaveChanges();
        }

        if (!context.Objects.Any())
        {
            var rootFolder = new CmisObject
            {
                Id = "root-folder",
                Name = "Root",
                TypeId = "cmis:folder",
                ParentId = null,
                Path = "/"
            };

            var sampleDoc = new CmisObject
            {
                Id = "doc-101",
                Name = "Welcome.txt",
                TypeId = "cmis:document",
                ParentId = "root-folder",
                Path = "/Welcome.txt",
                MimeType = "text/plain",
                ContentStream = System.Text.Encoding.UTF8.GetBytes("Welcome to MiniCMIS API Server!"),
                ContentStreamLength = 31
            };

            context.Objects.AddRange(rootFolder, sampleDoc);
            context.SaveChanges();
        }

        if (!context.TypePropertyDefinitions.Any())
        {
            // One demo custom property per type, proving the mechanism end to end.
            // Add more rows here (or expose an admin endpoint later) as real needs show up.
            context.TypePropertyDefinitions.AddRange(
                new TypePropertyDefinition
                {
                    TypeId = "cmis:document",
                    PropertyId = "custom:department",
                    LocalName = "department",
                    PropertyType = "string",
                    Cardinality = "single",
                    Updatability = "readwrite",
                    Required = false
                },
                new TypePropertyDefinition
                {
                    TypeId = "cmis:folder",
                    PropertyId = "custom:owner",
                    LocalName = "owner",
                    PropertyType = "string",
                    Cardinality = "single",
                    Updatability = "readwrite",
                    Required = false
                }
            );
            context.SaveChanges();
        }
    }

    /// <summary>
    /// Additive-only: creates ObjectProperties and TypePropertyDefinitions if they
    /// don't exist yet. Safe to call on every startup - never touches Objects,
    /// Types, or Identity tables. Needed for Postgres/prod, which bootstraps via
    /// EnsureCreated instead of migrations (see MigrateAsync path for SQL Server,
    /// which picks these up through a normal EF migration instead).
    /// </summary>
    public static async Task EnsureCustomPropertyTablesAsync(AppDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(ObjectPropertiesTableSql);
        await context.Database.ExecuteSqlRawAsync(TypePropertyDefinitionsTableSql);
    }

    private const string ObjectPropertiesTableSql = @"
        CREATE TABLE IF NOT EXISTS ""ObjectProperties"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""ObjectId"" TEXT NOT NULL,
            ""PropertyId"" TEXT NOT NULL,
            ""PropertyType"" TEXT NOT NULL,
            ""Cardinality"" TEXT NOT NULL,
            ""Value"" TEXT NOT NULL,
            ""SortOrder"" INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_objectproperties_objectid ON ""ObjectProperties"" (""ObjectId"");
    ";

    private const string TypePropertyDefinitionsTableSql = @"
        CREATE TABLE IF NOT EXISTS ""TypePropertyDefinitions"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""TypeId"" TEXT NOT NULL,
            ""PropertyId"" TEXT NOT NULL,
            ""LocalName"" TEXT NOT NULL,
            ""PropertyType"" TEXT NOT NULL,
            ""Cardinality"" TEXT NOT NULL,
            ""Updatability"" TEXT NOT NULL,
            ""Required"" BOOLEAN NOT NULL DEFAULT FALSE
        );
        CREATE INDEX IF NOT EXISTS ix_typepropertydefinitions_typeid ON ""TypePropertyDefinitions"" (""TypeId"");
    ";
}