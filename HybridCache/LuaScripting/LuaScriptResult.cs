namespace HybridCache.LuaScripting;

/// <summary>
/// Represents the result of a Lua script execution.
/// </summary>
public class LuaScriptResult
{
    /// <summary>
    /// Gets or sets whether the script executed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the raw result from the script execution.
    /// </summary>
    public object? Result { get; set; }

    /// <summary>
    /// Gets or sets an error message if the script failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static LuaScriptResult CreateSuccess(object? result = null) =>
        new() { Success = true, Result = result };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static LuaScriptResult CreateFailure(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    /// <summary>
    /// Gets the result as a specific type.
    /// </summary>
    public T? GetResult<T>()
    {
        if (Result is null)
            return default;

        if (Result is T typedResult)
            return typedResult;

        try
        {
            return (T)Convert.ChangeType(Result, typeof(T));
        }
        catch
        {
            return default;
        }
    }
}

/// <summary>
/// Represents a typed result of a Lua script execution.
/// </summary>
public class LuaScriptResult<T> : LuaScriptResult
{
    /// <summary>
    /// Gets or sets the typed result value.
    /// </summary>
    public new T? Result { get; set; }

    /// <summary>
    /// Creates a successful typed result.
    /// </summary>
    public static LuaScriptResult<T> CreateSuccess(T result) =>
        new() { Success = true, Result = result };

    /// <summary>
    /// Creates a failed typed result.
    /// </summary>
    public new static LuaScriptResult<T> CreateFailure(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}
