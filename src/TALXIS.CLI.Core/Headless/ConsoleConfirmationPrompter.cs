using TALXIS.CLI.Core.Abstractions;

namespace TALXIS.CLI.Core.Headless;

/// <summary>
/// Interactive TTY implementation of <see cref="IConfirmationPrompter"/>.
/// Writes the prompt to stderr (so it doesn't pollute JSON stdout) and
/// reads the response from stdin.
/// <para>
/// This is the single sanctioned location for <c>Console.ReadLine</c>
/// in the codebase — all other usages are banned by <c>BannedSymbols.txt</c>.
/// </para>
/// </summary>
internal sealed class ConsoleConfirmationPrompter : IConfirmationPrompter
{
    public async Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        Console.Error.Write($"{message} [y/N]: ");

        // Read stdin on a background thread so the cancellation token can
        // abort the wait if the caller cancels before the user types.
        var response = await Task.Run(() => Console.ReadLine(), ct).ConfigureAwait(false);
        return response?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;
    }
}
