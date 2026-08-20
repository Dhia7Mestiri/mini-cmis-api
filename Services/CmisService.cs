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
        return await _context.Types
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IEnumerable<CmisType>> GetTypeChildrenAsync(string? typeId)
    {
        // If no parent type is supplied, return the CMIS root/base types.
        if (string.IsNullOrWhiteSpace(typeId))
        {
            return await _context.Types
                .AsNoTracking()
                .Where(t => t.ParentTypeId == null)
                .OrderBy(t => t.Id)
                .ToListAsync();
        }

        var parentExists = await _context.Types
            .AsNoTracking()
            .AnyAsync(t => t.Id == typeId);

        if (!parentExists)
        {
            throw new KeyNotFoundException(
                $"CMIS type '{typeId}' was not found.");
        }

        return await _context.Types
            .AsNoTracking()
            .Where(t => t.ParentTypeId == typeId)
            .OrderBy(t => t.Id)
            .ToListAsync();
    }

    public async Task<CmisTypeDefinition?> GetTypeDefinitionAsync(string typeId)
    {
        var type = await _context.Types
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == typeId);

        if (type == null)
        {
            return null;
        }

        var inheritedProperties =
            await GetInheritedTypePropertiesAsync(type);

        return CmisTypeDefinition.WithCustomProperties(
            type,
            inheritedProperties);
    }
    public async Task<CmisTypeDefinition> CreateTypeAsync(
    CreateCmisTypeRequest request)
    {
        if (request == null)
        {
            throw new InvalidOperationException(
                "Type definition is required.");
        }

        var typeId = request.Id?.Trim();
        var parentTypeId = request.ParentTypeId?.Trim();

        if (string.IsNullOrWhiteSpace(typeId))
        {
            throw new InvalidOperationException(
                "Type ID is required.");
        }

        if (string.IsNullOrWhiteSpace(parentTypeId))
        {
            throw new InvalidOperationException(
                "Parent type ID is required.");
        }

        // cmis:* is reserved for system/base types.
        if (typeId.StartsWith(
            "cmis:",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Custom types cannot use the reserved 'cmis:' namespace.");
        }

        var alreadyExists = await _context.Types
            .AnyAsync(t => t.Id == typeId);

        if (alreadyExists)
        {
            throw new InvalidOperationException(
                $"CMIS type '{typeId}' already exists.");
        }

        var parentType = await _context.Types
            .FirstOrDefaultAsync(t => t.Id == parentTypeId);

        if (parentType == null)
        {
            throw new KeyNotFoundException(
                $"Parent type '{parentTypeId}' was not found.");
        }

        // Parent must ultimately belong to one of our two supported
        // CMIS base hierarchies.
        if (!string.Equals(
                parentType.BaseId,
                "cmis:document",
                StringComparison.OrdinalIgnoreCase)
            &&
            !string.Equals(
                parentType.BaseId,
                "cmis:folder",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Parent type '{parentTypeId}' has an unsupported base type.");
        }

        var propertyRequests =
            request.PropertyDefinitions ??
            new List<CmisTypePropertyRequest>();

        // Prevent duplicate property IDs inside this new type.
        var duplicateProperty = propertyRequests
            .Where(p => !string.IsNullOrWhiteSpace(p.PropertyId))
            .GroupBy(
                p => p.PropertyId.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateProperty != null)
        {
            throw new InvalidOperationException(
                $"Property '{duplicateProperty.Key}' is defined more than once.");
        }

        // Parent properties cannot be redefined by the child.
        var inheritedProperties =
            await GetEffectiveTypePropertyDefinitionsAsync(
                parentType.Id);

        var newType = new CmisType
        {
            Id = typeId,
            BaseId = parentType.BaseId,
            ParentTypeId = parentType.Id,

            DisplayName =
                string.IsNullOrWhiteSpace(request.DisplayName)
                    ? typeId
                    : request.DisplayName.Trim(),

            Description =
                request.Description?.Trim() ?? string.Empty
        };

        _context.Types.Add(newType);

        foreach (var property in propertyRequests)
        {
            ValidateManagedPropertyDefinition(
                property,
                inheritedProperties);

            _context.TypePropertyDefinitions.Add(
                new TypePropertyDefinition
                {
                    TypeId = typeId,

                    PropertyId =
                        property.PropertyId.Trim(),

                    LocalName =
                        string.IsNullOrWhiteSpace(property.LocalName)
                            ? property.PropertyId.Trim()
                            : property.LocalName.Trim(),

                    PropertyType =
                        property.PropertyType
                            .Trim()
                            .ToLowerInvariant(),

                    Cardinality =
                        property.Cardinality
                            .Trim()
                            .ToLowerInvariant(),

                    Updatability =
                        property.Updatability
                            .Trim()
                            .ToLowerInvariant(),

                    Required = property.Required
                });
        }

        await _context.SaveChangesAsync();

        var definition =
            await GetTypeDefinitionAsync(typeId);

        return definition
            ?? throw new InvalidOperationException(
                $"Type '{typeId}' was created but could not be loaded.");
    }
    public async Task<CmisTypeDefinition> UpdateTypeAsync(
    string typeId,
    UpdateCmisTypeRequest request)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            throw new InvalidOperationException(
                "Type ID is required.");
        }

        if (string.Equals(
                typeId,
                "cmis:document",
                StringComparison.OrdinalIgnoreCase)
            ||
            string.Equals(
                typeId,
                "cmis:folder",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Base CMIS type '{typeId}' cannot be modified.");
        }

        var type = await _context.Types
            .FirstOrDefaultAsync(t => t.Id == typeId);

        if (type == null)
        {
            throw new KeyNotFoundException(
                $"CMIS type '{typeId}' was not found.");
        }

        if (request == null)
        {
            throw new InvalidOperationException(
                "Type update definition is required.");
        }

        // -----------------------------
        // Parent
        // -----------------------------

        var parentTypeId =
            string.IsNullOrWhiteSpace(request.ParentTypeId)
                ? type.ParentTypeId
                : request.ParentTypeId.Trim();

        if (string.IsNullOrWhiteSpace(parentTypeId))
        {
            throw new InvalidOperationException(
                "A custom type must have a parent.");
        }

        if (string.Equals(
            parentTypeId,
            typeId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A type cannot inherit from itself.");
        }

        var parent = await _context.Types
            .FirstOrDefaultAsync(t => t.Id == parentTypeId);

        if (parent == null)
        {
            throw new KeyNotFoundException(
                $"Parent type '{parentTypeId}' was not found.");
        }

        // A document type cannot suddenly become a folder type
        // and vice versa.
        if (!string.Equals(
            parent.BaseId,
            type.BaseId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Type '{typeId}' must remain inside the '{type.BaseId}' hierarchy.");
        }

        // -----------------------------
        // Cycle protection
        // -----------------------------

        var currentParentId = parent.Id;

        var visited =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        while (!string.IsNullOrWhiteSpace(currentParentId))
        {
            if (!visited.Add(currentParentId))
            {
                throw new InvalidOperationException(
                    "Circular type inheritance detected.");
            }

            if (string.Equals(
                currentParentId,
                type.Id,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Changing the parent to '{parent.Id}' would create an inheritance cycle.");
            }

            currentParentId = await _context.Types
                .Where(t => t.Id == currentParentId)
                .Select(t => t.ParentTypeId)
                .FirstOrDefaultAsync();
        }

        // -----------------------------
        // Basic information
        // -----------------------------

        if (request.DisplayName != null)
        {
            type.DisplayName =
                string.IsNullOrWhiteSpace(request.DisplayName)
                    ? type.Id
                    : request.DisplayName.Trim();
        }

        if (request.Description != null)
        {
            type.Description =
                request.Description.Trim();
        }

        type.ParentTypeId = parent.Id;

        // -----------------------------
        // Property definitions
        // -----------------------------

        // null = don't modify properties.
        //
        // [] = replace own properties with zero properties.
        if (request.PropertyDefinitions != null)
        {
            var propertyRequests =
                request.PropertyDefinitions;

            var duplicateProperty = propertyRequests
                .Where(p =>
                    !string.IsNullOrWhiteSpace(
                        p.PropertyId))
                .GroupBy(
                    p => p.PropertyId.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicateProperty != null)
            {
                throw new InvalidOperationException(
                    $"Property '{duplicateProperty.Key}' is defined more than once.");
            }

            var inheritedProperties =
                await GetEffectiveTypePropertyDefinitionsAsync(
                    parent.Id);

            foreach (var property in propertyRequests)
            {
                ValidateManagedPropertyDefinition(
                    property,
                    inheritedProperties);
            }

            var existingDefinitions =
                await _context.TypePropertyDefinitions
                    .Where(p => p.TypeId == type.Id)
                    .ToListAsync();

            // Don't remove a property which already contains
            // values on repository objects.
            foreach (var existingDefinition
                     in existingDefinitions)
            {
                var replacement =
                    propertyRequests.FirstOrDefault(p =>
                        string.Equals(
                            p.PropertyId,
                            existingDefinition.PropertyId,
                            StringComparison.OrdinalIgnoreCase));

                if (replacement == null)
                {
                    var descendantTypeIds =
                        await GetTypeAndDescendantIdsAsync(
                            type.Id);

                    var used = await _context.Objects
                        .Where(o =>
                            descendantTypeIds.Contains(
                                o.TypeId))
                        .Join(
                            _context.ObjectProperties,
                            o => o.Id,
                            p => p.ObjectId,
                            (o, p) => p)
                        .AnyAsync(p =>
                            p.PropertyId ==
                            existingDefinition.PropertyId);

                    if (used)
                    {
                        throw new InvalidOperationException(
                            $"Property '{existingDefinition.PropertyId}' cannot be removed because objects contain values for it.");
                    }
                }
            }

            _context.TypePropertyDefinitions
                .RemoveRange(existingDefinitions);

            foreach (var property in propertyRequests)
            {
                _context.TypePropertyDefinitions.Add(
                    new TypePropertyDefinition
                    {
                        TypeId = type.Id,

                        PropertyId =
                            property.PropertyId.Trim(),

                        LocalName =
                            string.IsNullOrWhiteSpace(
                                property.LocalName)
                                ? property.PropertyId.Trim()
                                : property.LocalName.Trim(),

                        PropertyType =
                            property.PropertyType
                                .Trim()
                                .ToLowerInvariant(),

                        Cardinality =
                            property.Cardinality
                                .Trim()
                                .ToLowerInvariant(),

                        Updatability =
                            property.Updatability
                                .Trim()
                                .ToLowerInvariant(),

                        Required = property.Required
                    });
            }
        }

        await _context.SaveChangesAsync();

        var definition =
            await GetTypeDefinitionAsync(type.Id);

        return definition
            ?? throw new InvalidOperationException(
                $"Type '{type.Id}' was updated but could not be loaded.");
    }
    public async Task DeleteTypeAsync(
    string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            throw new InvalidOperationException(
                "Type ID is required.");
        }

        if (string.Equals(
                typeId,
                "cmis:document",
                StringComparison.OrdinalIgnoreCase)
            ||
            string.Equals(
                typeId,
                "cmis:folder",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Base CMIS type '{typeId}' cannot be deleted.");
        }

        var type = await _context.Types
            .FirstOrDefaultAsync(t => t.Id == typeId);

        if (type == null)
        {
            throw new KeyNotFoundException(
                $"CMIS type '{typeId}' was not found.");
        }

        // Don't delete a parent while child types still use it.
        var hasChildren =
            await _context.Types
                .AnyAsync(t =>
                    t.ParentTypeId == typeId);

        if (hasChildren)
        {
            throw new InvalidOperationException(
                $"Type '{typeId}' cannot be deleted because it has child types.");
        }

        // Don't delete a type used by actual repository objects.
        var usedByObjects =
            await _context.Objects
                .AnyAsync(o =>
                    o.TypeId == typeId);

        if (usedByObjects)
        {
            throw new InvalidOperationException(
                $"Type '{typeId}' cannot be deleted because repository objects use it.");
        }

        // Remove only the properties owned by this type.
        var propertyDefinitions =
            await _context.TypePropertyDefinitions
                .Where(p =>
                    p.TypeId == typeId)
                .ToListAsync();

        _context.TypePropertyDefinitions
            .RemoveRange(propertyDefinitions);

        _context.Types.Remove(type);

        await _context.SaveChangesAsync();
    }
    private static void ValidateManagedPropertyDefinition(
    CmisTypePropertyRequest property,
    IEnumerable<TypePropertyDefinition> inheritedProperties)
    {
        if (string.IsNullOrWhiteSpace(
            property.PropertyId))
        {
            throw new InvalidOperationException(
                "Every property requires a propertyId.");
        }

        var propertyId =
            property.PropertyId.Trim();

        if (propertyId.StartsWith(
            "cmis:",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Custom property '{propertyId}' cannot use the reserved 'cmis:' namespace.");
        }

        if (inheritedProperties.Any(p =>
            string.Equals(
                p.PropertyId,
                propertyId,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Property '{propertyId}' is already inherited from the parent type.");
        }

        var propertyType =
            (property.PropertyType ?? "string")
            .Trim()
            .ToLowerInvariant();

        if (propertyType != "string" &&
            propertyType != "integer" &&
            propertyType != "datetime" &&
            propertyType != "boolean")
        {
            throw new InvalidOperationException(
                $"Unsupported property type '{property.PropertyType}'. " +
                "Use string, integer, datetime or boolean.");
        }

        var cardinality =
            (property.Cardinality ?? "single")
            .Trim()
            .ToLowerInvariant();

        if (cardinality != "single" &&
            cardinality != "multi")
        {
            throw new InvalidOperationException(
                $"Invalid cardinality '{property.Cardinality}'. " +
                "Use single or multi.");
        }

        var updatability =
            (property.Updatability ?? "readwrite")
            .Trim()
            .ToLowerInvariant();

        if (updatability != "readonly" &&
            updatability != "oncreate" &&
            updatability != "readwrite")
        {
            throw new InvalidOperationException(
                $"Invalid updatability '{property.Updatability}'.");
        }

        if (property.Required &&
            updatability == "readonly")
        {
            throw new InvalidOperationException(
                $"Property '{propertyId}' cannot be both required and readonly.");
        }
    }

    private async Task<List<TypePropertyDefinition>>
        GetInheritedTypePropertiesAsync(CmisType type)
    {
        var result = new List<TypePropertyDefinition>();

        var visitedTypes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        await CollectTypePropertiesAsync(
            type,
            result,
            visitedTypes);

        return result;
    }
    private async Task<List<TypePropertyDefinition>>
    GetEffectiveTypePropertyDefinitionsAsync(string typeId)
    {
        var type = await _context.Types
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == typeId);

        if (type == null)
        {
            throw new KeyNotFoundException(
                $"CMIS type '{typeId}' was not found.");
        }

        return await GetInheritedTypePropertiesAsync(type);
    }

    private async Task CollectTypePropertiesAsync(
        CmisType type,
        List<TypePropertyDefinition> result,
        HashSet<string> visitedTypes)
    {
        // Prevent infinite recursion if bad/cyclic data ever reaches the DB.
        if (!visitedTypes.Add(type.Id))
        {
            throw new InvalidOperationException(
                $"Circular CMIS type inheritance detected at type '{type.Id}'.");
        }

        // Resolve parent properties first.
        // We stop at cmis:document / cmis:folder because their
        // system properties are already supplied by CmisTypeDefinition.
        if (!string.IsNullOrWhiteSpace(type.ParentTypeId))
        {
            var parent = await _context.Types
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == type.ParentTypeId);

            if (parent == null)
            {
                throw new InvalidOperationException(
                    $"Parent type '{type.ParentTypeId}' of type '{type.Id}' does not exist.");
            }

            if (!string.Equals(
                    parent.Id,
                    "cmis:document",
                    StringComparison.OrdinalIgnoreCase)
                &&
                !string.Equals(
                    parent.Id,
                    "cmis:folder",
                    StringComparison.OrdinalIgnoreCase))
            {
                await CollectTypePropertiesAsync(
                    parent,
                    result,
                    visitedTypes);
            }

            // If someone decides later to add custom definitions directly
            // to a base type, include them too.
            if (string.Equals(
                    parent.Id,
                    "cmis:document",
                    StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(
                    parent.Id,
                    "cmis:folder",
                    StringComparison.OrdinalIgnoreCase))
            {
                var baseCustomProperties =
                    await _context.TypePropertyDefinitions
                        .AsNoTracking()
                        .Where(p => p.TypeId == parent.Id)
                        .ToListAsync();

                AddOrReplaceProperties(
                    result,
                    baseCustomProperties);
            }
        }

        // Finally add this type's own property definitions.
        var ownProperties =
            await _context.TypePropertyDefinitions
                .AsNoTracking()
                .Where(p => p.TypeId == type.Id)
                .ToListAsync();

        AddOrReplaceProperties(
            result,
            ownProperties);
    }
    private async Task<bool> IsTypeDerivedFromAsync(
        string typeId,
        string baseTypeId)
    {
        var type = await _context.Types
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == typeId);

        return type != null &&
               string.Equals(
                   type.BaseId,
                   baseTypeId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<string>> GetTypeAndDescendantIdsAsync(string typeId)
    {
        var allTypes = await _context.Types
            .AsNoTracking()
            .Select(t => new { t.Id, t.ParentTypeId })
            .ToListAsync();

        if (!allTypes.Any(t =>
                t.Id.Equals(typeId, StringComparison.OrdinalIgnoreCase)))
        {
            return new List<string> { typeId };
        }

        var result = new List<string>();
        var queue = new Queue<string>();
        queue.Enqueue(typeId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (result.Any(id =>
                    id.Equals(current, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(current);

            var children = allTypes
                .Where(t => string.Equals(
                    t.ParentTypeId,
                    current,
                    StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Id);

            foreach (var child in children)
            {
                queue.Enqueue(child);
            }
        }

        return result;
    }

    private static void AddOrReplaceProperties(
        List<TypePropertyDefinition> target,
        IEnumerable<TypePropertyDefinition> properties)
    {
        foreach (var property in properties)
        {
            // A child type definition wins if the same property ID
            // is encountered higher in the hierarchy.
            var existing = target.FindIndex(p =>
                string.Equals(
                    p.PropertyId,
                    property.PropertyId,
                    StringComparison.OrdinalIgnoreCase));

            if (existing >= 0)
            {
                target[existing] = property;
            }
            else
            {
                target.Add(property);
            }
        }
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
    /// Builds the CMIS properties envelope for a batch of objects.
    /// Custom values are loaded in one batch and effective type definitions
    /// are resolved per distinct object type so inherited properties are preserved.
    /// </summary>
    private async Task<List<CmisObjectEnvelope>> BuildEnvelopesAsync(List<CmisObject> objs)
    {
        if (objs.Count == 0)
        {
            return new List<CmisObjectEnvelope>();
        }

        var objectIds = objs.Select(o => o.Id).ToList();
        var typeIds = objs
            .Select(o => o.TypeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var customPropsByObject = (await _context.ObjectProperties
                .Where(p => objectIds.Contains(p.ObjectId))
                .ToListAsync())
            .GroupBy(p => p.ObjectId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var cmisTypes = (await _context.Types
                .AsNoTracking()
                .Where(t => typeIds.Contains(t.Id))
                .ToListAsync())
            .ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        var effectiveDefsByType =
            new Dictionary<string, List<TypePropertyDefinition>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var typeId in typeIds)
        {
            effectiveDefsByType[typeId] =
                await GetEffectiveTypePropertyDefinitionsAsync(typeId);
        }

        var envelopes = new List<CmisObjectEnvelope>();

        foreach (var obj in objs)
        {
            var customProps = customPropsByObject.TryGetValue(obj.Id, out var cp)
                ? cp
                : new List<ObjectProperty>();

            var typeDefs = effectiveDefsByType.TryGetValue(obj.TypeId, out var td)
                ? td
                : new List<TypePropertyDefinition>();

            var isDocument = cmisTypes.TryGetValue(obj.TypeId, out var cmisType) &&
                             string.Equals(
                                 cmisType.BaseId,
                                 "cmis:document",
                                 StringComparison.OrdinalIgnoreCase);

            envelopes.Add(BuildEnvelope(
                obj,
                customProps,
                typeDefs,
                isDocument));
        }

        return envelopes;
    }

    private static CmisObjectEnvelope BuildEnvelope(
        CmisObject obj,
        List<ObjectProperty> customProps,
        List<TypePropertyDefinition> typeDefs,
        bool isDocument)
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

        if (isDocument)
        {
            AddSystem("cmis:contentStreamLength", "contentStreamLength", "integer", obj.ContentStreamLength);
            AddSystem("cmis:contentStreamMimeType", "contentStreamMimeType", "string", obj.MimeType);
        }

        foreach (var group in customProps.GroupBy(p => p.PropertyId))
        {
            var def = typeDefs.FirstOrDefault(d =>
                d.PropertyId.Equals(
                    group.Key,
                    StringComparison.OrdinalIgnoreCase));

            var ordered = group.OrderBy(p => p.SortOrder).ToList();
            var cardinality = def?.Cardinality ??
                              (ordered.Count > 1 ? "multi" : "single");

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
        var typeDefs =
            await GetEffectiveTypePropertyDefinitionsAsync(typeId);

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
        var typeDefs =
            await GetEffectiveTypePropertyDefinitionsAsync(typeId);

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
        string parentId,
        string name,
        string mimeType,
        byte[] content,
        string typeId = "cmis:document",
        string? propertiesJson = null)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            typeId = "cmis:document";
        }

        // -----------------------------------
        // Validate requested CMIS type
        // -----------------------------------

        var requestedType = await _context.Types
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == typeId);

        if (requestedType == null)
        {
            throw new KeyNotFoundException(
                $"CMIS type '{typeId}' was not found.");
        }

        // A document can only use cmis:document or
        // a custom type derived from cmis:document.
        if (!string.Equals(
                requestedType.BaseId,
                "cmis:document",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Type '{typeId}' is not a document type.");
        }

        // -----------------------------------
        // Validate parent folder
        // -----------------------------------

        var parentFolder = await _context.Objects
            .FirstOrDefaultAsync(o => o.Id == parentId);

        if (parentFolder == null)
        {
            throw new KeyNotFoundException(
                $"Parent folder with ID '{parentId}' was not found.");
        }

        var parentType = await _context.Types
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == parentFolder.TypeId);

        if (parentType == null ||
            !string.Equals(
                parentType.BaseId,
                "cmis:folder",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The parent object must be a folder.");
        }

        // -----------------------------------
        // Prevent duplicate names
        // -----------------------------------

        var existing = await _context.Objects
            .FirstOrDefaultAsync(o =>
                o.ParentId == parentId &&
                o.Name == name);

        if (existing != null)
        {
            throw new InvalidOperationException(
                $"An object named '{name}' already exists in this folder.");
        }

        // -----------------------------------
        // Build document
        // -----------------------------------

        var parentPath = parentFolder.Path ?? "/";

        var fullPath = parentPath == "/"
            ? $"/{name}"
            : $"{parentPath}/{name}";

        var newDoc = new CmisObject
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,

            // IMPORTANT:
            // can now be custom:facture, custom:loan, etc.
            TypeId = typeId,

            ParentId = parentId,
            Path = fullPath,
            MimeType = mimeType,
            ContentStream = content,
            ContentStreamLength = content.Length,
            CreatedBy = "admin",
            CreationDate = DateTime.UtcNow,
            LastModificationDate = DateTime.UtcNow
        };

        // Validates own + inherited properties.
        var customPropertyRows =
            await ValidateAndBuildCustomPropertiesAsync(
                typeId,
                newDoc.Id,
                propertiesJson,
                isCreate: true);

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

        if (!await IsTypeDerivedFromAsync(obj.TypeId, "cmis:document"))
        {
            throw new InvalidOperationException(
                "setContentStream can only be used on documents.");
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

        if (!await IsTypeDerivedFromAsync(
                targetFolder.TypeId,
                "cmis:folder"))
        {
            throw new InvalidOperationException(
                "Target object is not a folder.");
        }

        // Prevent moving a folder into one of its own descendants (would create a cycle)
        var movingObjectIsFolder =
            await IsTypeDerivedFromAsync(
                obj.TypeId,
                "cmis:folder");

        if (movingObjectIsFolder &&
            (targetFolder.Path == obj.Path ||
             targetFolder.Path.StartsWith(obj.Path + "/")))
        {
            throw new InvalidOperationException(
                "Cannot move a folder into its own descendant.");
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

        var isFolder =
            await IsTypeDerivedFromAsync(
                cmisObj.TypeId,
                "cmis:folder");

        if (isFolder && cmisObj.Children.Any())
        {
            throw new InvalidOperationException(
                "Cannot delete a folder that contains child objects. " +
                "Delete the contents first, or use deleteTree.");
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

        if (!await IsTypeDerivedFromAsync(
                folder.TypeId,
                "cmis:folder"))
        {
            throw new InvalidOperationException(
                "deleteTree can only be used on folders.");
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

        // A query on a parent type also includes objects of its derived types.
        // Example: FROM cmis:document includes custom:facture and custom:loan.
        var queryTypeIds = await GetTypeAndDescendantIdsAsync(parsed.TypeId);

        var candidates = await _context.Objects
            .Where(o => queryTypeIds.Contains(o.TypeId))
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