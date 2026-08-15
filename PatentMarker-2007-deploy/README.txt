============================================
PatentMarker 2007 安装说明 (v2.12)
============================================

【系统要求】
  - Windows 7 SP1 (64位)
  - AutoCAD 2007 (32位)
  - Word 2010
  - .NET Framework 2.0/3.5 (Win7 自带)

【文件内容】

  PatentMarker.dll            CAD 插件主程序
  install-2007.vbs            步骤1: 安装 CAD 插件 (三层自动加载)
  install-2007.bat            步骤1: BAT 快捷方式
  uninstall-2007.vbs          卸载 CAD 插件
  doctor-2007.vbs            诊断脚本 (CAD 外排查, 见下方【诊断】)
  install-vba.vbs             步骤2: 安装 VBA 到 Normal 模板
  load-patent-marker.lsp      CAD 加载脚本 (安装时自动部署)
  vba\
    Patterns.bas              VBA 模块
    DictModel.bas             VBA 模块
    JsonWriter.bas            VBA 模块
    PatentExtractor.bas       VBA 模块
    AutoExport.bas            VBA 模块
    clsSaveHook.cls           VBA 类模块

【安装步骤】

  步骤1: 安装 CAD 插件 (DLL)
  ---------------------------
  1. 将整个文件夹复制到任意 C 盘位置 (如 D:\PatentMarker\)
  2. 双击 install-2007.vbs
  3. 脚本将自动执行三层加载策略:
     - 第1层: 写 HKCU 注册表 (LOADCTRLS=14, 启动时加载)
       有管理员权限时同时写 HKLM
     - 第2层: 部署 acad.lsp 到 ACAD 支持路径
       扫描注册表支持路径/AcadLocation\Support/%APPDATA%
       在首个可写目录创建 acad.lsp 实现自动加载
     - 第3层: 部署 load-patent-marker.lsp 手动加载兜底
  4. 启动 AutoCAD 2007
  5. 若插件未加载 (ACAD 2007 可能不读 HKCU):
     方案A: 用 APPLOAD (推荐)
       - 打开 APPLOAD 对话框
       - 在"启动组"下点"内容"
       - 添加 load-patent-marker.lsp
       - 重启 AutoCAD
     方案B: 用 NETLOAD
       - 输入 NETLOAD 命令
       - 选择 PatentMarker.dll

  步骤2: 安装 VBA 模块 (Word Normal 模板)
  ----------------------------------------
  1. 打开 Word 并启用 VBA 宏运行权限:
     - 文件 > 选项 > 信任中心
     - 信任中心设置 > 宏设置
     - 勾选: 禁用 VBA 工程对象模型的访问
     - 宏安全设置: 禁用所有宏并发出通知
     - 确定
  2. 关闭所有 Word 窗口 (避免 Normal.dotm 被占用)
  3. 双击 install-vba.vbs
  4. 脚本自动:
     - 获取 Normal.dotm 路径
     - 打开 Normal.dotm
     - 导入 6 个 VBA 模块
     - 保存 Normal.dotm
  5. 打开 Word 文档即可使用宏

  步骤3: 验证
  -----------
  1. Word 端: 运行 EnableAutoExport (或 ExtractDict)
     -> 生成 <dwg名>.dict.json
  2. AutoCAD 端: 输入 BZ (或 PATPALETTE)
     -> 打开标注显示面板
  3. 双击面板项目, 输入 BZM 标注

