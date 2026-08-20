using CMIS_IyaSoft.Entities;

namespace CMIS_IyaSoft.Services;

public interface ICmisService
{
    Task<IEnumerable<CmisType>> GetTypesAsync();
    Task<IEnumerable<CmisType>> GetTypeChildrenAsync(string? typeId);

    // Full merged type definition (system properties + DB-driven custom ones).
    Task<CmisTypeDefinition?> GetTypeDefinitionAsync(string typeId);

    // Dynamic CMIS type management.
    Task<CmisTypeDefinition> CreateTypeAsync(CreateCmisTypeRequest request);
    Task<CmisTypeDefinition> UpdateTypeAsync(string typeId, UpdateCmisTypeRequest request);
    Task DeleteTypeAsync(string typeId);

    Task<IEnumerable<CmisObject>> GetChildrenAsync(string folderId);
    Task<CmisObject?> GetObjectByIdAsync(string objectId);
    Task<IEnumerable<CmisObject>> GetParentsAsync(string objectId);
    Task<(byte[]? Content, string? MimeType, string Name)?> GetContentStreamAsync(string objectId);

    // Properties-envelope reads - what the controller actually returns to clients.
    Task<CmisObjectEnvelope?> GetObjectEnvelopeAsync(string objectId);
    Task<IEnumerable<CmisObjectEnvelope>> GetChildrenEnvelopesAsync(string folderId);
    Task<IEnumerable<CmisObjectEnvelope>> GetParentsEnvelopesAsync(string objectId);

    // Write operations. propertiesJson is an optional JSON object of custom
    // property id -> value (or array of values for multi-valued properties),
    // e.g. {"custom:department":"Finance"}.
    Task<CmisObject> CreateDocumentAsync(
        string parentId,
        string name,
        string mimeType,
        byte[] content,
        string typeId = "cmis:document",
        string? propertiesJson = null);
    Task<CmisObject> CreateFolderAsync(string parentId, string name, string? propertiesJson = null);
    Task<CmisObject> UpdateObjectAsync(string objectId, string? newName, string? propertiesJson = null);
    Task<CmisObject> MoveObjectAsync(string objectId, string targetFolderId);
    Task<CmisObject> SetContentStreamAsync(string objectId, string mimeType, byte[] content);

    // Deletion & Search
    Task<bool> DeleteObjectAsync(string objectId);
    Task<int> DeleteTreeAsync(string folderId);
    Task<IEnumerable<CmisObject>> SearchObjectsAsync(string searchTerm);
    Task<(IEnumerable<CmisObject> Results, int NumItems, bool HasMoreItems)> ExecuteQueryAsync(
        string statement, int maxItems = 100, int skipCount = 0);
}