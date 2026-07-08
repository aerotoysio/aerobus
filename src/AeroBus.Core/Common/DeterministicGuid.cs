using System.Security.Cryptography;
using System.Text;

namespace AeroBus.Core.Common
{
    public static class DeterministicGuid
    {
        /// <summary>
        /// Creates a deterministic GUID from a string.
        /// Same input string -> same GUID.
        /// Different strings -> different GUIDs with extremely low collision risk.
        /// </summary>
        public static Guid FromString(string input)
        {
            ArgumentNullException.ThrowIfNull(input);

            // Hash the string to 16 bytes (SHA-256, first 16 bytes).
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

            Span<byte> guidBytes = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(guidBytes);

            // Set version and variant bits so it looks like a proper RFC 4122 GUID.
            guidBytes[6] = (byte)(guidBytes[6] & 0x0F | 5 << 4); // version 5 style
            guidBytes[8] = (byte)(guidBytes[8] & 0x3F | 0x80);   // variant 10xx xxxx

            return new Guid(guidBytes);
        }
    }
}
