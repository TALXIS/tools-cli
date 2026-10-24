using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Tests.Governance.PolicyRule;

/// <summary>
/// Shared test host for <c>txc governance policy-rule</c> command tests.
/// Registers a fixed profile context and an in-memory fake
/// <see cref="IPowerPlatformPolicyRuleClient"/> so tests exercise the full
/// CLI command pipeline (argument binding, resolution, output formatting)
/// without any real HTTP calls.
/// </summary>
internal sealed class PolicyRuleCommandTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public PolicyRuleCommandTestHost(FakePolicyRuleClient? client = null)
    {
        Client = client ?? new FakePolicyRuleClient();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfigurationResolver>(new FixedResolver(TestContext()));
        services.AddSingleton<IPowerPlatformPolicyRuleClient>(Client);

        _provider = services.BuildServiceProvider();
        TxcServices.Initialize(_provider);
    }

    public FakePolicyRuleClient Client { get; }

    public void Dispose()
    {
        TxcServices.Reset();
        _provider.Dispose();
    }

    private static ResolvedProfileContext TestContext() => new(
        new Profile { Id = "test", ConnectionRef = "conn", CredentialRef = "cred" },
        new Connection
        {
            Id = "conn",
            Provider = ProviderKind.Dataverse,
            Cloud = CloudInstance.Public,
            TenantId = "tenant-id",
            EnvironmentType = EnvironmentType.Sandbox,
        },
        new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser },
        ResolutionSource.CommandLine);

    private sealed class FixedResolver(ResolvedProfileContext context) : IConfigurationResolver
    {
        public Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct) => Task.FromResult(context);
    }

    /// <summary>
    /// In-memory fake implementing every rule-based-policy operation
    /// against simple dictionaries/lists, so tests can assert on call
    /// arguments and seed pre-existing policies/assignments without any
    /// HTTP mocking.
    /// </summary>
    internal sealed class FakePolicyRuleClient : IPowerPlatformPolicyRuleClient
    {
        private readonly Dictionary<Guid, PowerPlatformPolicy> _policies = new();
        private readonly List<PowerPlatformPolicyAssignment> _assignments = new();

        public List<(Guid PolicyId, string RuleSetId)> RemovedRuleSets { get; } = new();
        public List<(Guid PolicyId, Guid GroupId, IReadOnlyList<PowerPlatformPolicyAssignmentOverride>? Overrides)> GroupAssignments { get; } = new();
        public List<(Guid PolicyId, Guid EnvironmentId, IReadOnlyList<PowerPlatformPolicyAssignmentOverride>? Overrides)> EnvironmentAssignments { get; } = new();

        public PowerPlatformPolicy Add(string name, IReadOnlyList<PowerPlatformPolicyRuleSet>? ruleSets = null)
        {
            var sets = ruleSets ?? Array.Empty<PowerPlatformPolicyRuleSet>();
            var policy = new PowerPlatformPolicy(Guid.NewGuid(), name, "tenant-id", DateTimeOffset.UtcNow, sets.Count, sets);
            _policies[policy.Id] = policy;
            return policy;
        }

        public Task<IReadOnlyList<PowerPlatformPolicy>> ListAsync(Connection connection, Credential credential, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PowerPlatformPolicy>>(_policies.Values.ToList());

        public Task<PowerPlatformPolicy?> GetAsync(Connection connection, Credential credential, Guid policyId, CancellationToken ct)
            => Task.FromResult(_policies.GetValueOrDefault(policyId));

        public Task<PowerPlatformPolicy> CreateAsync(Connection connection, Credential credential, PowerPlatformPolicyCreateOptions options, CancellationToken ct)
        {
            var policy = new PowerPlatformPolicy(Guid.NewGuid(), options.Name, "tenant-id", DateTimeOffset.UtcNow, options.RuleSets.Count, options.RuleSets);
            _policies[policy.Id] = policy;
            return Task.FromResult(policy);
        }

        public Task<PowerPlatformPolicy> UpdateAsync(Connection connection, Credential credential, Guid policyId, PowerPlatformPolicyPatchOptions options, CancellationToken ct)
        {
            var existing = _policies[policyId];
            var ruleSets = existing.RuleSets.ToList();

            if (options.RuleSets is not null)
            {
                foreach (var ruleSet in options.RuleSets)
                {
                    ruleSets.RemoveAll(r => r.Id == ruleSet.Id);
                    ruleSets.Add(ruleSet);
                }
            }

            var updated = existing with
            {
                Name = options.Name ?? existing.Name,
                RuleSets = ruleSets,
                RuleSetCount = ruleSets.Count,
            };
            _policies[policyId] = updated;
            return Task.FromResult(updated);
        }

        public Task<PowerPlatformPolicy> RemoveRuleSetAsync(Connection connection, Credential credential, Guid policyId, string ruleSetId, CancellationToken ct)
        {
            RemovedRuleSets.Add((policyId, ruleSetId));
            var existing = _policies[policyId];
            var ruleSets = existing.RuleSets.Where(r => r.Id != ruleSetId).ToList();
            var updated = existing with { RuleSets = ruleSets, RuleSetCount = ruleSets.Count };
            _policies[policyId] = updated;
            return Task.FromResult(updated);
        }

        public Task<PowerPlatformPolicyAssignment> AssignToEnvironmentGroupAsync(
            Connection connection, Credential credential, Guid policyId, Guid environmentGroupId,
            IReadOnlyList<PowerPlatformPolicyAssignmentOverride>? overrides, CancellationToken ct)
        {
            GroupAssignments.Add((policyId, environmentGroupId, overrides));
            var assignment = new PowerPlatformPolicyAssignment(policyId, environmentGroupId, PowerPlatformPolicyAssignmentResourceType.EnvironmentGroup, _policies[policyId].RuleSetCount, "tenant-id");
            _assignments.Add(assignment);
            return Task.FromResult(assignment);
        }

        public Task<PowerPlatformPolicyAssignment> AssignToEnvironmentAsync(
            Connection connection, Credential credential, Guid policyId, Guid environmentId,
            IReadOnlyList<PowerPlatformPolicyAssignmentOverride>? overrides, CancellationToken ct)
        {
            EnvironmentAssignments.Add((policyId, environmentId, overrides));
            var assignment = new PowerPlatformPolicyAssignment(policyId, environmentId, PowerPlatformPolicyAssignmentResourceType.Environment, _policies[policyId].RuleSetCount, "tenant-id");
            _assignments.Add(assignment);
            return Task.FromResult(assignment);
        }

        public Task<IReadOnlyList<PowerPlatformPolicyAssignment>> ListAssignmentsAsync(
            Connection connection, Credential credential, Guid? policyId, Guid? environmentGroupId, Guid? environmentId, CancellationToken ct)
        {
            if (new[] { policyId, environmentGroupId, environmentId }.Count(f => f is not null) > 1)
                throw new ArgumentException("Specify at most one filter.");

            IEnumerable<PowerPlatformPolicyAssignment> query = _assignments;
            if (policyId is { } p) query = query.Where(a => a.PolicyId == p);
            if (environmentGroupId is { } g) query = query.Where(a => a.ResourceId == g && a.ResourceType == PowerPlatformPolicyAssignmentResourceType.EnvironmentGroup);
            if (environmentId is { } e) query = query.Where(a => a.ResourceId == e && a.ResourceType == PowerPlatformPolicyAssignmentResourceType.Environment);

            return Task.FromResult<IReadOnlyList<PowerPlatformPolicyAssignment>>(query.ToList());
        }
    }
}
