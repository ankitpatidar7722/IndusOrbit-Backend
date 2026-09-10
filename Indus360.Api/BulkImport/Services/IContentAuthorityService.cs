using Backend.DTOs;

namespace Backend.Services;

public interface IContentAuthorityService
{
    Task<List<ContentAuthorityRowDto>> GetContentAuthorityDataAsync();
    Task<ContentAuthoritySaveResult> SaveContentAuthorityAsync(ContentAuthoritySaveRequest request);
    Task<ContentAuthoritySaveResult> UpdateContentDetailsAsync(List<string> contentNames);
    Task<ContentAuthoritySaveResult> UpdateKeylineDetailsAsync(List<string> contentNames);
}
