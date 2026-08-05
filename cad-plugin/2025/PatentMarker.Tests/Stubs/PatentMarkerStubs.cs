// PatentMarkerApp 存根 — 提供 RawLog 方法的无操作实现。
// 测试环境中不需要写日志文件。

using System.Reflection;

namespace PatentMarker
{
    public static class PatentMarkerApp
    {
        internal static void RawLog(string msg)
        {
            // 测试环境：静默忽略日志输出
        }
    }
}

namespace PatentMarker.IO
{
    // ConfigLoader 存根 — DictEntry.cs 中 ResolveDictPath 引用了 ConfigLoader.Current。
    // 测试时始终返回 null，模拟无配置。
    public class PatConfig
    {
        public string DefaultDictPath { get; set; } = "";
    }

    public static class ConfigLoader
    {
        public static PatConfig? Current;
    }
}
