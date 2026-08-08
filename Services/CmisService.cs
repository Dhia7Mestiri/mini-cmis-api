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
        // 1. Prevent duplicates in the same parent folder
        var existing = await _context.Objects
            .FirstOrDefaultAsync(o => o.ParentId == parentId && o.Name == name);

        if (existing != null)
        {
            throw new InvalidOperationException($"An object named '{name}' already exists in this folder.");
        }

        // 2. Build full path
        var parentFolder = await _context.Objects.FirstOrDefaultAsync(o => o.Id == parentId);
        var parentPath = parentFolder?.Path ?? "/";
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
        // 1. Prevent duplicates in the same parent folder
        var existing = await _context.Objects
            .FirstOrDefaultAsync(o => o.ParentId == parentId && o.Name == name);

        if (existing != null)
        {
            throw new InvalidOperationException($"A folder named '{name}' already exists in this folder.");
        }

        // 2. Build full path
        var parentFolder = await _context.Objects.FirstOrDefaultAsync(o => o.Id == parentId);
        var parentPath = parentFolder?.Path ?? "/";
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

    public async Task<bool> DeleteObjectAsync(string objectId)
    {
        var cmisObj = await _context.Objects
            .Include(o => o.Children)
            .FirstOrDefaultAsync(o => o.Id == objectId);

        if (cmisObj == null)
        {
            return false;
        }

        // If it's a folder with children, restrict deletion to keep hierarchy safe
        if (cmisObj.TypeId == "cmis:folder" && cmisObj.Children.Any())
        {
            throw new InvalidOperationException("Cannot delete a folder that contains child objects. Delete the contents first.");
        }

        _context.Objects.Remove(cmisObj);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<CmisObject>> SearchObjectsAsync(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return Enumerable.Empty<CmisObject>();
        }

        return await _context.Objects
            .Where(o => EF.Functions.Like(o.Name, $"%{searchTerm}%"))
            .ToListAsync();
    }
}