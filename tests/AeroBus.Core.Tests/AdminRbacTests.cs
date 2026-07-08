using AeroBus.Core.Model.Admin;
using AeroBus.Core.Repositories.Admin;
using Xunit;

namespace AeroBus.Core.Tests;

[Collection("documentforge")]
public class AdminRbacTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task User_role_and_permissions_resolve_through_the_document_model()
    {
        var users = new Users(fx.Store);
        var roles = new Roles(fx.Store);
        var perms = new Permissions(fx.Store);

        var company = DocumentForgeFixture.NewCompany();
        var p1 = new Permission { Id = Guid.NewGuid(), Code = "orders.read", Name = "Read Orders", Status = "Active" };
        var p2 = new Permission { Id = Guid.NewGuid(), Code = "orders.write", Name = "Write Orders", Status = "Active" };
        await perms.SaveAsync(p1);
        await perms.SaveAsync(p2);

        var role = new Role { Id = Guid.NewGuid(), CompanyId = company, Code = "ADMIN", Name = "Administrator", Status = "Active", PermissionIds = new() { p1.Id, p2.Id } };
        await roles.SaveAsync(role);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "rbac@example.com", Name = "RBAC Test", Status = "Active", RoleId = role.Id, CompanyId = company, Password = "x" };
        await users.SaveAsync(user);

        var byEmail = await users.GetByEmailAsync("rbac@example.com", "any");
        Assert.NotNull(byEmail);
        Assert.Equal(userId, byEmail!.Id);

        var resolved = await users.GetPermissionsByUserIdAsync(userId);
        Assert.Equal(2, resolved.Count);
        Assert.Contains(resolved, p => p.Code == "orders.read");

        await users.DeleteAsync(userId);
        await roles.DeleteAsync(role.Id, Guid.Empty);
        await perms.DeleteAsync(p1.Id, Guid.Empty);
        await perms.DeleteAsync(p2.Id, Guid.Empty);
    }
}
