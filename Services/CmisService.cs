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

    public async Task<CmisType?> GetTypeDefinitionAsync(string typeId)
    {
        return await _context.Types.FirstOrDefaultAsync(t => t.Id == typeId);
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

    public async Task<IEnumerable<CmisObject>> GetParentsAsync(string objectId)
    {
        var obj = await _context.Objects.FirstOrDefaultAsync(o => o.Id == objectId);
        if (obj == null || obj.ParentId == null)
        {
            return Enumerable.Empty<CmisObject>();
        }

        var parent = await _context.Objects.FirstOrDefaultAsync(o => o.Id == obj.ParentId);
        return parent == null ? Enumerable.Empty<CmisObject>() : new[] { parent };
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
        var existing = await _context.Objects
            .FirstOrDefaultAsync(o => o.ParentId == parentId && o.Name == name);

        if (existing != null)
        {
            throw new InvalidOperationException($"An object named '{name}' already exists in this folder.");
        }

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
        var existing = await _context.Objects
            .FirstOrDefaultAsync(o => o.ParentId == parentId && o.Name == name);

        if (existing != null)
        {
            throw new InvalidOperationException($"A folder named '{name}' already exists in this folder.");
        }

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

    public async Task<CmisObject> UpdateObjectAsync(string objectId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new InvalidOperationException("The new name cannot be empty.");
        }

        var obj = await _context.Objects.FirstOrDefaultAsync(o => o.Id == objectId);
        if (obj == null)
        {
            throw new KeyNotFoundException($"Object with ID '{objectId}' was not found.");
        }

        if (obj.ParentId == null)
        {
            throw new InvalidOperationException("Cannot rename the root folder.");
        }

        var duplicate = await _context.Objects
            .FirstOrDefaultAsync(o => o.ParentId == obj.ParentId && o.Name == newName && o.Id != objectId);

        if (duplicate != null)
        {
            throw new InvalidOperationException($"An object named '{newName}' already exists in this folder.");
        }

        var oldPath = obj.Path;
        var lastSlashIndex = oldPath.LastIndexOf('/');
        var parentPath = lastSlashIndex <= 0 ? "/" : oldPath.Substring(0, lastSlashIndex);
        var newPath = parentPath == "/" ? $"/{newName}" : $"{parentPath}/{newName}";

        // Rewrite the path prefix on every descendant (materialized path pattern)
        var descendants = await _context.Objects
            .Where(o => o.Path.StartsWith(oldPath + "/"))
            .ToListAsync();

        foreach (var descendant in descendants)
        {
            descendant.Path = newPath + descendant.Path.Substring(oldPath.Length);
            descendant.LastModificationDate = DateTime.UtcNow;
        }

        obj.Name = newName;
        obj.Path = newPath;
        obj.LastModificationDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return obj;
    }

    public async Task<CmisObject> MoveObjectAsync(string objectId, string targetFolderId)
    {
        var obj = await _context.Objects.FirstOrDefaultAsync(o => o.Id == objectId);
        if (obj == null)
        {
            throw new KeyNotFoundException($"Object with ID '{objectId}' was not found.");
        }

        if (obj.Id == targetFolderId)
        {
            throw new InvalidOperationException("Cannot move an object into itself.");
        }

        if (obj.ParentId == null)
        {
            throw new InvalidOperationException("Cannot move the root folder.");
        }

        var targetFolder = await _context.Objects.FirstOrDefaultAsync(o => o.Id == targetFolderId);
        if (targetFolder == null)
        {
            throw new KeyNotFoundException($"Target folder with ID '{targetFolderId}' was not found.");
        }

        if (targetFolder.TypeId != "cmis:folder")
        {
            throw new InvalidOperationException("Target object is not a folder.");
        }

        // Prevent moving a folder into one of its own descendants (would create a cycle)
        if (obj.TypeId == "cmis:folder" &&
            (targetFolder.Path == obj.Path || targetFolder.Path.StartsWith(obj.Path + "/")))
        {
            throw new InvalidOperationException("Cannot move a folder into its own descendant.");
        }

        var duplicate = await _context.Objects
            .FirstOrDefaultAsync(o => o.ParentId == targetFolderId && o.Name == obj.Name && o.Id != objectId);

        if (duplicate != null)
        {
            throw new InvalidOperationException($"An object named '{obj.Name}' already exists in the target folder.");
        }

        var oldPath = obj.Path;
        var newPath = targetFolder.Path == "/" ? $"/{obj.Name}" : $"{targetFolder.Path}/{obj.Name}";

        var descendants = await _context.Objects
            .Where(o => o.Path.StartsWith(oldPath + "/"))
            .ToListAsync();

        foreach (var descendant in descendants)
        {
            descendant.Path = newPath + descendant.Path.Substring(oldPath.Length);
            descendant.LastModificationDate = DateTime.UtcNow;
        }

        obj.ParentId = targetFolderId;
        obj.Path = newPath;
        obj.LastModificationDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return obj;
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

        if (cmisObj.ParentId == null)
        {
            throw new InvalidOperationException("Cannot delete the root folder.");
        }

        if (cmisObj.TypeId == "cmis:folder" && cmisObj.Children.Any())
        {
            throw new InvalidOperationException("Cannot delete a folder that contains child objects. Delete the contents first, or use deleteTree.");
        }

        _context.Objects.Remove(cmisObj);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<int> DeleteTreeAsync(string folderId)
    {
        var folder = await _context.Objects.FirstOrDefaultAsync(o => o.Id == folderId);
        if (folder == null)
        {
            throw new KeyNotFoundException($"Object with ID '{folderId}' was not found.");
        }

        if (folder.TypeId != "cmis:folder")
        {
            throw new InvalidOperationException("deleteTree can only be used on folders.");
        }

        if (folder.ParentId == null)
        {
            throw new InvalidOperationException("Cannot delete the root folder.");
        }

        // Materialized path makes this a single query regardless of depth
        var descendants = await _context.Objects
            .Where(o => o.Path.StartsWith(folder.Path + "/"))
            .ToListAsync();

        var count = descendants.Count + 1;

        _context.Objects.RemoveRange(descendants);
        _context.Objects.Remove(folder);
        await _context.SaveChangesAsync();

        return count;
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

    public async Task<(IEnumerable<CmisObject> Results, int NumItems, bool HasMoreItems)> ExecuteQueryAsync(
        string statement, int maxItems = 100, int skipCount = 0)
    {
        var parsed = CmisQueryParser.Parse(statement);

        // Filter by type first (translatable to SQL), then evaluate the WHERE clause
        // in memory since it supports arbitrary property comparisons the provider
        // can't always translate. Acceptable trade-off for this project's scope.
        var candidates = await _context.Objects
            .Where(o => o.TypeId == parsed.TypeId)
            .ToListAsync();

        var filtered = candidates.Where(o => CmisQueryParser.Evaluate(o, parsed.WhereClause));
        var sorted = CmisQueryParser.Sort(filtered, parsed).ToList();

        var numItems = sorted.Count;
        var page = sorted.Skip(skipCount).Take(maxItems).ToList();
        var hasMoreItems = skipCount + page.Count < numItems;

        return (page, numItems, hasMoreItems);
    }
}
