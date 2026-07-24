============================================
PatentMarker 2007 部署说明
============================================

【系统要求】
  - Windows 7 SP1 (64位)
  - AutoCAD 2007 (32位)
  - Word 2010
  - .NET Framework 2.0/3.5 (Win7 自带)

【包内容】

  PatentMarker.dll            CAD 插件主程序
  install-2007.vbs            第1步: 安装 CAD 插件 (DLL)
  install-2007.bat            第1步: BAT 备用方案
  uninstall-2007.vbs          卸载 CAD 插件
  install-vba.vbs             第2步: 安装 VBA 到 Normal 模板
  load-patent-marker.lsp      CAD 辅助加载 (安装时自动生成)
  vba\
    Patterns.bas              VBA 模块
    DictModel.bas             VBA 模块
    JsonWriter.bas            VBA 模块
    PatentExtractor.bas       VBA 模块
    AutoExport.bas            VBA 模块
    clsSaveHook.cls           VBA 类模块

【部署步骤】

  第1步: 安装 CAD 插件 (DLL)
  ---------------------------
  1. 把整个文件夹复制到非 C 盘位置 (如 D:\PatentMarker\)
  2. 双击 install-2007.vbs
  3. 脚本会:
     - 写入 HKCU 注册表
     - 尝试写 HKLM (可能需要管理员权限)
     - 生成 load-patent-marker.lsp 辅助文件
  4. 重启 AutoCAD 2007
  5. 若命令不可用 (ACAD 2007 可能不读 HKCU):
     方案A: 用 APPLOAD (推荐)
       - 输入 APPLOAD 命令
       - 点击"启动套件"下的"内容"
       - 添加 load-patent-marker.lsp
       - 重启 AutoCAD
     方案B: 用 NETLOAD
       - 输入 NETLOAD 命令
       - 选择 PatentMarker.dll

  第2步: 安装 VBA 模块 (Word Normal 模板)
  ----------------------------------------
  1. 先在 Word 中启用 VBA 工程信任:
     - 文件 > 选项 > 信任中心
     - 信任中心设置 > 宏设置
     - 勾选: 信任对 VBA 工程对象模型的访问
     - 宏安全级别: 禁用所有宏并发出通知
     - 确定
  2. 关闭所有 Word 窗口 (避免 Normal.dotm 被占用)
  3. 双击 install-vba.vbs
  4. 脚本自动:
     - 获取 Normal.dotm 路径
     - 打开 Normal.dotm
     - 导入 6 个 VBA 模块
     - 保存 Normal.dotm
  5. 所有 Word 文档都能使用这些宏

  第3步: 验证
  -----------
  1. Word 中: 运行 EnableAutoExport (或 ExtractDict)
     -> 生成 <dwg名>.dict.json
  2. AutoCAD 中: 运行 BZ (或 PATPALETTE)
     -> 面板显示字典内容
  3. 双击字典条目, 运行 BZM 标注

【可用命令】

  CAD (AutoCAD):
    BZ  /  PATPALETTE    打开字典面板
    BZM /  PATMARK       创建引线标注
    BZC /  PATCHECK      检查一致性
    BZA /  PATALIGN      对齐引线
    BZS /  PATSELECTALL  全选标注实体

  Word (VBA, 在 Normal 工程):
    ExtractDict          手动导出 dict.json
    EnableAutoExport     保存时自动导出
    DisableAutoExport    关闭自动导出

【卸载】

  1. 运行 uninstall-2007.vbs (删除 CAD 注册表)
  2. Word 中: Alt+F11 > Normal > 删除 6 个模块
  3. 删除整个文件夹

【常见问题】

  问: CAD 命令不可用
  答: ACAD 2007 可能不读 HKCU 注册表。
      用 APPLOAD 加载 load-patent-marker.lsp,
      或用 NETLOAD 加载 PatentMarker.dll。

  问: VBA 安装提示 Normal 模板只读
  答: 关闭所有 Word 窗口后重试。
      Normal.dotm 被 Word 占用时会只读。

  问: VBA 安装提示无法访问 VBA 工程
  答: 在 Word 信任中心启用"信任对 VBA 工程对象模型的访问"。

  问: CAD 中找不到 dict.json
  答: dict.json 必须和 .dwg 文件在同一文件夹,
      且文件名相同 (如 drawing.dwg 对应 drawing.dict.json)。
