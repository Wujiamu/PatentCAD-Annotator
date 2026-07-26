AutoCAD 2025/2026+ 托管 DLL
==================================

编译前请将以下文件从 AutoCAD 安装目录复制到本目录：

  - acdbmgd.dll
  - acmgd.dll
  - accoremgd.dll

默认安装路径示例：
  C:\Program Files\Autodesk\AutoCAD 2025\
  C:\Program Files\Autodesk\AutoCAD 2026\

注意：
  - 这些 DLL 不随仓库分发（Autodesk 版权）
  - AutoCAD 2025+ 使用 .NET 8（Core），DLL 与 .NET Framework 版本不兼容
  - 内部版本号为 R25.0+
  - 最低操作系统要求：Windows 10 1607（.NET 8 不支持 Win7）

无 NuGet 依赖：
  - System.Text.Json 内置于 .NET 8，无需额外包
  - 编译输出为单一 PatentMarker.dll（零外部依赖）
