namespace AeroBus.Core.Model.Admin
{
    /// <summary>JWT issuance settings (HS256). For RS/ES, store cert/private key instead.</summary>
    public sealed record Token
    {
        public string Issuer { get; set; } = default!;
        public string Audience { get; set; } = default!;
        public string SigningKey { get; set; } = default!;
        public int AccessTokenMinutes { get; set; } = 15;
        public int RefreshTokenDays { get; set; } = 14;
    }
}
