using System.Text.Json.Serialization;

namespace AeroBus.Core.Model.Admin
{
    public sealed record User
    {
        public Guid Id { get; init; }

        /// <summary>
        /// Email of the user for this application.
        /// </summary>
        public string Email { get; init; } = default!;

        /// <summary>
        /// Full name of the user
        /// </summary>
        public string Name { get; init; } = default!;

        /// <summary>
        /// Users are required to be Active to have access to AeroBus
        /// </summary>
        public string? Status { get; init; }
        public DateTime? Created { get; init; }
        public string? Password { get; init; }
        public DateTime? Updated { get; init; }
        public Guid? RoleId { get; init; }
        public Guid? CompanyId { get; init; }

        [JsonIgnore] // never serialize out
        public string PasswordHash { get; init; } = default!;
    }
}
