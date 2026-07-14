using AeroBus.Core.Model.Identity;
using AeroBus.Core.Repositories.Identity;
using Xunit;

namespace AeroBus.Core.Tests;

[Collection("documentforge")]
public class OrgRbacTests(DocumentForgeFixture fx)
{
    [Fact]
    public async Task Custom_org_roles_and_assignments_resolve_through_the_document_model()
    {
        var roles = new OrgRoles(fx.Store);
        var assignments = new OrgRoleAssignments(fx.Store);

        var company = DocumentForgeFixture.NewCompany();
        var role = new OrgRole
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            Name = "Revenue Manager",
            Description = "Offers day-to-day",
            Permissions = ["offers.view", "offers.manage"],
            Created = DateTime.UtcNow,
        };
        await roles.SaveAsync(role);

        var userId = Guid.NewGuid(); // stands in for the Keycloak user id
        await assignments.SaveAsync(new OrgRoleAssignment
        {
            Id = userId,
            CompanyId = company,
            RoleIds = [role.Id],
            Updated = DateTime.UtcNow,
        });

        var byCompany = await roles.GetByCompanyAsync(company);
        Assert.Contains(byCompany, r => r.Id == role.Id && r.Permissions.Contains("offers.manage"));

        var assignment = await assignments.GetByUserAsync(userId);
        Assert.NotNull(assignment);
        Assert.Equal(company, assignment!.CompanyId);
        Assert.Contains(role.Id, assignment.RoleIds);

        await assignments.DeleteAsync(userId);
        await roles.DeleteAsync(role.Id);
    }
}
