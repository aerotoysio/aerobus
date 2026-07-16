using AeroBus.Core.Data;
using AeroBus.Core.Model.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AeroBus.Core.Repositories.Identity
{
    public interface IOrgRoles
    {
        Task<OrgRole?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<OrgRole>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<OrgRole?> SaveAsync(OrgRole model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }

    public sealed class OrgRoles(
        [FromKeyedServices(ServiceCollectionExtensions.ControlClientKey)] IDocumentStore store)
        : DocumentRepository<OrgRole>(store), IOrgRoles
    {
        protected override string Collection => "orgroles";
    }

    public interface IOrgRoleAssignments
    {
        /// <summary>Assignment doc id = Keycloak user id.</summary>
        Task<OrgRoleAssignment?> GetByUserAsync(Guid userId, CancellationToken ct = default);
        Task<IReadOnlyList<OrgRoleAssignment>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
        Task<OrgRoleAssignment?> SaveAsync(OrgRoleAssignment model, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid userId, CancellationToken ct = default);
    }

    public sealed class OrgRoleAssignments(
        [FromKeyedServices(ServiceCollectionExtensions.ControlClientKey)] IDocumentStore store)
        : DocumentRepository<OrgRoleAssignment>(store), IOrgRoleAssignments
    {
        protected override string Collection => "orgroleassignments";

        public Task<OrgRoleAssignment?> GetByUserAsync(Guid userId, CancellationToken ct = default) =>
            GetByIdAsync(userId, ct);
    }

    public interface IUserProfiles
    {
        /// <summary>Profile doc id = Keycloak user id.</summary>
        Task<UserProfileDoc?> GetByUserAsync(Guid userId, CancellationToken ct = default);
        Task<UserProfileDoc?> SaveAsync(UserProfileDoc model, CancellationToken ct = default);
    }

    public sealed class UserProfiles(
        [FromKeyedServices(ServiceCollectionExtensions.ControlClientKey)] IDocumentStore store)
        : DocumentRepository<UserProfileDoc>(store), IUserProfiles
    {
        protected override string Collection => "userprofiles";

        public Task<UserProfileDoc?> GetByUserAsync(Guid userId, CancellationToken ct = default) =>
            GetByIdAsync(userId, ct);
    }
}
