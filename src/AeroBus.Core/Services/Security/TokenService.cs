using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace AeroBus.Core.Services.Security
{
    public sealed class TokenService
    {
        private readonly string _issuer;
        private readonly string _audience;
        private readonly SymmetricSecurityKey _key;

        public TokenService(string issuer, string audience, SymmetricSecurityKey key)
        {
            _issuer = issuer;
            _audience = audience;
            _key = key;
        }

        public string CreateAccessToken(IEnumerable<Claim> claims, TimeSpan lifetime)
        {
            var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
            var now = DateTime.UtcNow;

            var jwt = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: now,
                expires: now.Add(lifetime),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }
    }
}
