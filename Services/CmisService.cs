using CMIS_IyaSoft.Data;
using CMIS_IyaSoft.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMIS_IyaSoft.Services;

public class CmisService : ICmisService
{
    private readonly AppDbContext _context;

    public CmisService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<CmisType>> GetTypesAsync()
    {
        return await _context.Types.ToListAsync();
    }

    public async Task<IEnumerable<CmisObject>> GetChildrenAsync(string folderId)
    {
        return await _context.Objects
            .Where(o => o.ParentId == folderId)
            .ToListAsync();
    }

    public async Task<CmisObject?> GetObjectByIdAsync(string objectId)
    {
        return await _context.Objects
            .FirstOrDefaultAsync(o => o.Id == objectId);
    }

    public async Task<(byte[]? Content, string? MimeType, string Name)?> GetContentStreamAsync(string objectId)
    {
        var doc = await _context.Objects.FirstOrDefaultAsync(o => o.Id == objectId);

        if (doc == null || doc.ContentStream == null)
        {
            return null;
        }

        return (doc.ContentStream, doc.MimeType ?? "application/octet-stream", doc.Name);
    }

    public async Task<CmisObject> CreateDocumentAsync(string parentId, string name, string mimeType, byte[] content)
    {
        // Check if an object with the same name already exists in this folder
        var existing = await _context.Objects
            .FirstOrDefaultAsync(o => o.ParentId == parentId && o.Name == name);

        if (existing != null)
        {
            throw new InvalidOperationException($"An object named '{name}' already exists in this folder.");
        }

        var parentFolder = await _context.Objects.FirstOrDefaultAsync(o => o.Id == parentId);
        var parentPath = parentFolder?.Path ?? "";
        var fullPath = parentPath == "/" ? $"/{name}" : $"{parentPath}/{name}";

        var newDoc = new CmisObject
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            TypeId = "cmis:document",
            ParentId = parentId,
            Path = fullPath,
            MimeType = mimeType,
            ContentStream = content,
            ContentStreamLength = content.Length,
            CreatedBy = "admin",
            CreationDate = DateTime.UtcNow,
            LastModificationDate = DateTime.UtcNow
        };

        _context.Objects.Add(newDoc);
        await _context.SaveChangesAsync();
        return newDoc;
    }

    public async Task<CmisObject> CreateFolderAsync(string parentId, string name)
    {
        var parentFolder = await _context.Objects.FirstOrDefaultAsync(o => o.Id == parentId);
        var parentPath = parentFolder?.Path ?? "";
        var fullPath = parentPath == "/" ? $"/{name}" : $"{parentPath}/{name}";

        var newFolder = new CmisObject
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            TypeId = "cmis:folder",
            ParentId = parentId,
            Path = fullPath,
            CreatedBy = "admin",
            CreationDate = DateTime.UtcNow,
            LastModificationDate = DateTime.UtcNow
        };

        _context.Objects.Add(newFolder);
        await _context.SaveChangesAsync();
        return newFolder;
    }
}