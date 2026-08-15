using System;
using System.Collections.Generic;

namespace PatentMarker.Diagnostics
{
    /// <summary>
    /// 进程内错误环形缓冲。所有命令通过 RawLog 钩子自动汇入，
    /// PATDOCTOR 命令据此生成现场报告。
    /// | In-process ring buffer of recent errors. Filled automatically via the
    ///   RawLog hook; PATDOCTOR renders it into the doctor report.
    ///
    /// 兼容 .NET 2.0：不使用 LINQ / 字符串插值等高版本特性。
    /// </summary>
    public static class PatDiagnostics
    {
        /// <summary>一条诊断记录 | One diagnostic entry.</summary>
        public sealed class Entry
        {
            private readonly DateTime _time;
            private readonly string _source;
            private readonly string _message;

            public Entry(DateTime time, string source, string message)
            {
                _time = time;
                _source = source ?? "";
                _message = message ?? "";
            }

            public DateTime Time { get { return _time; } }
            public string Source { get { return _source; } }
            public string Message { get { return _message; } }
        }

        private const int MaxEntries = 100;
        private static readonly object _lock = new object();
        private static readonly Queue<Entry> _entries = new Queue<Entry>();

        /// <summary>当前缓冲条数 | Current entry count.</summary>
        public static int Count
        {
            get { lock (_lock) { return _entries.Count; } }
        }

        /// <summary>记录一条错误 | Record one error entry.</summary>
        public static void Record(string source, string message)
        {
            if (message == null || message.Length == 0) return;
            lock (_lock)
            {
                _entries.Enqueue(new Entry(DateTime.Now, source, message));
                while (_entries.Count > MaxEntries)
                    _entries.Dequeue();
            }
        }

        /// <summary>记录异常 | Record an exception with type and stack.</summary>
        public static void RecordException(string source, Exception ex)
        {
            if (ex == null) return;
            string stack = ex.StackTrace ?? "";
            Record(source, ex.GetType().Name + ": " + ex.Message
                + (stack.Length > 0 ? "\n" + stack : ""));
        }

        /// <summary>
        /// RawLog 钩子入口：识别日志行中的错误并记入缓冲。
        /// 专利商标无关的普通信息行会被忽略。
        /// | RawLog hook: classify a log line; only error-like lines are kept.
        /// </summary>
        public static void OnRawLog(string message)
        {
            if (message == null) return;
            // PATDOCTOR 自身的运行/汇总日志不是错误，跳过以避免自引用污染缓冲。
            // | PATDOCTOR's own progress/summary lines are not errors; skip them
            //   so the buffer is not polluted by self-reference.
            if (message.StartsWith("PATDOCTOR", StringComparison.OrdinalIgnoreCase)) return;
            bool isError =
                ContainsWord(message, "error")
                || ContainsWord(message, "failed")
                || ContainsWord(message, "fatal")
                || ContainsWord(message, "exception");
            if (isError)
                Record("log", message);
        }

        /// <summary>取缓冲快照（旧到新） | Snapshot from oldest to newest.</summary>
        public static List<Entry> Snapshot()
        {
            lock (_lock)
            {
                return new List<Entry>(_entries);
            }
        }

        /// <summary>清空缓冲 | Clear the buffer.</summary>
        public static void Reset()
        {
            lock (_lock) { _entries.Clear(); }
        }

        private static bool ContainsWord(string text, string word)
        {
            return text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
