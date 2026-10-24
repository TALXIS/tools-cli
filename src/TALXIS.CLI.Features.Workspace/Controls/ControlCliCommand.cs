using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Workspace.Controls;

/// <summary>
/// Custom control (PCF) operations on forms in the local workspace.
/// Unlike <c>component create</c>, these commands are manifest-driven rather than
/// template-driven: the parameter schema comes from the control's own
/// <c>ControlManifest.xml</c> at run time, so no per-control template is needed.
/// </summary>
[CliCommand(
    Description = "Attach custom controls (PCF) to forms in your local workspace",
    Name = "control",
    Children = new[]
    {
        typeof(ControlAttachCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None)]
public class ControlCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
