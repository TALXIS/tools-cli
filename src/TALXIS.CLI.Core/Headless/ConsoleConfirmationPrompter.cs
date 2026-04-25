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
    public Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
    {
        Console.Error.Write($"{message} [y/N]: ");
        var response = Console.ReadLine();
        var confirmed = response?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;
        return Task.FromResult(confirmed);
    }
}
