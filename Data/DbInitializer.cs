using CMIS_IyaSoft.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMIS_IyaSoft.Data;

public static class DbInitializer
{
    public static void Initialize(AppDbContext context)
    {
        context.Database.EnsureCreated();

        // =========================================================
        // BASE CMIS TYPES
        // =========================================================

        if (!context.Types.Any(t => t.Id == "cmis:folder"))
        {
            context.Types.Add(new CmisType
            {
                Id = "cmis:folder",
                BaseId = "cmis:folder",
                DisplayName = "Folder",
                Description = "CMIS Folder Type",
                ParentTypeId = null
            });
        }

        if (!context.Types.Any(t => t.Id == "cmis:document"))
        {
            context.Types.Add(new CmisType
            {
                Id = "cmis:document",
                BaseId = "cmis:document",
                DisplayName = "Document",
                Description = "CMIS Document Type",
                ParentTypeId = null
            });
        }

        context.SaveChanges();


        // =========================================================
        // CUSTOM TYPE HIERARCHY
        //
        // cmis:document
        //      |
        //      └── custom:financialDocument
        //              |
        //              ├── custom:facture
        //              └── custom:loan
        // =========================================================

        if (!context.Types.Any(t => t.Id == "custom:financialDocument"))
        {
            context.Types.Add(new CmisType
            {
                Id = "custom:financialDocument",
                BaseId = "cmis:document",
                DisplayName = "Financial Document",
                Description = "Base type for financial documents",
                ParentTypeId = "cmis:document"
            });

            context.SaveChanges();
        }

        if (!context.Types.Any(t => t.Id == "custom:facture"))
        {
            context.Types.Add(new CmisType
            {
                Id = "custom:facture",
                BaseId = "cmis:document",
                DisplayName = "Facture",
                Description = "Invoice document type",
                ParentTypeId = "custom:financialDocument"
            });
        }

        if (!context.Types.Any(t => t.Id == "custom:loan"))
        {
            context.Types.Add(new CmisType
            {
                Id = "custom:loan",
                BaseId = "cmis:document",
                DisplayName = "Loan",
                Description = "Loan document type",
                ParentTypeId = "custom:financialDocument"
            });
        }

        context.SaveChanges();


        // =========================================================
        // FINANCIAL DOCUMENT PROPERTIES
        // inherited by Facture + Loan
        // =========================================================

        AddPropertyIfMissing(
            context,
            new TypePropertyDefinition
            {
                TypeId = "custom:financialDocument",
                PropertyId = "custom:amount",
                LocalName = "amount",
                PropertyType = "integer",
                Cardinality = "single",
                Updatability = "readwrite",
                Required = false
            });

        AddPropertyIfMissing(
            context,
            new TypePropertyDefinition
            {
                TypeId = "custom:financialDocument",
                PropertyId = "custom:currency",
                LocalName = "currency",
                PropertyType = "string",
                Cardinality = "single",
                Updatability = "readwrite",
                Required = false
            });


        // =========================================================
        // FACTURE PROPERTIES
        // =========================================================

        AddPropertyIfMissing(
            context,
            new TypePropertyDefinition
            {
                TypeId = "custom:facture",
                PropertyId = "custom:invoiceNumber",
                LocalName = "invoiceNumber",
                PropertyType = "string",
                Cardinality = "single",
                Updatability = "readwrite",
                Required = true
            });

        AddPropertyIfMissing(
            context,
            new TypePropertyDefinition
            {
                TypeId = "custom:facture",
                PropertyId = "custom:invoiceDate",
                LocalName = "invoiceDate",
                PropertyType = "datetime",
                Cardinality = "single",
                Updatability = "readwrite",
                Required = false
            });


        // =========================================================
        // LOAN PROPERTIES
        // =========================================================

        AddPropertyIfMissing(
            context,
            new TypePropertyDefinition
            {
                TypeId = "custom:loan",
                PropertyId = "custom:interestRate",
                LocalName = "interestRate",
                PropertyType = "integer",
                Cardinality = "single",
                Updatability = "readwrite",
                Required = false
            });

        AddPropertyIfMissing(
            context,
            new TypePropertyDefinition
            {
                TypeId = "custom:loan",
                PropertyId = "custom:duration",
                LocalName = "duration",
                PropertyType = "integer",
                Cardinality = "single",
                Updatability = "readwrite",
                Required = false
            });


        // =========================================================
        // FOLDER PROPERTY
        // =========================================================

        AddPropertyIfMissing(
            context,
            new TypePropertyDefinition
            {
                TypeId = "cmis:folder",
                PropertyId = "custom:owner",
                LocalName = "owner",
                PropertyType = "string",
                Cardinality = "single",
                Updatability = "readwrite",
                Required = false
            });

        context.SaveChanges();
    }


    private static void AddPropertyIfMissing(
        AppDbContext context,
        TypePropertyDefinition definition)
    {
        var exists = context.TypePropertyDefinitions.Any(p =>
            p.TypeId == definition.TypeId &&
            p.PropertyId == definition.PropertyId);

        if (!exists)
        {
            context.TypePropertyDefinitions.Add(definition);
        }
    }


    /// <summary>
    /// Additive production/PostgreSQL schema setup.
    /// Safe to call on every startup.
    /// </summary>
    public static async Task EnsureCustomPropertyTablesAsync(
        AppDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            ObjectPropertiesTableSql);

        await context.Database.ExecuteSqlRawAsync(
            TypePropertyDefinitionsTableSql);

        await context.Database.ExecuteSqlRawAsync(
            TypeInheritanceSql);
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

        CREATE INDEX IF NOT EXISTS
        ix_objectproperties_objectid
        ON ""ObjectProperties"" (""ObjectId"");
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

        CREATE INDEX IF NOT EXISTS
        ix_typepropertydefinitions_typeid
        ON ""TypePropertyDefinitions"" (""TypeId"");
    ";


    private const string TypeInheritanceSql = @"
        ALTER TABLE ""Types""
        ADD COLUMN IF NOT EXISTS ""ParentTypeId"" TEXT NULL;

        CREATE INDEX IF NOT EXISTS
        ""IX_Types_ParentTypeId""
        ON ""Types"" (""ParentTypeId"");

        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1
                FROM pg_constraint
                WHERE conname = 'FK_Types_Types_ParentTypeId'
            ) THEN
                ALTER TABLE ""Types""
                ADD CONSTRAINT ""FK_Types_Types_ParentTypeId""
                FOREIGN KEY (""ParentTypeId"")
                REFERENCES ""Types"" (""Id"")
                ON DELETE RESTRICT;
            END IF;
        END $$;
    ";
}