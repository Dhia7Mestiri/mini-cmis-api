namespace CMIS_IyaSoft.Entities;

public class CmisTypePropertyRequest
{
    public string PropertyId { get; set; } = string.Empty;
    public string LocalName { get; set; } = string.Empty;
    public string PropertyType { get; set; } = "string";
    public string Cardinality { get; set; } = "single";
    public string Updatability { get; set; } = "readwrite";
    public bool Required { get; set; }
}

public class CreateCmisTypeRequest
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ParentTypeId { get; set; } = string.Empty;
    public List<CmisTypePropertyRequest> PropertyDefinitions { get; set; } = new();
}

public class UpdateCmisTypeRequest
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? ParentTypeId { get; set; }
    public List<CmisTypePropertyRequest>? PropertyDefinitions { get; set; }
}