namespace CMIS_IyaSoft.Entities;

/// <summary>
/// One row = one value of one custom (non-system) property on a CmisObject.
/// Multi-valued properties are represented as several rows sharing the same
/// ObjectId + PropertyId, ordered by SortOrder.
///
/// System properties (cmis:name, cmis:objectId, dates, etc.) stay as real
/// columns on CmisObject - this table is only for the properties defined
/// per-type beyond the CMIS base set (e.g. "custom:invoiceNumber").
/// </summary>
public class ObjectProperty
{
    public int Id { get; set; }

    // FK to CmisObject.Id (not a navigation property on purpose - keeps this
    // table fully independent/additive, no FK constraint required at the DB level).
    public string ObjectId { get; set; } = string.Empty;

    // e.g. "custom:invoiceNumber"
    public string PropertyId { get; set; } = string.Empty;

    // string | integer | datetime | boolean | id
    public string PropertyType { get; set; } = "string";

    // single | multi
    public string Cardinality { get; set; } = "single";

    // Always stored as text, parsed/cast according to PropertyType on read.
    public string Value { get; set; } = string.Empty;

    // Preserves ordering for multi-valued properties. Irrelevant for single-valued ones.
    public int SortOrder { get; set; } = 0;
}
