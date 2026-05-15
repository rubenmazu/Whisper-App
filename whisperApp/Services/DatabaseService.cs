using SQLite;
using WhisperOfflineApp.Models;
using BCrypt.Net;

namespace WhisperOfflineApp.Services;

public class DatabaseService : IDatabaseService
{
    private SQLiteAsyncConnection? _database;
    private readonly string _dbPath;
    private bool _initialized = false;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public DatabaseService()
    {
        // Stochează baza de date în directorul privat al aplicației
        _dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "whisper_offline.db3"
        );
    }

    public async Task InitializeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            // Creează directorul dacă nu există
            var directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _database = new SQLiteAsyncConnection(_dbPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

            // Creează tabelele dacă nu există
            await _database.CreateTableAsync<User>();
            await _database.CreateTableAsync<Transcription>();

            _initialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Database error: {ex.Message}");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_initialized)
            await InitializeAsync();
    }

    public async Task<AppResult<User>> CreateUserAsync(string username, string email, string password)
    {
        try
        {
            await EnsureInitializedAsync();

            // Verifică dacă username-ul există deja
            var existing = await _database!.Table<User>()
                .Where(u => u.Username == username)
                .FirstOrDefaultAsync();

            if (existing != null)
                return AppResult<User>.Failure("Username-ul există deja.");

            // Hash parola cu SHA256 + Salt
            var passwordHash = HashPassword(password);

            var user = new User
            {
                Username = username,
                Email = email,
                PasswordHash = passwordHash,
                CreatedAt = DateTime.Now,
                SessionToken = string.Empty,
                SessionExpiry = DateTime.MinValue
            };

            await _database.InsertAsync(user);
            return AppResult<User>.Success(user);
        }
        catch (Exception ex)
        {
            return AppResult<User>.Failure($"Eroare la creare cont: {ex.Message}");
        }
    }

    // Hash securizat cu SHA256 + Salt
    private string HashPassword(string password)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var salt = "WhisperApp_SecureSalt_2024"; // În producție, folosește un salt unic per user
        var saltedPassword = password + salt;
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(saltedPassword));
        return Convert.ToBase64String(hashBytes);
    }

    public async Task<AppResult<User>> GetUserByUsernameAsync(string username)
    {
        try
        {
            await EnsureInitializedAsync();
            var user = await _database!.Table<User>()
                .Where(u => u.Username == username)
                .FirstOrDefaultAsync();

            if (user == null)
                return AppResult<User>.Failure("Utilizatorul nu a fost găsit.");

            return AppResult<User>.Success(user);
        }
        catch (Exception ex)
        {
            return AppResult<User>.Failure($"Eroare: {ex.Message}");
        }
    }

    public async Task<AppResult<bool>> UpdateSessionAsync(int userId, string token, DateTime expiry)
    {
        try
        {
            await EnsureInitializedAsync();
            await _database!.ExecuteAsync(
                "UPDATE Users SET SessionToken = ?, SessionExpiry = ? WHERE Id = ?",
                token, expiry, userId);
            return AppResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return AppResult<bool>.Failure($"Eroare: {ex.Message}");
        }
    }

    public async Task<AppResult<User>> GetUserBySessionTokenAsync(string token)
    {
        try
        {
            await EnsureInitializedAsync();
            var user = await _database!.Table<User>()
                .Where(u => u.SessionToken == token)
                .FirstOrDefaultAsync();

            if (user == null || user.SessionExpiry < DateTime.Now)
                return AppResult<User>.Failure("Sesiune invalidă sau expirată.");

            return AppResult<User>.Success(user);
        }
        catch (Exception ex)
        {
            return AppResult<User>.Failure($"Eroare: {ex.Message}");
        }
    }

    public async Task<AppResult<bool>> SaveTranscriptionAsync(Transcription transcription)
    {
        try
        {
            await EnsureInitializedAsync();
            await _database!.InsertAsync(transcription);
            return AppResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return AppResult<bool>.Failure($"Eroare: {ex.Message}");
        }
    }

    public async Task<AppResult<List<Transcription>>> GetTranscriptionsAsync(int userId)
    {
        try
        {
            await EnsureInitializedAsync();
            var transcriptions = await _database!.Table<Transcription>()
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
            return AppResult<List<Transcription>>.Success(transcriptions);
        }
        catch (Exception ex)
        {
            return AppResult<List<Transcription>>.Failure($"Eroare: {ex.Message}");
        }
    }

    public async Task<AppResult<bool>> DeleteTranscriptionAsync(int id)
    {
        try
        {
            await EnsureInitializedAsync();
            await _database!.DeleteAsync<Transcription>(id);
            return AppResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return AppResult<bool>.Failure($"Eroare: {ex.Message}");
        }
    }

    public async Task<AppResult<bool>> LogoutAsync(int userId)
    {
        try
        {
            await EnsureInitializedAsync();
            await _database!.ExecuteAsync(
                "UPDATE Users SET SessionToken = '', SessionExpiry = ? WHERE Id = ?",
                DateTime.MinValue, userId);
            return AppResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return AppResult<bool>.Failure($"Eroare: {ex.Message}");
        }
    }
}