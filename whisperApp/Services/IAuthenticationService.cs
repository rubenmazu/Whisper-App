using WhisperOfflineApp.Models;

namespace WhisperOfflineApp.Services;

public interface IAuthenticationService
{
    Task<AppResult<User>> LoginAsync(string username, string password);
    Task<AppResult<User>> RegisterAsync(string username, string email, string password);
    Task<AppResult<bool>> LogoutAsync();
    Task<AppResult<User>> GetCurrentUserAsync();
    bool IsLoggedIn { get; }
    User? CurrentUser { get; }
}