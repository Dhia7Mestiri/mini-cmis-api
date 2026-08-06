namespace CMIS_IyaSoft.Entities;

public class CmisType
{
    public string Id { get; set; } = string.Empty; // Primary Key e.g., "cmis:folder", "cmis:document"
    public string LocalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string BaseId { get; set; } = string.Empty;

    public bool IsCreatable { get; set; } = true;
    public bool IsQueryable { get; set; } = true;
    public bool IsFulltextIndexed { get; set; } = false;

    // Relationships
    public ICollection<CmisObject> Objects { get; set; } = new List<CmisObject>();
}