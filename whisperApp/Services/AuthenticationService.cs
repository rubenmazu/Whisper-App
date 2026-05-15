using WhisperOfflineApp.Models;

namespace WhisperOfflineApp.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly IDatabaseService _databaseService;
    private const string SESSION_TOKEN_KEY = "session_token";
    private User? _currentUser;

    public User? CurrentUser => _currentUser;
    public bool IsLoggedIn => _currentUser != null;

    public AuthenticationService(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<AppResult<User>> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return AppResult<User>.Failure("Username și parola sunt obligatorii.");

        var userResult = await _databaseService.GetUserByUsernameAsync(username);
        if (!userResult.IsSuccess)
            return AppResult<User>.Failure("Username sau parolă incorectă.");

        var user = userResult.Value!;

        // Verifică parola cu SHA256
        var passwordHash = HashPassword(password);
        if (passwordHash != user.PasswordHash)
            return AppResult<User>.Failure("Username sau parolă incorectă.");

        // Creează sesiune
        var sessionToken = GenerateSecureToken();
        var sessionExpiry = DateTime.Now.AddDays(30); // Sesiune 30 zile

        await _databaseService.UpdateSessionAsync(user.Id, sessionToken, sessionExpiry);

        // Salvează token local (Secure Storage = Keystore pe Android)
        await SecureStorage.SetAsync(SESSION_TOKEN_KEY, sessionToken);

        user.SessionToken = sessionToken;
        _currentUser = user;

        return AppResult<User>.Success(user);
    }

    public async Task<AppResult<User>> RegisterAsync(string username, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            return AppResult<User>.Failure("Username-ul este obligatoriu.");

        if (password.Length < 8)
            return AppResult<User>.Failure("Parola trebuie să aibă cel puțin 8 caractere.");

        return await _databaseService.CreateUserAsync(username, email, password);
    }

    public async Task<AppResult<bool>> LogoutAsync()
    {
        if (_currentUser == null)
            return AppResult<bool>.Success(true);

        await _databaseService.LogoutAsync(_currentUser.Id);
        SecureStorage.Remove(SESSION_TOKEN_KEY);
        _currentUser = null;

        return AppResult<bool>.Success(true);
    }

    public async Task<AppResult<User>> GetCurrentUserAsync()
    {
        if (_currentUser != null)
            return AppResult<User>.Success(_currentUser);

        // Încearcă să restaureze sesiunea
        try
        {
            var token = await SecureStorage.GetAsync(SESSION_TOKEN_KEY);
            if (string.IsNullOrEmpty(token))
                return AppResult<User>.Failure("Nu există sesiune activă.");

            var result = await _databaseService.GetUserBySessionTokenAsync(token);
            if (result.IsSuccess)
            {
                _currentUser = result.Value;
                return result;
            }

            SecureStorage.Remove(SESSION_TOKEN_KEY);
            return AppResult<User>.Failure("Sesiune expirată.");
        }
        catch
        {
            return AppResult<User>.Failure("Eroare la restaurarea sesiunii.");
        }
    }

    // Hash securizat cu SHA256 + Salt (același ca în DatabaseService)
    private string HashPassword(string password)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var salt = "WhisperApp_SecureSalt_2024";
        var saltedPassword = password + salt;
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(saltedPassword));
        return Convert.ToBase64String(hashBytes);
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}