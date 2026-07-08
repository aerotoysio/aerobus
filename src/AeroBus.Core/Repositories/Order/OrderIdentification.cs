using System.Text;

namespace AeroBus.Core.Repositories.Order
{
    /// <summary>
    /// Generates customer-facing order ids. Ported from ooms
    /// Business.Order.OrderIdentification: an airline designator / accounting-code
    /// prefix followed by a deterministic-but-scrambled Base36 code derived from
    /// the order sequence (Knuth multiplicative hash + secret salt). Unused ooms
    /// imports (System.Data, Business.Admin) and the dead <c>randomLength</c> local
    /// are dropped; the id scheme itself is preserved exactly.
    /// </summary>
    public static class OrderIdentification
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        /// <summary>
        /// Generates an order id from the airline designator/accounting code plus a
        /// scrambled suffix derived from <paramref name="orderSequence"/>.
        /// </summary>
        public static string Generate(string? designator, string? accountingCode, int orderSequence)
        {
            // Prefix per R777 — airline designator if available, else accounting code.
            var prefix = ChoosePrefix(designator, accountingCode);
            return prefix + CreateCodeFromSequence(orderSequence, 747);
        }

        private static string ChoosePrefix(string? designator, string? accounting)
        {
            if (string.IsNullOrWhiteSpace(accounting))
                accounting = "XXX";

            if (!string.IsNullOrWhiteSpace(designator))
                return designator.Trim().ToUpperInvariant().Substring(0, Math.Min(3, designator.Trim().Length));

            if (!string.IsNullOrWhiteSpace(accounting))
                return accounting.Trim().ToUpperInvariant().Substring(0, Math.Min(3, accounting.Trim().Length));

            // fallback: no prefix
            return string.Empty;
        }

        /// <summary>
        /// Creates an 8-character obfuscated order code from a sequential integer.
        /// The mapping is a bijection over 32-bit space (deterministic and unique
        /// per sequence value) but looks random / non-sequential to customers.
        /// </summary>
        public static string CreateCodeFromSequence(long sequenceValue, uint secretSalt)
        {
            if (sequenceValue < 0)
                throw new ArgumentOutOfRangeException(nameof(sequenceValue), "Must be non-negative.");

            // Assumes sequenceValue fits in 32 bits.
            var v = (uint)sequenceValue;

            // Obfuscation: Knuth multiplicative hash then XOR a secret salt.
            // Bijective over uint, so still unique but scrambled.
            v = unchecked(v * 2654435761u); // Knuth's constant
            v ^= secretSalt;

            return ToBase36(v, 8);
        }

        private static string ToBase36(ulong value, int width)
        {
            Span<char> buffer = stackalloc char[32];
            int pos = buffer.Length;

            do
            {
                var idx = (int)(value % 36);
                buffer[--pos] = Alphabet[idx];
                value /= 36;
            } while (value > 0);

            int len = buffer.Length - pos;

            if (len > width)
            {
                pos = buffer.Length - width;
                len = width;
            }

            var sb = new StringBuilder(width);
            for (int i = 0; i < width - len; i++)
                sb.Append('0');
            sb.Append(buffer.Slice(pos, len));
            return sb.ToString();
        }
    }
}
