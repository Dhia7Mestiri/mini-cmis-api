using CMIS_IyaSoft.Entities;

namespace CMIS_IyaSoft.Services;

public interface ICmisService
{
    Task<IEnumerable<CmisType>> GetTypesAsync();
    Task<IEnumerable<CmisObject>> GetChildrenAsync(string folderId);
    Task<CmisObject?> GetObjectByIdAsync(string objectId);

    // Add method to retrieve binary content stream
    Task<(byte[]? Content, string? MimeType, string Name)?> GetContentStreamAsync(string objectId);
}