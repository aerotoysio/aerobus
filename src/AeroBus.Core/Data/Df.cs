using System.Text.Json;

namespace AeroBus.Core.Data
{
    /// <summary>
    /// Field-name bridge between the PascalCase .NET models and the camelCase
    /// storage convention (see <see cref="DocumentStore"/>). Stored field names
    /// must always be derived from the model — <c>Df.Field(nameof(Flight.CompanyId))</c>
    /// — never hand-written, so a model rename breaks the build instead of a
    /// query silently matching nothing (DocumentForge field matching is
    /// case-sensitive). The constants cover the store-level fields that every
    /// document carries regardless of model type.
    /// </summary>
    public static class Df
    {
        /// <summary>The single business identifier every stored document carries.</summary>
        public const string Id = "id";

        /// <summary>Audit stamp set on first write (preserved on later writes).</summary>
        public const string Created = "created";

        /// <summary>Audit stamp refreshed on every write.</summary>
        public const string Updated = "updated";

        /// <summary>
        /// Tenant scoping field. A cross-model convention rather than a
        /// per-model nameof: IDocument deliberately does not declare CompanyId
        /// (global reference data has none), but every tenant-scoped model
        /// stores it under this name.
        /// </summary>
        public const string CompanyId = "companyId";

        /// <summary>Stored (camelCase) name of a model property: pass <c>nameof(Model.Property)</c>.</summary>
        public static string Field(string propertyName) =>
            JsonNamingPolicy.CamelCase.ConvertName(propertyName);

        /// <summary>
        /// Contains-style LIKE pattern for a user-supplied search term, with the
        /// string literal escaped for interpolation into a DocumentForge WHERE
        /// clause (DF's LIKE is case-insensitive; % = any, _ = one char).
        /// </summary>
        public static string Contains(string term) =>
            "%" + term.Trim().Replace("'", "''") + "%";

        /// <summary>
        /// OR-of-LIKEs search predicate over the given stored field names,
        /// parenthesised for direct use in a WHERE clause.
        /// </summary>
        public static string Match(string term, params string[] fields)
        {
            var pattern = Contains(term);
            return "(" + string.Join(" OR ", fields.Select(f => $"{f} LIKE '{pattern}'")) + ")";
        }
    }
}
