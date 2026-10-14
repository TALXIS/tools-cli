using DotMake.CommandLine;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Security;

public abstract class SecurityScopedCliCommand : ProfiledCliCommand
{
    [CliOption(
        Name = "--environment",
        Description = "Scope this operation to a Dataverse environment by environment ID. When omitted, txc uses the active environment connection if the resolved profile is already connected to one; otherwise the tenant-wide security implementation runs when this command supports it.",
        Required = false)]
    public Guid? Environment { get; set; }
}
