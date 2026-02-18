using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using System.IO;
using TALXIS.CLI.Workspace.Upgrade;

namespace TALXIS.CLI.Workspace;

[CliCommand(
    Description = "Upgrade project files to SDK-style format while preserving custom references and backups.",
    Name = "upgrade",
    Children = new[] { typeof(ProjectUpgradeCliCommand) })]
public class UpgradeCliCommand
{
    [CliArgument(Name = "path", Description = "Path to a .csproj/.cdsproj file or directory to upgrade.")]
    public required string TargetPath { get; set; }

    [CliOption(Description = "Skip creating .backup files before rewriting.")]
    public bool NoBackup { get; set; }

    public int Run()
    {
        return RunInternal(TargetPath, NoBackup);
    }

    [CliCommand(
        Description = "Upgrade project files (explicit project subcommand).",
        Name = "project")]
    public class ProjectUpgradeCliCommand
    {
        [CliArgument(Name = "path", Description = "Path to a .csproj/.cdsproj file or directory to upgrade.")]
        public required string TargetPath { get; set; }

        [CliOption(Description = "Skip creating .backup files before rewriting.")]
        public bool NoBackup { get; set; }

        public int Run()
        {
            return RunInternal(TargetPath, NoBackup);
        }
    }

    private static int RunInternal(string targetPath, bool noBackup)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var templatesBasePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Upgrade", "Templates");
        var runner = new ProjectUpgradeRunner(loggerFactory, templatesBasePath, createBackup: !noBackup);
        return runner.Run(targetPath);
    }
}
