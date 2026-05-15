using WhisperOfflineApp.Models;

namespace WhisperOfflineApp.Services;

public interface IDatabaseService
{
    Task InitializeAsync();
    Task<AppResult<User>> CreateUserAsync(string username, string email, string password);
    Task<AppResult<User>> GetUserByUsernameAsync(string username);
    Task<AppResult<bool>> UpdateSessionAsync(int userId, string token, DateTime expiry);
    Task<AppResult<User>> GetUserBySessionTokenAsync(string token);
    Task<AppResult<bool>> SaveTranscriptionAsync(Transcription transcription);
    Task<AppResult<List<Transcription>>> GetTranscriptionsAsync(int userId);
    Task<AppResult<bool>> DeleteTranscriptionAsync(int id);
    Task<AppResult<bool>> LogoutAsync(int userId);
}