using System;

namespace TALXIS.CLI.MCP
{
    public class CliCommandNameResolver
    {
        public string ResolveCommandName(Type cmdType, DotMake.CommandLine.CliCommandAttribute attr)
        {
            if (!string.IsNullOrWhiteSpace(attr.Name))
                return attr.Name.Trim();
            var cliNamer = new DotMake.CommandLine.CliNamer(
                attr.NameAutoGenerate,
                attr.NameCasingConvention,
                attr.NamePrefixConvention,
                attr.ShortFormAutoGenerate,
                attr.ShortFormPrefixConvention,
                null);
            return cliNamer.GetCommandName(cmdType.Name);
        }
    }
}
