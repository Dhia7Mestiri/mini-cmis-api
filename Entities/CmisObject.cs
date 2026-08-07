namespace CMIS_IyaSoft.Entities;

public class CmisObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;

    // "cmis:folder" or "cmis:document"
    public string TypeId { get; set; } = "cmis:document";

    public string? ParentId { get; set; }
    public string Path { get; set; } = string.Empty;

    // Document specific fields
    public byte[]? ContentStream { get; set; }
    public string? MimeType { get; set; }
    public long? ContentStreamLength { get; set; }

    // System Metadata
    public string CreatedBy { get; set; } = "admin";
    public DateTime CreationDate { get; set; } = DateTime.UtcNow;
    public DateTime LastModificationDate { get; set; } = DateTime.UtcNow;
}