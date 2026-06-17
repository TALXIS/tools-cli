using System.Diagnostics;

namespace TALXIS.CLI.Features.Ui.Tests;

public record CliResult(int ExitCode, string Output, string Error);

public static class CliRunner
{
    private static readonly string CliProject = TestExecutionContext.GetProjectPath("src", "TALXIS.CLI", "TALXIS.CLI.csproj");

    public static Task<string> RunAsync(string[] args)
        => RunAsync(args, null);

    public static async Task<string> RunAsync(string[] args, IReadOnlyDictionary<string, string?>? env)
    {
        var result = await RunRawAsync(args, env);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"CLI command failed: {string.Join(' ', args)}\n{result.Error}\n{result.Output}");

        return result.Output;
    }

    public static async Task<CliResult> RunRawAsync(string[] args, IReadOnlyDictionary<string, string?>? env = null)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.GetCurrentDirectory(),
        };

        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(CliProject);
        psi.ArgumentList.Add("--configuration");
        psi.ArgumentList.Add(TestExecutionContext.BuildConfiguration);
        psi.ArgumentList.Add("--no-build");
        psi.ArgumentList.Add("--");
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        if (env is not null)
        {
            foreach (var kvp in env)
            {
                if (kvp.Value is null)
                    psi.Environment.Remove(kvp.Key);
                else
                    psi.Environment[kvp.Key] = kvp.Value;
            }
        }

        using var process = Process.Start(psi)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new CliResult(process.ExitCode, await outputTask, await errorTask);
    }
}
