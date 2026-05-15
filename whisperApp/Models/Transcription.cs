using SQLite;

namespace WhisperOfflineApp.Models;

[Table("Transcriptions")]
public class Transcription
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull]
    public int UserId { get; set; }

    [NotNull]
    public string Text { get; set; } = string.Empty;

    public string AudioFilePath { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public double DurationSeconds { get; set; }

    public string Language { get; set; } = "ro";
}