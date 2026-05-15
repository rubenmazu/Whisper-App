namespace WhisperOfflineApp.Models;

/// <summary>
/// Pattern Result pentru gestionarea erorilor fără excepții
/// </summary>
public class AppResult<T>
{
    public bool IsSuccess { get; private set; }
    public T? Value { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;

    private AppResult() { }

    public static AppResult<T> Success(T value) => new()
    {
        IsSuccess = true,
        Value = value
    };

    public static AppResult<T> Failure(string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}