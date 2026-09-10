using Backend.DTOs;

namespace Backend.Services;

public interface IAuthService
{
    Task<CompanyLoginResponse> CompanyLoginAsync(CompanyLoginRequest request);
    Task<UserLoginResponse> UserLoginAsync(UserLoginRequest request, Guid sessionId);
    Task<IndusLoginResponse> IndusLoginAsync(IndusLoginRequest request);
}
