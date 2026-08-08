using CMIS_IyaSoft.Entities;

namespace CMIS_IyaSoft.Services;

public interface ICmisService
{
    Task<IEnumerable<CmisType>> GetTypesAsync();
    Task<IEnumerable<CmisObject>> GetChildrenAsync(string folderId);
    Task<CmisObject?> GetObjectByIdAsync(string objectId);
    Task<(byte[]? Content, string? MimeType, string Name)?> GetContentStreamAsync(string objectId);

    // Write operations
    Task<CmisObject> CreateDocumentAsync(string parentId, string name, string mimeType, byte[] content);
    Task<CmisObject> CreateFolderAsync(string parentId, string name);

    // Deletion & Search
    Task<bool> DeleteObjectAsync(string objectId);
    Task<IEnumerable<CmisObject>> SearchObjectsAsync(string searchTerm);
}