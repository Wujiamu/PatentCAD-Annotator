// ============================================================================
// PATCHECK 结果的跨命令通信（命令 → 面板高亮）。
// 5 版本链接编译；仅静态状态，无 CAD API 依赖（2007 的 .NET 2.0 可编译）。
// ============================================================================
using System;
using System.Collections.Generic;

namespace PatentMarker.Commands
{
    /// <summary>
    /// Holds PATCHECK results so the palette can highlight dictionary entries
    /// that are not yet annotated in the drawing. Results are keyed by the
    /// AutoCAD Document object (passed as object to keep this file free of
    /// AutoCAD references and compilable by the 2007 profile).
    /// </summary>
    public static class PatCheckResult
    {
        private sealed class ResultState
        {
            public List<string> Unmarked = new List<string>();
            public bool HasResult;
            public int Version;
        }

        private sealed class ResultSlot
        {
            public object Key = new object();
            public ResultState State = new ResultState();
        }

        // A dedicated default key preserves the small compatibility API used
        // by tests and older callers that did not have a document context.
        private static readonly object DefaultKey = new object();
        private static readonly object SyncRoot = new object();
        private static readonly List<ResultSlot> Results = new List<ResultSlot>();
        private static readonly ResultState MissingState = new ResultState();
        private static int _version;

        /// <summary>Normalized numbers from the dictionary that were NOT
        /// found in the drawing at the last context-free PATCHECK run.</summary>
        public static List<string> UnmarkedNumbers
        {
            get
            {
                lock (SyncRoot)
                {
                    return GetDefaultState(true).Unmarked;
                }
            }
        }

        /// <summary>True after the first context-free PATCHECK run.</summary>
        public static bool HasResult
        {
            get { return HasResultFor(DefaultKey); }
        }

        /// <summary>Global change counter kept for compatibility with older callers.</summary>
        public static int Version
        {
            get
            {
                lock (SyncRoot) return _version;
            }
        }

        /// <summary>Compatibility key for callers without a document context.</summary>
        public static object DefaultContextKey
        {
            get { return DefaultKey; }
        }

        /// <summary>Replaces the context-free unmarked list.</summary>
        public static void SetUnmarked(List<string> numbers)
        {
            SetUnmarked(DefaultKey, numbers);
        }

        /// <summary>Replaces the unmarked list for one AutoCAD document.</summary>
        public static void SetUnmarked(object documentKey, List<string> numbers)
        {
            lock (SyncRoot)
            {
                ResultState state = GetState(documentKey, true);
                state.Unmarked = numbers ?? new List<string>();
                state.HasResult = true;
                Touch(state);
            }
        }

        /// <summary>Drops the context-free result.</summary>
        public static void Clear()
        {
            Clear(DefaultKey);
        }

        /// <summary>Drops the result for one AutoCAD document.</summary>
        public static void Clear(object documentKey)
        {
            lock (SyncRoot)
            {
                ResultState state = GetState(documentKey, true);
                state.Unmarked = new List<string>();
                state.HasResult = false;
                Touch(state);
            }
        }

        /// <summary>Releases a closed document's state and its object key.</summary>
        public static void Release(object documentKey)
        {
            object key = NormalizeKey(documentKey);
            lock (SyncRoot)
            {
                for (int i = Results.Count - 1; i >= 0; i--)
                {
                    if (object.ReferenceEquals(Results[i].Key, key))
                    {
                        Results.RemoveAt(i);
                        _version++;
                        break;
                    }
                }
            }
        }

        /// <summary>Drops every document result when the palette is disposed.</summary>
        public static void ClearAll()
        {
            lock (SyncRoot)
            {
                Results.Clear();
                _version++;
            }
        }

        /// <summary>True when the normalized number is in the context-free list.</summary>
        public static bool IsUnmarked(string normalizedNumber)
        {
            return IsUnmarked(DefaultKey, normalizedNumber);
        }

        /// <summary>True when the normalized number is unmarked in one document.</summary>
        public static bool IsUnmarked(object documentKey, string normalizedNumber)
        {
            if (normalizedNumber == null) return false;
            lock (SyncRoot)
            {
                ResultState state = GetState(documentKey, false);
                if (!state.HasResult || normalizedNumber == null) return false;
                for (int i = 0; i < state.Unmarked.Count; i++)
                {
                    if (string.Equals(state.Unmarked[i], normalizedNumber,
                        StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
        }

        /// <summary>Returns whether a document has a completed PATCHECK result.</summary>
        public static bool HasResultFor(object documentKey)
        {
            lock (SyncRoot)
            {
                ResultState state = GetState(documentKey, false);
                return state.HasResult;
            }
        }

        /// <summary>Returns the change counter for one document.</summary>
        public static int GetVersion(object documentKey)
        {
            lock (SyncRoot)
            {
                ResultState state = GetState(documentKey, false);
                return state.Version;
            }
        }

        private static object NormalizeKey(object documentKey)
        {
            return documentKey ?? DefaultKey;
        }

        private static ResultState GetDefaultState(bool create)
        {
            return GetState(DefaultKey, create);
        }

        private static ResultState GetState(object documentKey, bool create)
        {
            object key = NormalizeKey(documentKey);
            for (int i = 0; i < Results.Count; i++)
            {
                if (object.ReferenceEquals(Results[i].Key, key))
                    return Results[i].State;
            }
            if (!create) return MissingState;
            ResultState state = new ResultState();
            ResultSlot slot = new ResultSlot();
            slot.Key = key;
            slot.State = state;
            Results.Add(slot);
            return state;
        }

        private static void Touch(ResultState state)
        {
            state.Version++;
            _version++;
        }
    }
}
