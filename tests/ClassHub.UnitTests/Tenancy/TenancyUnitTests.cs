using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.SharedKernel.Security;
using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.UnitTests.Tenancy;

public sealed class TenancyUnitTests
{
    [Fact]
    public void Owner_has_every_known_workspace_permission_and_nothing_unknown()
    {
        foreach (var permission in WorkspacePermissions.All)
        {
            WorkspaceRolePermissions.Has(WorkspaceRole.Owner, permission).ShouldBeTrue();
        }

        WorkspaceRolePermissions.Has(WorkspaceRole.Owner, "admin.users.suspend").ShouldBeFalse();
    }

    [Fact]
    public void Workspace_policy_names_are_prefixed_permissions()
    {
        WorkspacePolicies.Read.ShouldBe("ws:workspace.read");
        WorkspacePolicies.For(WorkspacePermissions.WorkspaceManage).ShouldBe(WorkspacePolicies.Manage);
    }

    [Fact]
    public void A_request_can_never_switch_to_another_workspace()
    {
        var tenant = new TenantContext();
        var a = Guid.NewGuid();
        tenant.Set(a, Guid.NewGuid(), WorkspaceRole.Owner);
        tenant.Set(a, Guid.NewGuid(), WorkspaceRole.Owner); // same workspace: fine

        Should.Throw<InvalidOperationException>(() => tenant.Set(Guid.NewGuid(), Guid.NewGuid(), WorkspaceRole.Owner));
        ((ITenantContext)tenant).RequireWorkspaceId().ShouldBe(a);
    }

    [Fact]
    public void Without_a_workspace_the_tenant_context_fails_closed()
    {
        ITenantContext tenant = new TenantContext();

        tenant.HasWorkspace.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => tenant.RequireWorkspaceId());
    }

    [Fact]
    public async Task Unknown_workspace_permissions_are_rejected()
    {
        var provider = new WorkspacePolicyProvider(Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Authorization.AuthorizationOptions()));

        await Should.ThrowAsync<InvalidOperationException>(() => provider.GetPolicyAsync("ws:does.not.exist"));
        (await provider.GetPolicyAsync(WorkspacePolicies.Read)).ShouldNotBeNull();
    }
}
