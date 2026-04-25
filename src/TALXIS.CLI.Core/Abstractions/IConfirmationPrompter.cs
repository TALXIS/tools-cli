namespace TALXIS.CLI.Core.Abstractions;

/// <summary>
/// Prompts the user for a yes/no confirmation in an interactive terminal session.
/// <para>
/// This abstraction isolates direct console I/O (<c>Console.ReadLine</c>) behind a
/// testable interface, satisfying the <c>BannedSymbols.txt</c> rule that forbids raw
/// console reads in command code.
/// </para>
/// </summary>
public interface IConfirmationPrompter
{
    /// <summary>
    /// Displays <paramref name="message"/> and waits for the user to type <c>y</c>
    /// (case-insensitive) to confirm. Any other input (including empty / Enter) is
    /// treated as rejection.
    /// </summary>
    /// <returns><c>true</c> if the user confirmed; <c>false</c> otherwise.</returns>
    Task<bool> ConfirmAsync(string message, CancellationToken ct = default);
}
