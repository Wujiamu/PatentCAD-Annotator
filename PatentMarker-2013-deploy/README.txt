PatentMarker 2013 部署说明
===========================
版本：1.0.3 candidate (2026-09-12)  待发布


目标环境：AutoCAD 2013 / 2014 (R19.x)，Windows 7+

文件说明：
  PatentMarker.dll     - CAD 插件主文件（单文件部署，已内置 JSON 解析）
  install-2013.vbs     - 安装脚本（写注册表）
  uninstall-2013.vbs   - 卸载脚本（清除注册表）
  doctor-2013.vbs      - 诊断脚本（CAD 外排查，见下方"诊断"）
  PatentMarker.dotm    - Word 全局加载模板（由根 vba/ 唯一真源生成）
  install-vba.vbs      - Word 安装脚本（复制到 Word Startup，不修改 Normal.dotm）
  uninstall-vba.vbs    - Word 可恢复卸载脚本
  vba/                 - Word VBA 源文件（8个组件、9个物理文件）

安装步骤：
  1. 将 PatentMarker.dll 放到固定目录（如 C:\PatentMarker\）
  2. 将 install-2013.vbs 也放到同一目录
  3. 双击 install-2013.vbs
  4. 重启 AutoCAD
  5. 命令行输入 BZ 验证

卸载步骤：
  1. 双击 uninstall-2013.vbs
  2. 重启 AutoCAD

注意事项：
  - 单文件部署：无需 Newtonsoft.Json.dll（已合并进 PatentMarker.dll）
  - 脚本会合并 HKCU/HKLM 并枚举 R19.0 (2013) 和 R19.1 (2014) 下的全部注册表配置，支持并存安装
  - DLL 路径不能含中文（部分环境不兼容）
  - 如果注册表方式不生效，可用 NETLOAD 手动加载

Word 端：
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

诊断（doctor）：
  插件无法加载或 BZD 命令不可用时，无需进入 AutoCAD 即可排查。
  双击 doctor-2013.vbs，或命令行运行:
      cscript doctor-2013.vbs
  - 离线层：检查 PatentMarker.dll、自动加载注册表及 LOADER 指向、
    所需 .NET Framework 4.0、PatentMarker.log 尾部
  - 在线层：自动以批处理模式启动本版本范围内的 AutoCAD，
    NETLOAD 部署 DLL 并执行 PATDOCTOR 生成 CAD 内诊断报告
  - 仅做离线检查: cscript doctor-2013.vbs offline
  - 报告输出到本目录 PatentMarker-doctor-offline-report.txt
    （以及 CAD 内诊断报告 PatentMarker-doctor-report.txt）
  - 运行在线层前请先关闭已打开的 AutoCAD

命令：BZ BZM BZC BZA BZS BZD DAGUOHAO PATBRACE PATBRACEEDIT PATMLSET PATMLVERIFY

BZC 漏标检测（字典有 · 图纸未标注）；BZA 对齐标注文字（先选标注，再选线/框基准）；PATMLSET/PATMLVERIFY 为 MLeader 开关与形态诊断。
`PATBRACE` / `DAGUOHAO` 通过顶部、底部和宽度方向三点创建独立矢量大括号；`PATBRACEEDIT` 支持重新点选控制点或输入高度/宽度调整。
第三点决定中部尖点方向和宽度：竖向可向左/向右，横向可向上/向下；完整轮廓保持在端点轴线与尖点之间，直干位于所选宽度中线。
