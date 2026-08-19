using System.Text.Json;
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

    // ---------- Types ----------

    public async Task<IEnumerable<CmisType>> GetTypesAsync()
    {
        return await _context.Types.ToListAsync();
    }

    public async Task<CmisTypeDefinition?> GetTypeDefinitionAsync(string typeId)
    {
        var type = await _context.Types.FirstOrDefaultAsync(t => t.Id == typeId);
        if (type == null)
        {
            return null;
        }

        var customProps = await _context.TypePropertyDefinitions
            .Where(d => d.TypeId == typeId)
            .ToListAsync();

        return CmisTypeDefinition.WithCustomProperties(type, customProps);
    }

    // ---------- Reads (raw entities - used internally and by write operations) ----------

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

    // ---------- Reads (properties envelope - what controllers return to clients) ----------

    public async Task<CmisObjectEnvelope?> GetObjectEnvelopeAsync(string objectId)
    {
        var obj = await GetObjectByIdAsync(objectId);
        if (obj == null)
        {
            return null;
        }

        var envelopes = await BuildEnvelopesAsync(new List<CmisObject> { obj });
        return envelopes.FirstOrDefault();
    }

    public async Task<IEnumerable<CmisObjectEnvelope>> GetChildrenEnvelopesAsync(string folderId)
    {
        var children = (await GetChildrenAsync(folderId)).ToList();
        return await BuildEnvelopesAsync(children);
    }

    public async Task<IEnumerable<CmisObjectEnvelope>> GetParentsEnvelopesAsync(string objectId)
    {
        var parents = (await GetParentsAsync(objectId)).ToList();
        return await BuildEnvelopesAsync(parents);
    }

    /// <summary>
    /// Builds the CMIS properties envelope for a batch of objects in O(1) queries
    /// (not N+1): loads all their custom property values and the relevant
    /// TypePropertyDefinitions once, then assembles each envelope in memory.
    /// </summary>
    private async Task<List<CmisObjectEnvelope>> BuildEnvelopesAsync(List<CmisObject> objs)
    {
        if (objs.Count == 0)
        {
            return new List<CmisObjectEnvelope>();
        }

        var objectIds = objs.Select(o => o.Id).ToList();
        var typeIds = objs.Select(o => o.TypeId).Distinct().ToList();

        var customPropsByObject = (await _context.ObjectProperties
                .Where(p => objectIds.Contains(p.ObjectId))
                .ToListAsync())
            .GroupBy(p => p.ObjectId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var typeDefsByType = (await _context.TypePropertyDefinitions
                .Where(d => typeIds.Contains(d.TypeId))
                .ToListAsync())
            .GroupBy(d => d.TypeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var envelopes = new List<CmisObjectEnvelope>();
        foreach (var obj in objs)
        {
            var customProps = customPropsByObject.TryGetValue(obj.Id, out var cp) ? cp : new List<ObjectProperty>();
            var typeDefs = typeDefsByType.TryGetValue(obj.TypeId, out var td) ? td : new List<TypePropertyDefinition>();
            envelopes.Add(BuildEnvelope(obj, customProps, typeDefs));
        }

        return envelopes;
    }

    private static CmisObjectEnvelope BuildEnvelope(
        CmisObject obj, List<ObjectProperty> customProps, List<TypePropertyDefinition> typeDefs)
    {
        var envelope = new CmisObjectEnvelope
        {
            Id = obj.Id,
            Name = obj.Name,
            TypeId = obj.TypeId,
            ParentId = obj.ParentId,
            Path = obj.Path
        };

        void AddSystem(string id, string localName, string type, object? value) =>
            envelope.Properties[id] = new CmisPropertyValue
            {
                Id = id,
                LocalName = localName,
                Type = type,
                Cardinality = "single",
                Value = value
            };

        AddSystem("cmis:objectId", "objectId", "id", obj.Id);
        AddSystem("cmis:name", "name", "string", obj.Name);
        AddSystem("cmis:objectTypeId", "objectTypeId", "id", obj.TypeId);
        AddSystem("cmis:parentId", "parentId", "id", obj.ParentId);
        AddSystem("cmis:path", "path", "string", obj.Path);
        AddSystem("cmis:createdBy", "createdBy", "string", obj.CreatedBy);
        AddSystem("cmis:creationDate", "creationDate", "datetime", obj.CreationDate);
        AddSystem("cmis:lastModificationDate", "lastModificationDate", "datetime", obj.LastModificationDate);

        if (obj.TypeId == "cmis:document")
        {
            AddSystem("cmis:contentStreamLength", "contentStreamLength", "integer", obj.ContentStreamLength);
            AddSystem("cmis:contentStreamMimeType", "contentStreamMimeType", "string", obj.MimeType);
        }

        foreach (var group in customProps.GroupBy(p => p.PropertyId))
        {
            var def = typeDefs.FirstOrDefault(d => d.PropertyId.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
            var ordered = group.OrderBy(p => p.SortOrder).ToList();
            var cardinality = def?.Cardinality ?? (ordered.Count > 1 ? "multi" : "single");

            object? value = cardinality == "multi"
                ? ordered.Select(p => p.Value).ToArray()
                : ordered.FirstOrDefault()?.Value;

            envelope.Properties[group.Key] = new CmisPropertyValue
            {
                Id = group.Key,
                LocalName = def?.LocalName ?? group.Key,
                Type = def?.PropertyType ?? "string",
                Cardinality = cardinality,
                Value = value
            };
        }

        return envelope;
    }

    // ---------- Custom property validation (create / update) ----------

    /// <summary>
    /// Parses and validates a JSON object of custom property id -> value (or array
    /// of values, for multi-valued properties) against the type's TypePropertyDefinitions.
    /// Returns the ObjectProperty rows to insert. Empty/null values are skipped here
    /// (they mean "no value supplied", not "clear this property" - clearing only
    /// applies on update, handled separately in ApplyCustomPropertyUpdatesAsync).
    /// </summary>
    private async Task<List<ObjectProperty>> ValidateAndBuildCustomPropertiesAsync(
        string typeId, string objectId, string? propertiesJson, bool isCreate)
    {
        var typeDefs = await _context.TypePropertyDefinitions.Where(d => d.TypeId == typeId).ToListAsync();
        var result = new List<ObjectProperty>();

        var parsed = ParsePropertiesJson(propertiesJson);

        foreach (var kvp in parsed)
        {
            var def = typeDefs.FirstOrDefault(d => d.PropertyId.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));
            if (def == null)
            {
                throw new InvalidOperationException($"Unknown property '{kvp.Key}' for type '{typeId}'.");
            }

            if (def.Updatability == "readonly")
            {
                throw new InvalidOperationException($"Property '{kvp.Key}' is read-only and cannot be set.");
            }

            if (IsEmptyValue(kvp.Value))
            {
                continue; // nothing to insert
            }

            result.AddRange(BuildPropertyRows(objectId, def, kvp.Value));
        }

        if (isCreate)
        {
            var missing = typeDefs.FirstOrDefault(d =>
                d.Required && !parsed.Keys.Any(k => k.Equals(d.PropertyId, StringComparison.OrdinalIgnoreCase)));

            if (missing != null)
            {
                throw new InvalidOperationException($"Required property '{missing.PropertyId}' is missing.");
            }
        }

        return result;
    }

    /// <summary>
    /// Applies a properties update in place: readonly properties are rejected,
    /// an empty/null value clears the property ("vider une propriété"), anything
    /// else replaces the existing value(s) for that property.
    /// </summary>
    private async Task ApplyCustomPropertyUpdatesAsync(string objectId, string typeId, string propertiesJson)
    {
        var typeDefs = await _context.TypePropertyDefinitions.Where(d => d.TypeId == typeId).ToListAsync();
        var parsed = ParsePropertiesJson(propertiesJson);

        foreach (var kvp in parsed)
        {
            var def = typeDefs.FirstOrDefault(d => d.PropertyId.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));
            if (def == null)
            {
                throw new InvalidOperationException($"Unknown property '{kvp.Key}' for type '{typeId}'.");
            }

            if (def.Updatability == "readonly")
            {
                throw new InvalidOperationException($"Property '{kvp.Key}' is read-only and cannot be modified.");
            }

            var existing = _context.ObjectProperties.Where(p => p.ObjectId == objectId && p.PropertyId == def.PropertyId);
            _context.ObjectProperties.RemoveRange(existing);

            if (IsEmptyValue(kvp.Value))
            {
                continue; // cleared - nothing re-added
            }

            _context.ObjectProperties.AddRange(BuildPropertyRows(objectId, def, kvp.Value));
        }
    }

    private static Dictionary<string, JsonElement> ParsePropertiesJson(string? propertiesJson)
    {
        if (string.IsNullOrWhiteSpace(propertiesJson))
        {
            return new Dictionary<string, JsonElement>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(propertiesJson)
                   ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                "'properties' must be a valid JSON object, e.g. {\"custom:department\":\"Finance\"}.");
        }
    }

    private static bool IsEmptyValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ||
        (value.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(value.GetString()));

    private static List<ObjectProperty> BuildPropertyRows(string objectId, TypePropertyDefinition def, JsonElement value)
    {
        var rows = new List<ObjectProperty>();

        if (def.Cardinality == "multi")
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"Property '{def.PropertyId}' is multi-valued and expects a JSON array.");
            }

            var order = 0;
            foreach (var item in value.EnumerateArray())
            {
                rows.Add(new ObjectProperty
                {
                    ObjectId = objectId,
                    PropertyId = def.PropertyId,
                    PropertyType = def.PropertyType,
                    Cardinality = def.Cardinality,
                    Value = item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString(),
                    SortOrder = order++
                });
            }
        }
        else
        {
            rows.Add(new ObjectProperty
            {
                ObjectId = objectId,
                PropertyId = def.PropertyId,
                PropertyType = def.PropertyType,
                Cardinality = def.Cardinality,
                Value = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString(),
                SortOrder = 0
            });
        }

        return rows;
    }

    // ---------- Writes ----------

    public async Task<CmisObject> CreateDocumentAsync(
        string parentId, string name, string mimeType, byte[] content, string? propertiesJson = null)
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

        var customPropertyRows = await ValidateAndBuildCustomPropertiesAsync(
            "cmis:document", newDoc.Id, propertiesJson, isCreate: true);

        _context.Objects.Add(newDoc);
        _context.ObjectProperties.AddRange(customPropertyRows);
        await _context.SaveChangesAsync();
        return newDoc;
    }

    public async Task<CmisObject> CreateFolderAsync(string parentId, string name, string? propertiesJson = null)
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

        var customPropertyRows = await ValidateAndBuildCustomPropertiesAsync(
            "cmis:folder", newFolder.Id, propertiesJson, isCreate: true);

        _context.Objects.Add(newFolder);
        _context.ObjectProperties.AddRange(customPropertyRows);
        await _context.SaveChangesAsync();
        return newFolder;
    }

    public async Task<CmisObject> UpdateObjectAsync(string objectId, string? newName, string? propertiesJson = null)
    {
        if (string.IsNullOrWhiteSpace(newName) && string.IsNullOrWhiteSpace(propertiesJson))
        {
            throw new InvalidOperationException("Provide at least 'name' or 'properties' to update.");
        }

        var obj = await _context.Objects.FirstOrDefaultAsync(o => o.Id == objectId);
        if (obj == null)
        {
            throw new KeyNotFoundException($"Object with ID '{objectId}' was not found.");
        }

        if (!string.IsNullOrWhiteSpace(newName))
        {
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
        }

        if (!string.IsNullOrWhiteSpace(propertiesJson))
        {
            await ApplyCustomPropertyUpdatesAsync(objectId, obj.TypeId, propertiesJson);
        }

        obj.LastModificationDate = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return obj;
    }

    public async Task<CmisObject> SetContentStreamAsync(string objectId, string mimeType, byte[] content)
    {
        var obj = await _context.Objects.FirstOrDefaultAsync(o => o.Id == objectId);
        if (obj == null)
        {
            throw new KeyNotFoundException($"Object with ID '{objectId}' was not found.");
        }

        if (obj.TypeId != "cmis:document")
        {
            throw new InvalidOperationException("setContentStream can only be used on documents.");
        }

        obj.ContentStream = content;
        obj.MimeType = mimeType;
        obj.ContentStreamLength = content.Length;
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

        var customProps = _context.ObjectProperties.Where(p => p.ObjectId == objectId);
        _context.ObjectProperties.RemoveRange(customProps);
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
        var allIds = descendants.Select(d => d.Id).Append(folder.Id).ToList();

        var customProps = _context.ObjectProperties.Where(p => allIds.Contains(p.ObjectId));
        _context.ObjectProperties.RemoveRange(customProps);

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

        var candidateIds = candidates.Select(o => o.Id).ToList();

        // Load custom property values + their type definitions once (no N+1), so
        // WHERE/ORDER BY can also resolve custom (non-system) properties, typed
        // correctly per TypePropertyDefinition.PropertyType.
        var customPropsByObject = (await _context.ObjectProperties
                .Where(p => candidateIds.Contains(p.ObjectId))
                .ToListAsync())
            .GroupBy(p => p.ObjectId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.SortOrder).ToList());

        object? CustomResolver(CmisObject obj, string propertyId)
        {
            if (!customPropsByObject.TryGetValue(obj.Id, out var props))
            {
                return null;
            }

            var match = props.FirstOrDefault(p => p.PropertyId.Equals(propertyId, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                return null;
            }

            // WHERE/ORDER BY compare against the first value for multi-valued properties
            // (documented simplification, consistent with the parser's scope).
            return match.PropertyType switch
            {
                "integer" => long.TryParse(match.Value, out var l) ? l : null,
                "datetime" => DateTime.TryParse(match.Value, out var d) ? d : null,
                "boolean" => bool.TryParse(match.Value, out var b) ? b : null,
                _ => match.Value
            };
        }

        var filtered = candidates.Where(o => CmisQueryParser.Evaluate(o, parsed.WhereClause, CustomResolver));
        var sorted = CmisQueryParser.Sort(filtered, parsed, CustomResolver).ToList();

        var numItems = sorted.Count;
        var page = sorted.Skip(skipCount).Take(maxItems).ToList();
        var hasMoreItems = skipCount + page.Count < numItems;

        return (page, numItems, hasMoreItems);
    }
}
