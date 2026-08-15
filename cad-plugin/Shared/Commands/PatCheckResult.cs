// ============================================================================
// PATCHECK 结果的跨命令通信（命令 → 面板高亮）。
// 5 版本链接编译；仅静态状态，无 CAD API 依赖（2007 的 .NET 2.0 可编译）。
// ============================================================================
using System.Collections.Generic;

namespace PatentMarker.Commands
{
    /// <summary>
    /// Holds the latest PATCHECK result so the palette can highlight
    /// dictionary entries that are not yet annotated in the drawing.
    /// Read by the palette render layer, written by PATCHECK.
    /// </summary>
    public static class PatCheckResult
    {
        /// <summary>Normalized numbers from the dictionary that were NOT
        /// found in the drawing at the last PATCHECK run.</summary>
        public static List<string> UnmarkedNumbers
        {
            get { return _unmarked; }
        }
        private static List<string> _unmarked = new List<string>();

        /// <summary>True after the first PATCHECK run in this session.</summary>
        public static bool HasResult;

        /// <summary>Increments on every SetUnmarked/Clear; the palette timer
        /// compares it to detect finished checks without polling lists.</summary>
        public static int Version;

        /// <summary>Replaces the unmarked list (atomic reference swap so the
        /// palette never enumerates a list being modified).</summary>
        public static void SetUnmarked(List<string> numbers)
        {
            _unmarked = numbers ?? new List<string>();
            HasResult = true;
            Version++;
        }

        /// <summary>Drops the result (e.g. when the dictionary file changes).</summary>
        public static void Clear()
        {
            _unmarked = new List<string>();
            HasResult = false;
            Version++;
        }

        /// <summary>True when the normalized number is in the current
        /// unmarked list (and a result exists).</summary>
        public static bool IsUnmarked(string normalizedNumber)
        {
            if (!HasResult || normalizedNumber == null) return false;
            return _unmarked.Contains(normalizedNumber);
        }
    }
}
