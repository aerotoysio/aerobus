namespace AeroBus.Core.Model.Admin
{
    public sealed class RolePermission
    {
        public Guid? RoleId { get; init; }
        public Guid? PermissionId { get; init; }
    }
}
