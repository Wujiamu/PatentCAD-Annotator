PatentMarker 2025 部署说明
===========================

目标环境：AutoCAD 2025 / 2026+ (R25.0+)，Windows 10+

部署方式（二选一）：

方式 A：注册表自动加载（推荐）
  1. 将 PatentMarker.dll 放到固定目录
  2. 运行 install-2025.ps1（右键 → 使用 PowerShell 运行）
  3. 重启 AutoCAD

安装脚本说明：
  - 脚本会优先写入当前用户 HKCU 注册表，不需要管理员权限
  - 脚本会在部署目录生成 load-patent-marker.lsp 兜底文件
  - 如果窗口闪退，请从 PowerShell 运行：
      powershell.exe -ExecutionPolicy Bypass -File .\install-2025.ps1
    脚本会停在最后显示错误；日志保存在 install-2025.log
  - 也可以使用 -NoPause 供批处理或自动化调用

方式 B：ApplicationPlugins Bundle
  1. 将 PatentMarker.dll 复制到 PatentMarker.bundle\Contents\ 目录
  2. 将整个 PatentMarker.bundle 文件夹复制到：
     %ProgramData%\Autodesk\ApplicationPlugins\
  3. 重启 AutoCAD

验证：
  命令行输入 BZ，应弹出字典面板。

卸载：
  运行 uninstall-2025.ps1（PowerShell），清理注册表自动加载条目和生成的
  LSP 兜底文件（部署目录与 %LOCALAPPDATA%\PatentMarker）。
  - 如需保留 LSP 兜底文件，使用参数：-KeepLsp
  - 部署目录本身不会被删除，如需彻底移除请手动删除整个文件夹
  - 日志保存在 uninstall-2025.log
  - 方式 B（Bundle）安装的用户：删除 %ProgramData%\Autodesk\ApplicationPlugins\
    下的 PatentMarker.bundle 文件夹即可

注意：
  - .NET 8 不支持 Win7，最低要求 Windows 10 1607
  - PatentMarker.dll 是单文件部署，无其他依赖
  - Bundle 方式支持自动更新（替换 DLL 即可）
  - 注册表自动加载未生效时，在 AutoCAD 中运行 APPLOAD，选择 load-patent-marker.lsp；
    或直接运行 NETLOAD，选择 PatentMarker.dll

命令：
  BZ   (PATPALETTE)    打开字典面板
  BZM  (PATMARK)       创建引线标注
  BZC  (PATCHECK)      校验一致性
  BZA  (PATALIGN)      对齐引线
  BZS  (PATSELECTALL)  全选 PAT 标注
  DAGUOHAO (PATBRACE)  三点创建独立矢量大括号
  PATBRACEEDIT         通过控制点或输入高度/宽度调整大括号
  第三点决定中部尖点方向：竖向左/右、横向上/下；外侧肩部位于相反侧
