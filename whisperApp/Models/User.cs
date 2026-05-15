using SQLite;

namespace WhisperOfflineApp.Models;

[Table("Users")]
public class User
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Unique, NotNull]
    public string Username { get; set; } = string.Empty;

    [NotNull]
    public string PasswordHash { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string SessionToken { get; set; } = string.Empty;

    public DateTime SessionExpiry { get; set; }
}