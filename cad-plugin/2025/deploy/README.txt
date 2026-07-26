PatentMarker 2025 部署说明
===========================

目标环境：AutoCAD 2025 / 2026+ (R25.0+)，Windows 10+

部署方式（二选一）：

方式 A：注册表自动加载（推荐）
  1. 将 PatentMarker.dll 放到固定目录
  2. 运行 install-2025.ps1（右键 → 使用 PowerShell 运行）
  3. 重启 AutoCAD

方式 B：ApplicationPlugins Bundle
  1. 将 PatentMarker.dll 复制到 PatentMarker.bundle\Contents\ 目录
  2. 将整个 PatentMarker.bundle 文件夹复制到：
     %ProgramData%\Autodesk\ApplicationPlugins\
  3. 重启 AutoCAD

验证：
  命令行输入 BZ，应弹出字典面板。

注意：
  - .NET 8 不支持 Win7，最低要求 Windows 10 1607
  - PatentMarker.dll 是单文件部署，无其他依赖
  - Bundle 方式支持自动更新（替换 DLL 即可）

命令：
  BZ   (PATPALETTE)    打开字典面板
  BZM  (PATMARK)       创建引线标注
  BZC  (PATCHECK)      校验一致性
  BZA  (PATALIGN)      对齐引线
  BZS  (PATSELECTALL)  全选 PAT 标注
