using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Executes CLI commands for testing purposes.
/// </summary>
public static class CliRunner
{
    private static readonly string CliProject = GetCliProjectPath();

    public static async Task<string> RunAsync(string command)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(CliProject);
        psi.ArgumentList.Add("--");
        
        var commandArgs = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var arg in commandArgs)
        {
            psi.ArgumentList.Add(arg);
        }
        
        using var process = Process.Start(psi);
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        if (process.ExitCode != 0)
        {
            var errorMessage = $"CLI command failed: {command}\nExit code: {process.ExitCode}";
            if (!string.IsNullOrEmpty(error))
                errorMessage += $"\nError: {error}";
            if (!string.IsNullOrEmpty(output))
                errorMessage += $"\nOutput: {output}";
                
            throw new InvalidOperationException(errorMessage);
        }
            
        return output;
    }

    private static string GetCliProjectPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new System.IO.DirectoryInfo(baseDir);
        
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "TALXIS.CLI.sln")))
        {
            dir = dir.Parent;
        }
        
        if (dir == null)
            throw new InvalidOperationException("Could not find repository root");
            
        return System.IO.Path.Combine(dir.FullName, "src", "TALXIS.CLI", "TALXIS.CLI.csproj");
    }
}
