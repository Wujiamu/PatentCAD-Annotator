using System;
using System.Collections.Generic;

namespace PatentMarker.IO
{
    /// <summary>
    /// Centralized comparison rules for patent marking numbers.
    /// Numbers are trimmed and compared case-insensitively everywhere.
    /// </summary>
    public static class NumberIdentity
    {
        public static string Normalize(string value)
        {
            return value == null ? "" : value.Trim();
        }

        public static bool AreEqual(string left, string right)
        {
            return string.Equals(
                Normalize(left), Normalize(right),
                StringComparison.OrdinalIgnoreCase);
        }

        public static IEqualityComparer<string> Comparer
        {
            get { return StringComparer.OrdinalIgnoreCase; }
        }
    }
}
