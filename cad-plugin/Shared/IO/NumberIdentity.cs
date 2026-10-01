using System;
using System.Collections.Generic;

namespace PatentMarker.IO
{
    /// <summary>
    /// Centralized comparison rules for patent marking numbers.
    /// Numbers are trimmed and compared case-insensitively everywhere. Comparer
    /// applies the same normalization as AreEqual, including when used by a set
    /// or dictionary without pre-normalizing its keys.
    /// </summary>
    public static class NumberIdentity
    {
        private static readonly IEqualityComparer<string> NumberComparer = new NormalizedComparer();

#if NET8_0_OR_GREATER
        public static string Normalize(string? value)
#else
        public static string Normalize(string value)
#endif
        {
            return value == null ? "" : value.Trim();
        }

#if NET8_0_OR_GREATER
        public static bool AreEqual(string? left, string? right)
#else
        public static bool AreEqual(string left, string right)
#endif
        {
            return string.Equals(
                Normalize(left), Normalize(right),
                StringComparison.OrdinalIgnoreCase);
        }

        public static IEqualityComparer<string> Comparer
        {
            get { return NumberComparer; }
        }

        private sealed class NormalizedComparer : IEqualityComparer<string>
        {
#if NET8_0_OR_GREATER
            public bool Equals(string? left, string? right)
#else
            public bool Equals(string left, string right)
#endif
            {
                return AreEqual(left, right);
            }

            public int GetHashCode(string value)
            {
                return StringComparer.OrdinalIgnoreCase.GetHashCode(Normalize(value));
            }
        }
    }
}
