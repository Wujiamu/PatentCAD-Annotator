// AutoCAD 类型存根 — 仅用于测试编译，不实现任何 AutoCAD 运行时功能。
// 这些存根使 DictEntry.cs 中的 ResolveDictPath / ConfigLoader.Load 能通过编译。

namespace Autodesk.AutoCAD.ApplicationServices
{
    public static class Application
    {
        public static DocumentManager DocumentManager { get; } = new DocumentManager();
    }

    public class DocumentManager
    {
        // 测试环境中始终返回 null，模拟无活动文档
        public Document? MdiActiveDocument => null;
    }

    public class Document
    {
        public string Name { get; set; } = "";
        public Editor Editor { get; set; } = new Editor();
    }

    public class Editor
    {
        public void WriteMessage(string message) { }
    }
}
