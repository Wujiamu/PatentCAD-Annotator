# 开发日志与经验教训

> 本文档记录 PatentCAD-Annotator 项目的所有改动、遇到的问题及解决方案。
> 目的是让后续接手的人或 AI 模型能快速了解项目历史，避免重复踩坑。

---

## 2007/2010 版实测修复（2026-07-26）

### 13. Word 2010 无法正确导入 clsSaveHook.cls

**问题现象**：在 Word 2010（Win7）上通过 VBA 编辑器“导入文件”安装 `clsSaveHook.cls` 后，`VERSION 1.0 CLASS` 和 `Attribute VB_Name` 等元数据行会显示在代码窗口中，编译时报错。

**根本原因**：Word 2010 的类模块文件导入机制存在兼容性问题，无法识别 .cls 文件头部的 VERSION/Attribute 元数据，导致其被当作普通代码处理。

**解决方案**：
- 修改 `install-vba.vbs`，不再导入 `clsSaveHook.cls` 文件。
- 通过 `VBComponents.Add(2)` 创建空白类模块，再用 `CodeModule.AddFromString()` 将代码注入。
- 已同步更新所有部署包中的 `install-vba.vbs`。

### 14. Leader 箭头大小修改后不能立即生效

**问题现象**：在面板中修改箭头大小后，新建的引线仍使用修改前的大小，只有切换一次“显示/隐藏箭头”后才会更新。

**根本原因**：创建 `Leader` 时从标注样式（DimStyle）继承了旧的 `Dimasz` 值；虽然代码会同步修改 `DimStyleTableRecord.Dimasz`，但已创建或新创建的 Leader 实例不会自动感知样式变化。

**解决方案**：在 `CreateLeaderWithText` 中为新 Leader 直接设置实例属性：

```csharp
leader.Dimasz = Palette.PatPaletteCommand.ArrowSize;
```

**影响版本**：2007、2010（均使用 `Leader` + `MText` 方案）。

**测试结果**：
- VBA 端：Word 2010 保存文档可正常生成 `.dict.json`
- CAD 端：AutoCAD 2007 标注、面板、字典刷新均正常

---

## 多版本适配（2010/2013/2015/2025）

### 改动概述

从已完成的 2007 版派生 4 个适配版本，覆盖 AutoCAD 2007—2026+ 全部版本：

| 版本 | 标注 API | .NET | JSON | 编译验证 |
|------|----------|------|------|----------|
| 2010 | Leader + MText | 3.5 | SimpleJson | ✅ |
| 2013 | MLeader | 4.0 | Newtonsoft.Json | ✅ |
| 2015 | MLeader | 4.5 | Newtonsoft.Json | ✅ |
| 2025 | MLeader | 8.0 | System.Text.Json | ✅ |

### 经验教训（多版本适配）

#### 9. MLeader API 名称与文档不一致

**问题现象**：编译时报 CS1061（未包含定义）或 CS0246（类型未找到）。

**根本原因**：AutoCAD .NET API 的实际名称与网上文档/示例常有不一致：

| 误写 | 正确名称 |
|------|----------|
| `mleader.TextPosition` | `mleader.TextLocation` |
| `mleader.AddVertex(idx, pt)` | `mleader.AddLastVertex(idx, pt)` |
| `LeaderLineType.Splines` | `LeaderType.SplineLeader` |
| `LeaderLineType.Straight` | `LeaderType.StraightLeader` |
| `db.MLeaderStyle` | `db.MLeaderstyle`（小写 s） |
| `mleader.GetLeaderLines()` | 不存在，用 `LeaderLineCount` + `GetLastVertex(0)` |
| `style.TextLeftAttachmentType` | 不存在，只有 `TextAttachmentType` |

**解决方案**：使用 `System.Reflection.MetadataLoadContext` 加载 acdbmgd.dll 元数据，反射探测真实 API。不要用 `Assembly.LoadFrom`（会因原生依赖报 FileNotFoundException）。

#### 10. .NET 8 ImplicitUsings 导致命名空间冲突

**问题现象**：2025 版编译报 CS0104（"Application"/"Exception" 歧义）。

**根本原因**：.NET 8 SDK 风格 csproj 启用 `ImplicitUsings` + `UseWindowsForms` 后，`System.Windows.Forms.Application` 与 `Autodesk.AutoCAD.ApplicationServices.Application` 冲突；`Autodesk.AutoCAD.Runtime.Exception` 与 `System.Exception` 冲突。

