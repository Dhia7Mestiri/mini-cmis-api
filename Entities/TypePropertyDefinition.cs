namespace CMIS_IyaSoft.Entities;

/// <summary>
/// A single CUSTOM property definition attached to a type, stored in the DB.
/// This is what makes createDocument/createFolder/update able to validate
/// arbitrary per-type properties, and what cmisselector=typeDefinition merges
/// on top of the fixed CMIS base/document properties.
///
/// System properties (cmis:name, cmis:objectId, dates, etc.) are NOT stored
/// here - those stay hardcoded in CmisTypeDefinition.BaseProperties() /
/// DocumentProperties() because they never change and every object has them.
/// </summary>
public class TypePropertyDefinition
{
    public int Id { get; set; }

    // FK to CmisType.Id (e.g. "cmis:document")
    public string TypeId { get; set; } = string.Empty;

    // e.g. "custom:department"
    public string PropertyId { get; set; } = string.Empty;

    public string LocalName { get; set; } = string.Empty;

    // string | integer | datetime | boolean | id
    public string PropertyType { get; set; } = "string";

    // single | multi
    public string Cardinality { get; set; } = "single";

    // readonly | oncreate | readwrite
    public string Updatability { get; set; } = "readwrite";

    public bool Required { get; set; } = false;
}
