PatentMarker 2025 部署说明
===========================
版本：1.0.3 candidate (2026-09-12)  待发布


目标环境：AutoCAD 2025 / 2026+ (R25.0+)，Windows 10+

部署方式（二选一）：

方式 A：注册表自动加载（推荐）
  1. 将 PatentMarker.dll 放到固定目录
  2. 运行 install-2025.ps1（右键 → 使用 PowerShell 运行）
  3. 重启 AutoCAD

安装脚本说明：
  - 脚本会优先写入当前用户 HKCU 注册表，不需要管理员权限
  - 脚本会合并 HKCU/HKLM，枚举已安装的 R25.0/R25.1/R26.0 版本，并为每个发现的配置写入 HKCU 自动加载项（重复配置去重）
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

Word 端：
  - PatentMarker.dotm 是由根 vba/ 的 8 个组件、9 个物理文件生成的全局模板。
  1. 关闭 Word，双击 install-vba.vbs；脚本会逐字节校验后把 PatentMarker.dotm
     安装到 Word Startup。安装器不打开、保存或替换 Normal.dotm，也不要求 AccessVBOM。
  2. 正常重启 Word。全局模板通过 AutoExec 初始化保存事件；运行宏
     ShowPatentDictPanel 可打开"专利标注字典工具"面板。
  3. 卸载 Word 模板时运行 uninstall-vba.vbs；脚本只处理本产品文件并保留可恢复备份。
  验证边界：本候选仅在 Word 16.0 64 位完成 L4；Word 2010、32 位和不同宏策略仍待目标环境验收。
  失败保护：已有 JSON 被占用或设为只读时，普通保存会取消并保留原内容与文件属性；解除故障后可再次保存。
  Save As 边界：改名/换目录时先更新旧路径；请在新路径再普通保存一次或手动导出。
  导出规则：目录无 DWG 时使用 Word 文件名；有多个 DWG 时点击“手动导出字典”选择目标，
    按所选 DWG 主名生成字典。自动保存只复用当前文档已选目标，未选择时拒绝写入并记录
    autoexport-error.txt。已有路径的普通保存在导出失败时会取消保存，Save As 会先允许建立路径。
  诊断日志：%LOCALAPPDATA%\PatentMarker\Logs\word-vba-YYYYMMDD.tsv

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

诊断（doctor）：
  插件无法加载或 BZD 命令不可用时，无需进入 AutoCAD 即可排查:
      powershell -ExecutionPolicy Bypass -File .\doctor-2025.ps1
  - 离线层：检查 PatentMarker.dll、自动加载注册表及 LOADER 指向、
    .NET 8 运行时、PatentMarker.log 尾部
  - 在线层：自动以批处理模式启动 AutoCAD 2025/2026+，
    NETLOAD 部署 DLL 并执行 PATDOCTOR 生成 CAD 内诊断报告
  - 参数：-OfflineOnly 仅做离线检查；-NoPause 结束时不等待回车
  - 报告输出到本目录 PatentMarker-doctor-offline-report.txt
    （以及 CAD 内诊断报告 PatentMarker-doctor-report.txt）
  - 在线层会核对实际加载 DLL 路径及 PASS/FAIL/SKIP 汇总；CAD 报告含 FAIL 时
    脚本返回非零。若已注册的 demand-load DLL 会抢先加载另一份文件，则 WARN
    并跳过在线层，不把其他目录生成的报告误算为本候选通过
  - 字典文件存在但读取或 JSON 解析失败时，PATDOCTOR 明确报告 FAIL，并在
    Recent errors 中保留具体异常；不会再把损坏字典当成 0 条内容而 SKIP
  - 运行在线层前请先关闭已打开的 AutoCAD

命令：
  BZ   (PATPALETTE)    打开字典面板
  BZM  (PATMARK)       创建引线标注
  BZC  (PATCHECK)      漏标检测：报告"字典有 · 图纸未标注"清单
  BZA  (PATALIGN)      对齐标注文字（先选标注，再选线/框基准）
  BZS  (PATSELECTALL)  全选 PAT 标注
  BZD  (PATDOCTOR)     插件自检并生成诊断报告
  PATMLSET / PATMLVERIFY MLeader scriptable switches and form diagnostic
  DAGUOHAO (PATBRACE)  三点创建独立矢量大括号
  PATBRACEEDIT         通过控制点或输入高度/宽度调整大括号
  第三点决定中部尖点方向和宽度：竖向可向左/向右，横向可向上/向下；
  完整轮廓保持在端点轴线与尖点之间，直干位于所选宽度中线。
