using CMIS_IyaSoft.Entities;

namespace CMIS_IyaSoft.Services;

public interface ICmisService
{
    Task<IEnumerable<CmisType>> GetTypesAsync();
    Task<CmisType?> GetTypeDefinitionAsync(string typeId);
    Task<IEnumerable<CmisObject>> GetChildrenAsync(string folderId);
    Task<CmisObject?> GetObjectByIdAsync(string objectId);
    Task<IEnumerable<CmisObject>> GetParentsAsync(string objectId);
    Task<(byte[]? Content, string? MimeType, string Name)?> GetContentStreamAsync(string objectId);

    // Write operations
    Task<CmisObject> CreateDocumentAsync(string parentId, string name, string mimeType, byte[] content);
    Task<CmisObject> CreateFolderAsync(string parentId, string name);
    Task<CmisObject> UpdateObjectAsync(string objectId, string newName);
    Task<CmisObject> MoveObjectAsync(string objectId, string targetFolderId);

    // Deletion & Search
    Task<bool> DeleteObjectAsync(string objectId);
    Task<int> DeleteTreeAsync(string folderId);
    Task<IEnumerable<CmisObject>> SearchObjectsAsync(string searchTerm);
    Task<(IEnumerable<CmisObject> Results, int NumItems, bool HasMoreItems)> ExecuteQueryAsync(
        string statement, int maxItems = 100, int skipCount = 0);
}
