namespace HybridCache.LuaScripting;

/// <summary>
/// Defines a Lua script executor for distributed cache operations.
/// </summary>
public interface ILuaScriptExecutor
{
    /// <summary>
    /// Executes a Lua script on the distributed cache.
    /// </summary>
    /// <param name="script">The Lua script to execute.</param>
    /// <param name="keys">Cache keys to pass to the script (accessed via KEYS[] in Lua).</param>
    /// <param name="values">Values to pass to the script (accessed via ARGV[] in Lua).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the script execution.</returns>
    Task<LuaScriptResult> ExecuteAsync(
        string script,
        string[]? keys = null,
        object[]? values = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a Lua script and returns a typed result.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="script">The Lua script to execute.</param>
    /// <param name="keys">Cache keys to pass to the script (accessed via KEYS[] in Lua).</param>
    /// <param name="values">Values to pass to the script (accessed via ARGV[] in Lua).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The typed result of the script execution.</returns>
    Task<LuaScriptResult<T>> ExecuteAsync<T>(
        string script,
        string[]? keys = null,
        object[]? values = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares a Lua script for repeated execution (cached on the server).
    /// </summary>
    /// <param name="script">The Lua script to prepare.</param>
    /// <returns>A prepared script that can be executed multiple times efficiently.</returns>
    Task<IPreparedLuaScript> PrepareAsync(string script);
}

/// <summary>
/// Represents a prepared Lua script that can be executed multiple times.
/// </summary>
public interface IPreparedLuaScript
{
    /// <summary>
    /// Gets the script content.
    /// </summary>
    string Script { get; }

    /// <summary>
    /// Executes the prepared script.
    /// </summary>
    Task<LuaScriptResult> ExecuteAsync(
        string[]? keys = null,
        object[]? values = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the prepared script and returns a typed result.
    /// </summary>
    Task<LuaScriptResult<T>> ExecuteAsync<T>(
        string[]? keys = null,
        object[]? values = null,
        CancellationToken cancellationToken = default);
}