【附图标记说明 支持格式】

  VBA 宏从 Word 文档中自动识别"附图标记说明"段落,
  提取零件编号与名称的对应关系, 输出为 JSON 文件。

  一、章节标题识别 (以下格式均可自动定位):
    - 附图标记说明如下：
    - 附图标记说明：
    - 附图说明如下：
    - 附图说明：
    - 附图：
    - 标记说明如下：
    - 标记说明：
    - 标记：
    - 图面说明如下：
    - 图面说明：
    - 零部件说明：
    - 参考标记说明：

  二、编号格式:
    - 纯数字: 100, 1100, 2442
    - 数字+字母后缀(A-F): 1342A, 1342B, 1102C
    - 多编号共享名称: 1102A、1102B连接件

  三、分隔符 (编号与名称之间):
    - 冒号: 100：板式换热器
    - 中文冒号: 100:板式换热器
    - 顿号: 100、板式换热器
    - 句号: 100．板式换热器
    - 英文句点: 100.板式换热器
    - 分号: 100；板式换热器
    - 英文分号: 100;板式换热器
    - 逗号: 100，板式换热器
    - 破折号: 100—板式换热器
    - 短横线: 100-板式换热器
    - 斜杠: 100/板式换热器
    - 空格: 100 板式换热器
    - 无分隔: 100板式换热器

  四、条目间分隔:
    - 分号: 100板式换热器；110板片；
    - 逗号: 100板式换热器，110板片
    - 顿号: 100板式换热器、110板片
    - 换行: 每行一个条目
    - 混合格式均可识别

  五、中文数字枚举格式:
    - 一、弹簧
    - 二、连接件
    - 三：壳体
    - 支持: 一~二十

  六、输出示例 (JSON):
    {
      "100": "板式换热器",
      "110": "板片",
      "1342A": "过渡连接板部",
      "1342B": "凹陷板部"
    }

【命令一览】

  CAD (AutoCAD):
    BZ  /  PATPALETTE    打开面板
    BZM /  PATMARK       标注选中对象
    BZC /  PATCHECK      一致性检查
    BZA /  PATALIGN      对齐标注
    BZS /  PATSELECTALL  全选标注实体

  Word (VBA, 在 Normal 模板):
    ExtractDict          手动导出 dict.json
    EnableAutoExport     保存时自动导出
    DisableAutoExport    关闭自动导出


【诊断】

  插件无法加载或 BZD 命令不可用时，无需进入 AutoCAD 即可排查。
  双击 doctor-2007.vbs，或命令行运行:
      cscript doctor-2007.vbs
  - 离线层: 检查 PatentMarker.dll、自动加载注册表及 LOADER 指向、
    所需 .NET Framework 2.0、PatentMarker.log 尾部
  - 在线层: 自动以批处理模式启动 AutoCAD 2007~2009，
    NETLOAD 部署 DLL 并执行 PATDOCTOR 生成 CAD 内诊断报告
  - 仅做离线检查: cscript doctor-2007.vbs offline
  - 报告输出到本目录 PatentMarker-doctor-offline-report.txt
    （以及 CAD 内诊断报告 PatentMarker-doctor-report.txt）
  - 运行在线层前请先关闭已打开的 AutoCAD

【卸载】

  1. 运行 uninstall-2007.vbs (删除 CAD 注册表)
  2. Word 中: Alt+F11 > Normal > 删除 6 个模块
  3. 删除部署文件夹

【常见问题】

  问: CAD 插件未加载
  答: 安装脚本已部署三层自动加载 (注册表+acad.lsp+手动LSP),
      正常启动 AutoCAD 应自动加载。若仍未加载:
      用 APPLOAD 添加 load-patent-marker.lsp,
      或 NETLOAD 加载 PatentMarker.dll。

  问: VBA 安装提示 Normal 模板只读
  答: 关闭所有 Word 窗口后重试。
      Normal.dotm 被 Word 占用时会只读。

  问: VBA 安装提示无法访问 VBA 工程
  答: 在 Word 信任中心勾选"禁用 VBA 工程对象模型的访问"。

  问: CAD 找不到 dict.json
  答: dict.json 必须与 .dwg 文件在同一文件夹,
      且文件名相同 (如 drawing.dwg 对应 drawing.dict.json)。

  问: 附图标记说明识别不到
  答: 确保文档中包含上述"章节标题识别"中列出的标题格式,
      且标题后紧跟编号-名称内容。