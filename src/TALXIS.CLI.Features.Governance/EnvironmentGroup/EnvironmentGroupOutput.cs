using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

internal static class EnvironmentGroupOutput
{
#pragma warning disable TXC003
    public static void PrintList(IReadOnlyList<PowerPlatformEnvironmentGroup> groups)
    {
        if (groups.Count == 0)
        {
            OutputWriter.WriteLine("No environment groups found.");
            return;
        }

        int nameWidth = Math.Clamp(groups.Max(g => g.DisplayName.Length), 12, 36);
        string header = $"{"Display Name".PadRight(nameWidth)} | {"Environment Group ID".PadRight(36)} | Environments";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var group in groups)
        {
            OutputWriter.WriteLine(
                $"{Truncate(group.DisplayName, nameWidth).PadRight(nameWidth)} | {group.Id} | {group.EnvironmentIds.Count}");
        }
    }

    public static void PrintDetail(PowerPlatformEnvironmentGroup group)
    {
        OutputWriter.WriteLine($"Display Name:   {group.DisplayName}");
        OutputWriter.WriteLine($"Group ID:       {group.Id}");
        OutputWriter.WriteLine($"Description:    {group.Description ?? "-"}");
        OutputWriter.WriteLine($"Created On:     {(group.CreatedOn is { } c ? c.ToString("u") : "-")}");
        OutputWriter.WriteLine($"Last Modified:  {(group.LastModifiedOn is { } m ? m.ToString("u") : "-")}");
        OutputWriter.WriteLine($"Environments:   {(group.EnvironmentIds.Count == 0 ? "-" : string.Join(", ", group.EnvironmentIds))}");
    }

    private static string Truncate(string value, int maxWidth)
        => value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;
#pragma warning restore TXC003
}
