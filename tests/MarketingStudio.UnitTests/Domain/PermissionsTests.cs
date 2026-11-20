using FluentAssertions;
using MarketingStudio.Domain.Users;

namespace MarketingStudio.UnitTests.Domain;

/// <summary>Spot checks that the code matches RBAC Matrix.md.</summary>
public class PermissionsTests
{
    [Theory]
    [Trait("Story", "AUTH-6")]
    [InlineData(Roles.TenantAdmin, Permissions.UsersManage, true)]
    [InlineData(Roles.TenantAdmin, Permissions.BillingManage, true)]
    [InlineData(Roles.TenantAdmin, Permissions.PlatformAdmin, false)]
    [InlineData(Roles.MarketingManager, Permissions.ContentApprove, true)]
    [InlineData(Roles.MarketingManager, Permissions.UsersManage, false)]
    [InlineData(Roles.MarketingManager, Permissions.ChannelsManage, false)]
    [InlineData(Roles.ContentManager, Permissions.ContentSubmit, true)]
    [InlineData(Roles.ContentManager, Permissions.ContentApprove, false)]
    [InlineData(Roles.ContentManager, Permissions.BrandsWrite, false)]
    [InlineData(Roles.ContentManager, Permissions.BrandContentWrite, true)]
    [InlineData(Roles.Viewer, Permissions.BrandsRead, true)]
    [InlineData(Roles.Viewer, Permissions.AgentsExecute, false)]
    [InlineData(Roles.SuperAdmin, Permissions.PlatformAdmin, true)]
    public void Role_has_the_permissions_of_the_rbac_matrix(string role, string permission, bool expected) =>
        Permissions.RolesHave([role], permission).Should().Be(expected);

    [Fact]
    public void Unknown_roles_have_no_permissions() =>
        Permissions.ForRoles(["NotARole"]).Should().BeEmpty();

    [Fact]
    public void Every_tenant_role_has_at_least_read_access_to_brands() =>
        Roles.TenantRoles.Where(r => !Permissions.RolesHave(new[] { r }, Permissions.BrandsRead)).Should().BeEmpty();
}