**解决方案**：在文件头添加别名：
```csharp
using AppAcad = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;
```

#### 11. ObjectARX SDK 是 DLL 的官方获取渠道

编译不需要安装对应版本的 AutoCAD。从 [ObjectARX SDK](https://aps.autodesk.com/developer/overview/autocad-objectarx-sdk-downloads) 下载对应版本，解压后在 `inc/` 目录即可获取 acdbmgd.dll、acmgd.dll、accoremgd.dll。

#### 12. PowerShell Set-Content 破坏文件编码

**问题现象**：用 PowerShell 的 `-replace` + `Set-Content` 修改 .cs 文件后，中文注释变为乱码。

**根本原因**：`Set-Content` 默认使用系统编码（GBK）写入，而原文件是 UTF-8。

**解决方案**：始终使用 SearchReplace 工具修改文件，不要用 PowerShell 的 Set-Content。

---

## 版本变更记录

### v2.10 → v2.11

| 改动 | 涉及文件 | 说明 |
|------|----------|------|
| 三层自动加载策略 | `install-2007.vbs` | 基于探针工具(Probe Results)采集的运行环境数据，将原来手动 NETLOAD 改为三层自动加载：HKCU 注册表 → acad.lsp 部署 → 手动 LSP 兜底 |
| VBA 安装脚本重写 | `install-vba.vbs` | 解决 Normal.dotm 文件锁定问题，增加进程检测和三级保存策略 |
| VBA 解析逻辑重写 | `Patterns.bas`, `DictModel.bas` | 支持 1102A/1102B 标号、多种分隔符、中文数字枚举 |
| 编码修复 | 所有 `.bas` 文件 | 解决中文乱码问题（详见"经验教训"） |
| 运行时错误 5021 修复 | `Patterns.bas` | 移除 VBScript.RegExp 不支持的语法 |

### v2.11 → v2.12

| 改动 | 涉及文件 | 说明 |
|------|----------|------|
| U形架误判修复 | `Patterns.bas` | `[A-Za-z]?` → `[A-Fa-f]?`，限制字母后缀范围 |
| 章节检测增强 | `DictModel.bas` | 新增"附图标记说明如下："精确匹配模式 |
| README 补充格式文档 | `README.txt` | 详细列出所有支持的附图标记说明格式 |
| 打包结构修正 | zip 包 | VBA 文件必须在 `vba\` 子文件夹内，排除 JSON 文件 |

---

## 经验教训（重要！）

### 1. VBA 文件编码问题

**问题现象**：VBA 模块中的中文全部显示为乱码。

**根本原因**：VBA 编辑器要求源文件使用 ANSI 编码（中文 Windows 下即 GBK/GB2312）。如果以 UTF-8 保存 `.bas` 文件，导入后中文全部乱码。

**解决方案**：
- 所有 `.bas` 文件必须以 **GBK 编码**保存
- 正则表达式中需要匹配的中文字符，使用 `ChrW()` 函数构建，而非直接写入中文字面量
- 示例：

```vb
' 错误写法（依赖文件编码，UTF-8下会乱码）
re.Pattern = "(\d+)[、：:]\s*([\u4e00-\u9fa5]+)"

' 正确写法（跨编码安全）
Dim dunHao, maoHao, cjk
dunHao = ChrW(&H3001)    ' 、
maoHao = ChrW(&HFF1A)    ' ：
cjk = ChrW(&H4E00) & "-" & ChrW(&H9FA5)
re.Pattern = "(\d+)[" & dunHao & maoHao & ":]\s*([" & cjk & "]+)"
```

**注意事项**：
- 用 PowerShell 写文件时指定编码：`[System.Text.Encoding]::GetEncoding("GBK")`
- 用 ADODB.Stream 读写时设置 `Charset = "utf-8"`（用于读取 docx 内容）
- VBS 脚本本身（`.vbs`）使用系统默认编码即可，但路径中含中文时需注意

### 2. VBScript.RegExp 的正则语法限制

**问题现象**：运行时报"运行时错误 5021：应用程序定义或对象定义错误"，出错行为 `Set m = re.Execute(text)`。

**根本原因**：VBScript.RegExp（即 `CreateObject("VBScript.RegExp")`）是 IE 时代的正则引擎，功能非常有限：
- ❌ 不支持非贪婪量词 `*?`、`+?`
- ❌ 不支持零宽断言（前瞻 `(?=...)`、后顾 `(?<=...)`）
- ❌ 不支持命名分组 `(?<name>...)`
- ❌ 不支持 `\uXXXX` Unicode 转义
- ✅ 仅支持：基本量词 `*`、`+`、`?`、`{n,m}`、字符类 `[]`、分组 `()`、交替 `|`、锚点 `^`、`$`、`\d`、`\w`、`\s`

**解决方案**：
- 将所有非贪婪匹配改为贪婪匹配 + 边界控制
- 将前瞻断言改为显式字符类边界
- 示例：

```vb
' 错误（VBScript.RegExp 不支持）
re.Pattern = "(\d+?)(?=[\u4e00-\u9fa5])"

' 正确
re.Pattern = "(\d{1,5})([" & cjk & "A-Za-z][" & nameChars & "]*)"
```

**验证方法**：修改正则后务必用 `cscript` 运行测试脚本验证，不要假设语法正确。

### 3. Normal.dotm 文件锁定问题

**问题现象**：VBA 安装脚本执行时，保存 Normal.dotm 弹出对话框无响应；或提示"文件正在被其他用户使用"、"文件只读"。

**根本原因**：
- Word 启动时会锁定 Normal.dotm（即使没有打开任何文档）
- 多个 Word 实例同时存在时，文件句柄不会释放
- 仅设置文件属性为非只读是不够的，问题在于进程级文件锁

**解决方案**（三级策略）：
1. 安装前检测并关闭所有 `WINWORD.EXE` 进程
2. 保存时尝试三级降级：`doc.Save` → `doc.SaveAs` → 临时文件 + 关闭 Word + 替换原文件
3. 设置 `wd.DisplayAlerts = 0` 和 `SaveNormalPrompt = False` 抑制所有对话框

```vb
' 关键代码
Set wmi = GetObject("winmgmts:\\.\root\cimv2")
Set procs = wmi.ExecQuery("SELECT * FROM Win32_Process WHERE Name='WINWORD.EXE'")
For Each p In procs
    p.Terminate
Next
WScript.Sleep 2000
```

### 4. 零件编号字母后缀的误判

**问题现象**：`2442U形架` 被错误解析为编号 `2442U` + 名称 `形架`。

**根本原因**：正则中 `[A-Za-z]?` 允许任意字母作为编号后缀，但工程图纸中编号后缀通常为 A-F（表示变体/版本），而 U、T、L 等字母往往是名称的一部分（如 U形架、T型槽、L形板）。

**解决方案**：将 `[A-Za-z]?` 限制为 `[A-Fa-f]?`。

```vb
' 修复前
re.Pattern = "\d{1,5}[A-Za-z]?"

' 修复后
re.Pattern = "\d{1,5}[A-Fa-f]?"
```

**注意**：如果将来遇到 G-Z 后缀的编号需求，需要在此处扩展。

### 5. 完整文档中的章节检测

**问题现象**：仅包含附图标记说明的测试文档能正确提取，但完整申请文件（含权利要求书、说明书正文等）会提取到正文中的数字引用。

**根本原因**：完整文档中正文大量出现"如图1所示"、"部件100包括"等引用，如果不先定位"附图标记说明"章节，正则会匹配到正文内容。

**解决方案**：
- 在 `DictModel.bas` 中实现章节定位逻辑
- 支持 12 种标题格式（附图标记说明如下：、附图说明：、标记：等）
- 定位到标题后，截取到下一个双换行为止作为提取范围
- 如果检测失败则回退到全文（并输出警告）

### 6. 打包结构要求

**问题现象**：VBA 文件散落在 zip 根目录，安装脚本找不到。

**正确结构**：
```
PatentMarker-2007-v2-XX.zip
├── PatentMarker.dll
├── install-2007.vbs
├── install-2007.bat
├── install-vba.vbs
├── uninstall-2007.vbs
├── README.txt
├── test-patterns.vbs
├── install-vba-test.vbs
└── vba/
    ├── AutoExport.bas
    ├── clsSaveHook.cls
    ├── DictModel.bas
    ├── JsonWriter.bas
    ├── PatentExtractor.bas
    └── Patterns.bas
```

**注意**：
- 不要将 JSON 文件打入包中（测试产物）
- 版本号按顺序递增（v2-10, v2-11, v2-12...），不要重复使用同一版本号
- 打包前用 `Compress-Archive` 的 staging 目录方式确保结构正确

### 7. VBS 脚本中的中文路径

**问题现象**：VBS 脚本中硬编码含中文的路径字符串时，`fso.FileExists()` 返回 False。

**根本原因**：VBS 文件的编码与系统代码页不匹配时，中文字符串会损坏。

**解决方案**：
- 不要在 VBS 中硬编码中文路径
- 使用 `WScript.ScriptFullName` 获取脚本所在目录，动态查找目标文件
- 用 `InStr(fileName, "关键词")` 做模糊匹配

```vb
' 错误
docPath = "c:\Users\wjm\WorkBuddy\2026-06-20-00-50-28\MU26005942.2稿(1).docx"

' 正确
Set folder = fso.GetFolder(baseDir)
For Each file In folder.Files
    If InStr(file.Name, "MU26005942") > 0 And LCase(fso.GetExtensionName(file.Name)) = "docx" Then
        docPath = file.Path
        Exit For
    End If
Next
```

### 8. VBScript 中不可用的 .NET 对象

**问题现象**：`CreateObject("System.Text.StringBuilder")` 报错"不能支持 Automation 类型"。

**根本原因**：VBScript 的 `CreateObject` 只能创建 COM 对象，不能直接实例化 .NET 类（除非注册为 COM 可见）。

**可用的对象**：
- ✅ `Scripting.FileSystemObject`
- ✅ `VBScript.RegExp`
- ✅ `ADODB.Stream`
- ✅ `Word.Application`
- ✅ `System.Collections.ArrayList`（.NET COM 互操作，通常可用）
- ❌ `System.Text.StringBuilder`（不可用）
- ❌ `System.IO.File`（不可用）

**替代方案**：字符串拼接直接用 `&` 运算符即可。

---

## 测试方法

### 本地测试脚本

项目中有两个测试脚本可用于验证 VBA 逻辑：

1. **`批量测试\test-full-extract.vbs`** — 批量测试，读取 `.txt` 文件模拟提取
2. **`test-single-export.vbs`** — 单文件测试，通过 Word COM 打开 `.docx` 并导出 JSON

运行方式：
```powershell
cscript //nologo "批量测试\test-full-extract.vbs" > test-output.txt 2>&1
cscript //nologo "test-single-export.vbs" > test-single-output.txt 2>&1
```

### 测试要点

- 章节检测：是否从完整文档中正确定位"附图标记说明"段落
- 编号提取：纯数字、数字+字母后缀(A-F)、多编号共享名称
- 分隔符：冒号、顿号、分号、逗号、破折号、斜杠、空格、无分隔
- 中文枚举：一、弹簧 / 二、连接件 格式
- 特殊名称：U形架、T型槽等（字母不应被吞入编号）
- JSON 输出：UTF-8 编码、格式有效、无重复键

### 测试文件

`批量测试\` 文件夹包含 8 个真实申请文件（已删除图片），覆盖各种格式：
- 顿号+分号混合分隔
- 数字+字母后缀（1342A、1342B）
- 多级编号（100、1100、1110）
- 中文数字枚举

---

## 关键文件说明

| 文件 | 编码 | 说明 |
|------|------|------|
| `deploy/vba/*.bas` | GBK | VBA 源码，必须 GBK 编码 |
| `deploy/vba/Patterns.bas` | GBK | 核心正则，中文用 ChrW() |
| `deploy/vba/DictModel.bas` | GBK | 章节检测 + 字典模型 |
| `deploy/install-vba.vbs` | 系统默认 | VBA 安装脚本 |
| `deploy/install-2007.vbs` | 系统默认 | CAD 插件安装脚本 |
| `deploy/README.txt` | GBK | 用户安装说明 |
| `批量测试/test-full-extract.vbs` | 系统默认 | 批量测试脚本 |

---

## 后续待办 / 已知限制

1. **字母后缀范围**：当前仅支持 A-F，若遇到 G-Z 后缀需扩展 `Patterns.bas`
2. **章节结束判断**：当前以双换行为章节结束标志，极端情况下可能截断过长段落
3. **AutoCAD 2007 注册表**：HKCU 方式在部分 ACAD 2007 环境下不生效，需依赖 LSP 兜底
4. **Word 版本兼容**：当前仅在 Word 2010 上验证，其他版本未测试
5. **并发 Word 实例**：安装脚本会强制关闭所有 Word 进程，生产环境需提醒用户保存工作
