namespace CMIS_IyaSoft.Entities;

/// <summary>
/// One property inside the CMIS properties envelope: { id, localName, type, cardinality, value }.
/// Value is a plain string for single-valued properties, or a JSON array of strings for
/// multi-valued ones - matching the browser-binding convention the note de besoin describes.
/// </summary>
public class CmisPropertyValue
{
    public string Id { get; set; } = string.Empty;
    public string LocalName { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public string Cardinality { get; set; } = "single";
    public object? Value { get; set; }
}

/// <summary>
/// The full object representation returned by cmisselector=object|children|parents.
/// Wraps every property (system + custom) in the typed envelope instead of
/// serializing the raw CmisObject POCO fields directly.
/// </summary>
public class CmisObjectEnvelope
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TypeId { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string Path { get; set; } = string.Empty;

    // Keyed by property id, e.g. "cmis:name", "custom:department"
    public Dictionary<string, CmisPropertyValue> Properties { get; set; } = new();
}
