using CMIS_IyaSoft.Entities;

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
    }
}