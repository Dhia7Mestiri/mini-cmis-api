namespace CMIS_IyaSoft.Entities;

public class CmisObject
{
    // Primary Key
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Core Properties
    public string Name { get; set; } = string.Empty;

    // Relationship to Type
    public string TypeId { get; set; } = string.Empty;
    public CmisType Type { get; set; } = null!;

    // Self-Referencing Hierarchy (Parent-Child)
    public string? ParentId { get; set; }
    public CmisObject? Parent { get; set; }
    public ICollection<CmisObject> Children { get; set; } = new List<CmisObject>();
}