using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;

namespace TALXIS.CLI.Features.Governance.PolicyRule;

internal static class PolicyRuleOutput
{
#pragma warning disable TXC003
    public static void PrintList(IReadOnlyList<PowerPlatformPolicy> policies)
    {
        if (policies.Count == 0)
        {
            OutputWriter.WriteLine("No policies found.");
            return;
        }

        int nameWidth = Math.Clamp(policies.Max(p => p.Name.Length), 8, 36);
        string header = $"{"Name".PadRight(nameWidth)} | {"Policy ID".PadRight(36)} | Rule Sets";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var policy in policies)
        {
            OutputWriter.WriteLine($"{Truncate(policy.Name, nameWidth).PadRight(nameWidth)} | {policy.Id} | {policy.RuleSetCount}");
        }
    }

    public static void PrintDetail(PowerPlatformPolicy policy)
    {
        OutputWriter.WriteLine($"Name:           {policy.Name}");
        OutputWriter.WriteLine($"Policy ID:      {policy.Id}");
        OutputWriter.WriteLine($"Tenant ID:      {policy.TenantId ?? "-"}");
        OutputWriter.WriteLine($"Last Modified:  {(policy.LastModified is { } m ? m.ToString("u") : "-")}");
        OutputWriter.WriteLine($"Rule Sets:      {(policy.RuleSets.Count == 0 ? "-" : string.Join(", ", policy.RuleSets.Select(r => $"{r.Id} (v{r.Version})")))}");
    }

    public static void PrintAssignmentList(IReadOnlyList<PowerPlatformPolicyAssignment> assignments)
    {
        if (assignments.Count == 0)
        {
            OutputWriter.WriteLine("No policy assignments found.");
            return;
        }

        string header = $"{"Policy ID".PadRight(36)} | {"Resource Type".PadRight(16)} | {"Resource ID".PadRight(36)} | Rule Sets";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var assignment in assignments)
        {
            OutputWriter.WriteLine(
                $"{assignment.PolicyId.ToString().PadRight(36)} | " +
                $"{assignment.ResourceType.ToString().PadRight(16)} | " +
                $"{assignment.ResourceId.ToString().PadRight(36)} | " +
                assignment.RuleSetCount);
        }
    }
#pragma warning restore TXC003

    private static string Truncate(string value, int maxWidth)
        => value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;
}
