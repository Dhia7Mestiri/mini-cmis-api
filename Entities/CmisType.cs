namespace CMIS_IyaSoft.Entities;

// UNCHANGED from your original - no migration needed.
public class CmisType
{
    public string Id { get; set; } = string.Empty;
    public string BaseId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class CmisPropertyDefinition
{
    public string Id { get; set; } = string.Empty;
    public string LocalName { get; set; } = string.Empty;
    public string PropertyType { get; set; } = "string"; // string | integer | datetime | boolean | id
    public string Cardinality { get; set; } = "single";  // single | multi
    public string Updatability { get; set; } = "readonly"; // readonly | oncreate | readwrite
    public bool Required { get; set; } = false;
}

/// <summary>
/// Full type definition returned by cmisselector=typeDefinition.
/// Computed on the fly from the stored CmisType + a static property set per base type -
/// no schema change / migration required.
/// </summary>
public class CmisTypeDefinition
{
    public string Id { get; set; } = string.Empty;
    public string BaseId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<CmisPropertyDefinition> PropertyDefinitions { get; set; } = new();

    public static List<CmisPropertyDefinition> BaseProperties() => new()
    {
        new() { Id = "cmis:objectId", LocalName = "objectId", PropertyType = "id", Cardinality = "single", Updatability = "readonly", Required = true },
        new() { Id = "cmis:name", LocalName = "name", PropertyType = "string", Cardinality = "single", Updatability = "readwrite", Required = true },
        new() { Id = "cmis:objectTypeId", LocalName = "objectTypeId", PropertyType = "id", Cardinality = "single", Updatability = "oncreate", Required = true },
        new() { Id = "cmis:parentId", LocalName = "parentId", PropertyType = "id", Cardinality = "single", Updatability = "readonly", Required = false },
        new() { Id = "cmis:path", LocalName = "path", PropertyType = "string", Cardinality = "single", Updatability = "readonly", Required = false },
        new() { Id = "cmis:createdBy", LocalName = "createdBy", PropertyType = "string", Cardinality = "single", Updatability = "readonly", Required = false },
        new() { Id = "cmis:creationDate", LocalName = "creationDate", PropertyType = "datetime", Cardinality = "single", Updatability = "readonly", Required = false },
        new() { Id = "cmis:lastModificationDate", LocalName = "lastModificationDate", PropertyType = "datetime", Cardinality = "single", Updatability = "readonly", Required = false },
    };

    public static List<CmisPropertyDefinition> DocumentProperties()
    {
        var props = BaseProperties();
        props.AddRange(new List<CmisPropertyDefinition>
        {
            new() { Id = "cmis:contentStreamLength", LocalName = "contentStreamLength", PropertyType = "integer", Cardinality = "single", Updatability = "readonly", Required = false },
            new() { Id = "cmis:contentStreamMimeType", LocalName = "contentStreamMimeType", PropertyType = "string", Cardinality = "single", Updatability = "readonly", Required = false },
        });
        return props;
    }

    /// <summary>
    /// Builds the fixed system property set for a type (no DB access - cmis:name,
    /// cmis:objectId, etc. are the same for every repository, always).
    /// </summary>
    public static CmisTypeDefinition FromCmisType(CmisType type)
    {
        var props = string.Equals(type.Id, "cmis:folder", StringComparison.OrdinalIgnoreCase)
            ? BaseProperties()
            : DocumentProperties();

        return new CmisTypeDefinition
        {
            Id = type.Id,
            BaseId = type.BaseId,
            DisplayName = type.DisplayName,
            Description = type.Description,
            PropertyDefinitions = props
        };
    }

    /// <summary>
    /// Merges DB-driven custom property definitions (per-type, from TypePropertyDefinition)
    /// on top of the fixed system set built by FromCmisType. This is what
    /// cmisselector=typeDefinition actually returns.
    /// </summary>
    public static CmisTypeDefinition WithCustomProperties(
        CmisType type, IEnumerable<TypePropertyDefinition> customProps)
    {
        var definition = FromCmisType(type);

        definition.PropertyDefinitions.AddRange(customProps.Select(cp => new CmisPropertyDefinition
        {
            Id = cp.PropertyId,
            LocalName = cp.LocalName,
            PropertyType = cp.PropertyType,
            Cardinality = cp.Cardinality,
            Updatability = cp.Updatability,
            Required = cp.Required
        }));

        return definition;
    }
}
