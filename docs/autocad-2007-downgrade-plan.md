# PatentMarker → AutoCAD 2007 / Win7 降级实现方案

## 一、目标环境

| 项目 | 值 |
|------|-----|
| 操作系统 | Windows 7 (x86/x64) |
| AutoCAD 版本 | 2007（内部版本号 R17.0） |
| .NET 运行时 | 2.0（CLR 2.0）；Win7 自带 .NET 3.5 可选用 |
| 托管程序集 | acdbmgd.dll + acmgd.dll（无 accoremgd） |
| 构建工具 | VS2008/2010 或现代 VS + 旧格式 csproj |
| 部署方式 | 注册表 HKCU 自动加载（无 ApplicationPlugins） |

---

## 二、核心架构变更：MLeader → Leader + MText

AutoCAD 2007 **没有 MLeader**（2008/R17.1 引入）。必须用旧式 `Leader`（继承自 `Dimension`）+ 独立 `MText` 组合替代。

### 2.1 实体模型对比

```
2014/2026 版：
  MLeader（一体式：引线 + 文字 + 样式）
    ├── ContentType = MTextContent
    ├── MText（内嵌）
    ├── TextPosition / TextLocation
    ├── MLeaderStyle → PAT_STYLE
    └── LeaderLine vertices

2007 版（v2 增强：样条曲线 + 无限拐点 + 默认无箭头）：
  Leader（引线，继承 Dimension）
    ├── AppendVertex(pt) × N        ← 支持无限拐点（循环采集）
    ├── IsSplined = true            ← v2：样条曲线引线（非直线段）
    ├── HasArrowHead = false        ← v2：默认无箭头（面板可一键切换）
    ├── DimensionText = "编号"（或空，用 Annotation 关联）
    ├── Annotation = MText.ObjectId（关联文字）
    ├── DimStyle → PAT_DIM（标注样式）
    └── 独立 MText 实体
         ├── Contents = "编号"
         ├── TextHeight = 3.5
         ├── TextStyleId → TIMES_ROMAN
         └── Location = textPt
```

### 2.2 识别 PAT_STYLE 的方式变更

2014 版通过 `mleader.MLeaderStyle` → `MLeaderStyle.Name == "PAT_STYLE"` 识别。

2007 版改为：通过 `Leader.DimensionStyle` → `DimStyleTableRecord.Name == "PAT_DIM"` 识别。
同时校验 `leader.Annotation != ObjectId.Null` 且关联的 MText 存在。

---

## 三、逐文件实现方案

### 3.1 PatentMarker.csproj（新建 cad-plugin-2007/PatentMarker/）

```xml
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <TargetFrameworkVersion>v2.0</TargetFrameworkVersion>
    <OutputType>Library</OutputType>
    <RootNamespace>PatentMarker</RootNamespace>
    <AssemblyName>PatentMarker</AssemblyName>
    <PlatformTarget>x86</PlatformTarget>  <!-- 2007 多为 32 位 -->
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="acdbmgd">
      <HintPath>C:\Program Files\Autodesk\AutoCAD 2007\acdbmgd.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="acmgd">
      <HintPath>C:\Program Files\Autodesk\AutoCAD 2007\acmgd.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="System" />
    <Reference Include="System.Windows.Forms" />
    <Reference Include="System.Drawing" />
    <Reference Include="System.Data" />
    <Reference Include="System.Xml" />
  </ItemGroup>
</Project>
```

**关键决策**：
- 目标 .NET 2.0（最安全）。若确认目标机有 .NET 3.5（Win7 默认有），可改为 v3.5 以保留 LINQ。
- **不引用 accoremgd**（2007 无此程序集）。
- **不引用 Newtonsoft.Json**（13.x 需 .NET 4.0+）。改用内置 JSON 解析器（见 3.6）。
- PlatformTarget 改 x86（AutoCAD 2007 主流为 32 位；若目标机是 64 位 2007 则改 x64）。

### 3.2 PatMarkCommand.cs — 完全重写

**核心逻辑**：创建 `Leader` + `MText` 组合。

**v2 增强（样条曲线 + 无限拐点 + 默认无箭头）**：
- 交互流程改为：点击附着点 → 循环点击拐点（回车/空格结束）→ 点击文字位置
- 拐点数量无限制：用户可连续点击多个拐点，直到回车/空格/Esc 结束拐点采集
- `leader.IsSplined = true`：引线以样条曲线形式平滑连接所有顶点
- `leader.HasArrowHead = PatPaletteCommand.HasArrowHead`：默认 false（无箭头），由面板开关控制

```csharp
// 伪代码骨架（v2）
private void CreateLeaderWithText(Database db, Point3d attachPt, List<Point3d> doglegPts, Point3d textPt, string number)
{
    Transaction tr = db.TransactionManager.StartTransaction();
    try
    {
        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

        // 1. 创建 MText
        MText mt = new MText();
        mt.SetDatabaseDefaults(db);
        mt.Contents = number;
        mt.TextHeight = PatPaletteCommand.TextHeight;
        mt.Location = textPt;
        ObjectId fontId = PatStyleInitializer.GetOrCreateTimesRoman(db, tr);
        if (!fontId.IsNull) mt.TextStyleId = fontId;
        btr.AppendEntity(mt);
        tr.AddNewlyCreatedDBObject(mt, true);

        // 2. 创建 Leader（v2：样条曲线 + 无限拐点 + 默认无箭头）
        Leader leader = new Leader();
        leader.SetDatabaseDefaults(db);
        leader.AppendVertex(attachPt);              // 箭头端（起点）
        foreach (Point3d p in doglegPts)            // v2：循环追加所有拐点
            leader.AppendVertex(p);
        leader.IsSplined = true;                    // v2：样条曲线
        leader.HasArrowHead = PatPaletteCommand.HasArrowHead;  // v2：默认 false，面板可切换
        leader.Annotation = mt.ObjectId;            // 关联文字
        leader.DimensionStyle = PatStyleInitializer.GetPatDimStyleId(db, tr);
        btr.AppendEntity(leader);
        tr.AddNewlyCreatedDBObject(leader, true);

        tr.Commit();
    }
    catch
    {
        tr.Abort();
        throw;
    }
    finally
    {
        tr.Dispose();
    }
}
```

**交互流程（v2）**：
1. 点击附着点（箭头端/起点）
2. 循环点击拐点（每点一个拐点后提示继续，回车/空格结束拐点采集）
3. 点击文字位置
4. 循环回到第 1 步（同编号可连续标注多处）

**拐点采集实现**：使用 `PromptPointOptions` + `AllowNone = true`，用户输入回车/空格时 `Status == None` 结束采集。至少需要 1 个拐点（即附着点 + 拐点 + 文字位置 = 3 点）；用户可继续点击更多拐点形成复杂路径。

**与 2014 版的差异**：
- 去掉 `mleader.SetDogleg()`（Leader 无此概念，拐点即后续 vertex）
- 去掉 `ContentType`、`TextPosition` 等 MLeader 专属属性
- 文字位置由 MText.Location 控制，不再由 MLeader 管理
- v2：样条曲线（`IsSplined`）替代直线段；拐点数量不受限

### 3.3 PatStyleInitializer.cs — 改为 DimStyle 管理

**删除**：所有 MLeaderStyle 相关代码。

**新增**：创建 `PAT_DIM` 标注样式（DimStyleTableRecord）。

```csharp
public static class PatStyleInitializer
{
    public const string DimStyleName = "PAT_DIM";
    public const string TextStyleName = "TIMES_ROMAN";

    public static void EnsurePatDimStyle()
    {
        // 检查 DimStyleTable 中是否已有 PAT_DIM
        // 若无，创建并设置：
        //   Dimasz = 2.5（箭头大小）
        //   Dimgap = 0.625（文字间距）
        //   Dimtxt = 3.5（文字高度）
        //   Dimtxsty = TIMES_ROMAN
        //   Dimldrblk = 默认箭头
    }

    public static ObjectId GetPatDimStyleId(Database db, Transaction tr)
    {
        DimStyleTable dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
        if (dst.Has(DimStyleName))
            return dst[DimStyleName];
        return db.Dimstyle; // 回退到当前标注样式
    }

    public static ObjectId GetOrCreateTimesRoman(Database db, Transaction tr)
    {
        // 与 2014 版逻辑相同，但去掉 using var 语法
    }
}
```

**注意**：2007 的 `Leader` 继承 `Dimension`，其外观由 `DimStyleTableRecord` 控制。PAT_DIM 样式确保引线箭头、文字大小一致。

**v2 箭头默认值**：`Leader.HasArrowHead` 的默认值由 `PatPaletteCommand.HasArrowHead`（静态字段，初始 `false`）控制，不再在 DimStyle 中硬编码。DimStyle 的 `Dimasz` 仍保留（用户开启箭头时控制箭头大小）。

### 3.4 PatAlignCommand.cs — 重写

**删除**：
- `ed.Command("_.MLEADERALIGN", ...)` — 2007 无此方法也无此命令
- 所有 `MLeader` / `MLeaderStyle` 引用

**Select 模式替代方案**：
手动实现对齐逻辑（选中多个 Leader+MText 组合，统一移动文字到同一 X 或 Y 坐标）。

**Frame 模式**：
- 遍历选中的 `Leader` 实体
- 通过 `leader.DimensionStyle` → `DimStyleTableRecord.Name == "PAT_DIM"` 过滤
- 读取关联 MText 的 `Location`，移动到框边 + margin
- 同时移动 Leader 的最后一个 vertex（拐点）以保持引线连贯

```csharp
// 识别 PAT 引线的辅助方法
private static bool IsPatLeader(Leader leader, Transaction tr)
{
    if (leader.DimensionStyle.IsNull) return false;
    DimStyleTableRecord dsr = (DimStyleTableRecord)tr.GetObject(leader.DimensionStyle, OpenMode.ForRead);
    return dsr.Name == PatStyleInitializer.DimStyleName;
}
```

### 3.5 PatCheckCommand.cs — 重写扫描逻辑

**变更**：
- 遍历 ModelSpace 中所有 `Leader` 实体（DxfName == "LEADER"）
- 通过 DimStyle 名过滤 PAT_DIM
- 读取文字：优先 `leader.Annotation` → 打开 MText → `.Contents`；回退 `leader.DimensionText`
- 位置：取 MText.Location 或 Leader 最后一个 vertex

**去掉**：
- 报告保存到文件的交互提示（`PromptKeywordOptions` 在命令末尾追问）— 保留但改为直接保存，减少交互步骤
- 所有 LINQ（`.Except`、`.OrderBy`、`.Where`、`.Select`、`.FirstOrDefault`、`.Take`）→ 改为手动循环 + `List.Sort()`

### 3.6 IO/ConfigLoader.cs + IO/DictEntry.cs — JSON 解析替换

**问题**：Newtonsoft.Json 13.x 需 .NET 4.0+；6.x 支持 .NET 2.0 但引入额外 DLL。

**方案 A（推荐）：内置极简 JSON 解析器**

dict.json 和 config.json 结构固定且简单，手写一个 ~150 行的递归下降解析器即可：

```csharp
// IO/SimpleJson.cs — 极简 JSON 解析（仅支持 object/array/string/number/bool/null）
public static class SimpleJson
{
    public static Dictionary<string, object> ParseObject(string json) { ... }
    // 递归解析，返回嵌套 Dictionary/List/string/double/bool
}
```

然后 `DictLoader` 和 `ConfigLoader` 直接调用 `SimpleJson.ParseObject()` 后手动映射字段。

**方案 B：Newtonsoft.Json 6.0.8**（最后支持 .NET 2.0 的版本）
- 需随 DLL 部署 Newtonsoft.Json.dll（~400KB）
- 代码改动最小（仅改 `[JsonProperty]` 属性）
- 但增加部署复杂度

**推荐方案 A**，理由：
1. 零外部依赖，单 DLL 部署
2. dict.json 结构固定，不需要通用 JSON 库
3. 避免 Win7 上 .NET 2.0 + Newtonsoft 6.x 的潜在兼容问题

### 3.7 Palette/PatPaletteCommand.cs — 适配 2007 PaletteSet

**2007 的 PaletteSet API 差异**：
- 构造函数 `PaletteSet(string name)` 可用（Guid 重载也存在但行为不同）
- `PaletteSetStyles` 枚举值更少（无 `ShowPropertiesMenu`）
- `MinimumSize` 属性存在
- `Add(string, Control)` 方法存在

**修改**：
```csharp
_paletteSet = new PaletteSet("PatentMarker");
_paletteSet.Style = PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton;
_paletteSet.MinimumSize = new System.Drawing.Size(280, 400);
_paletteSet.Visible = true;
_paletteSet.Add("Dictionary", _control);
```

**`SendStringToExecute` 签名**：
2007 中为 `Document.SendStringToExecute(string, bool, bool, bool)` — 4 参数版本存在，保持不变。

### 3.8 Palette/DictPaletteControl.cs — 语法降级 + 修复

**语法降级**（去 C# 6+ 特性）：
- `$"..."` → `string.Format("...", args)` 或手动拼接
- `?.` → 显式 null 检查
- `using var` → `using (...) { }` 块
- `nameof()` → 字符串字面量
- 属性初始化器 `= new()` → 构造函数中赋值
- `is PaletteEntry entry` 模式匹配 → `as` + null 检查

**字体**：`"Microsoft YaHei UI"` → `"Microsoft Sans Serif"`（Win7 无 YaHei UI）

**v2 新增：箭头开关按钮**
- 按钮栏新增"箭头开/关"按钮（`_btnArrow`），切换 `PatPaletteCommand.HasArrowHead` 静态字段
- 按钮文字根据当前状态显示"箭头:关"或"箭头:开"，点击后立即切换并刷新文字
- 仅影响**后续创建**的引线（已存在的引线不回溯修改，避免破坏图纸）
- 与"字高"控件并列，属于"创建参数"区，不触发数据库操作

### 3.9 PatentMarkerApp.cs — 语法降级

- 去掉 LINQ（`FirstOrDefault`）→ foreach 循环
- 去掉 `$""` 插值
- 去掉 `?.`
- 保留 RawLog 逻辑（`File.AppendAllText` 在 .NET 2.0 中可用）

### 3.10 部署脚本 deploy/install-2007.ps1

```powershell
# 注册表路径
$AcadVersion = "R17.0"
$ProductCode = "ACAD-5001:804"  # 中文 AutoCAD 2007（需确认目标机实际值）

$appKey = "HKCU:\Software\Autodesk\AutoCAD\$AcadVersion\$ProductCode\Applications\PatentMarker"
# LOADER = 单 DLL 路径（无 Newtonsoft 依赖）
# LOADCTRLS = 1
# MANAGED = 1
```

**删除**：
- `deploy/PatentMarker2014.bundle/` 整个目录
- `deploy/make-bundle-2014.ps1`
- `deploy/PackageContents.xml`

---

## 四、现有代码质量问题（在本次降级中一并修复）

### 4.1 架构/设计问题

| # | 问题 | 位置 | 修复方案 |
|---|------|------|----------|
| D1 | **静态可变状态共享**：`PendingNumber`/`PendingName`/`TextHeight` 是 static 属性，多文档场景下有竞态风险 | PatPaletteCommand.cs | 改为实例字段，通过单例 `_instance` 访问；或加 `lock` |
| D2 | **无文档锁**：`BtnExplode_Click` 从 WinForms 事件直接修改数据库，未获取 DocumentLock | DictPaletteControl.cs | 包裹 `using (doc.LockDocument()) { ... }` |
| D3 | **Initialize 中创建样式无意义**：NETLOAD 时可能无活动文档，`EnsurePatStyle()` 大概率空跑 | PatentMarkerApp.cs | 删除 Initialize 中的样式创建，仅保留 config 加载；样式在首次命令执行时懒创建 |
| D4 | **DictLoader 加载时向 Editor 输出信息**：每次加载字典都打印路径到命令行，干扰用户 | DictEntry.cs (DictLoader) | 改为仅写 RawLog，不写 Editor；或加 verbose 开关 |
| D5 | **2026 版 ConfigLoader 仍用 `AppDomain.BaseDirectory`**：2014 版已修复为 `Assembly.GetExecutingAssembly().Location`，但 2026 版未同步 | cad-plugin/IO/ConfigLoader.cs L78 | 2007 版直接用 `Assembly.GetExecutingAssembly().Location` |

### 4.2 代码风格/机械死板问题

| # | 问题 | 位置 | 修复方案 |
|---|------|------|----------|
| S1 | **过度日志**：2026 版 `CreateMLeader` 有 ~20 行 log 调用，每个 API 调用前后都打日志 | cad-plugin PatMarkCommand.cs | 2007 版仅保留 START/END/ERROR 三级日志 |
| S2 | **NaturalSort 名不副实**：只处理纯数字字符串，"10a" 排在 "9" 后面（按字典序） | DictPaletteControl.cs | 实现真正的自然排序（逐字符比较数字段） |
| S3 | **重复的 PAT_STYLE 检查逻辑**：PatAlignCommand、PatCheckCommand、BtnExplode_Click 三处各自写了一遍"打开 style → 比对 Name"的逻辑 | 多文件 | 抽取为 `PatEntityHelper.IsPatEntity(Entity, Transaction)` 工具方法 |
| S4 | **事务模式不一致**：有的用 `using var tr = ...`（2014），有的用 `using (...) { }`（2014 PatCheckCommand），有的不 abort | 多文件 | 统一为 `using (Transaction tr = ...) { try { ... tr.Commit(); } finally { } }` 模式 |
| S5 | **硬编码 GUID**：PaletteSet 的 Guid 是随意编造的固定值 | PatPaletteCommand.cs | 保留（PaletteSet 需要稳定 Guid 来记忆停靠位置），但加注释说明 |
| S6 | **BtnExplode 的 "兼容 AutoCAD 2007" 提示语**：2026 版的 explode 输出写"兼容 AutoCAD 2007"，但 2007 版根本不需要 explode（Leader 已是基本实体） | DictPaletteControl.cs L377 | 2007 版删除 Explode 按钮，或改为"删除所有 PAT 引线" |
| S7 | **config.json 中 `fontName: "仿宋_GB2312"` 未被代码使用**：代码硬编码 Times New Roman，config 中的 fontName 字段从未读取 | config.json + PatStyleInitializer | 要么读取 config 的 fontName，要么从 config 中删除此字段 |
| S8 | **2014 版 Palette 标签用英文，2026 版用中文**：UI 语言不一致 | 两版 DictPaletteControl | 2007 版统一用中文（目标用户为中文专利代理人） |

### 4.3 潜在 Bug

| # | 问题 | 位置 | 修复方案 |
|---|------|------|----------|
| B1 | **`GetOrCreateTimesRoman` 在事务嵌套中调用**：PatMarkCommand.CreateMLeader 已开启事务，内部又调用 `GetOrCreateTimesRoman(db)` 再开一个事务 → 嵌套事务在 2007 中行为不确定 | PatMarkCommand + PatStyleInitializer | 改为传入外层 Transaction，不再内部新开 |
| B2 | **`GetPatStyleId` 同上**：在已有事务上下文中又开新事务 | PatMarkCommand L127 | 同上，传入外层 tr |
| B3 | **Leader.Annotation 设置时机**：必须先 AppendEntity(MText) 获得 ObjectId，再设 leader.Annotation；若顺序反了会抛 eNullObjectId | 新 PatMarkCommand | 严格按：MText append → 获 id → Leader append → 设 Annotation |
| B4 | **`string.IsNullOrWhiteSpace`**：.NET 4.0 方法，2007 版不可用 | 多处 | 替换为 `s == null || s.Trim().Length == 0` |
| B5 | **`Path.GetDirectoryName` 返回 null 时**：`Path.Combine(null, ...)` 在 .NET 2.0 抛 ArgumentNullException | DictLoader | 加 null 守卫 |
| B6 | **DocumentActivated 事件泄漏**：若 Terminate 未被调用（AutoCAD 崩溃），静态事件处理器持有已释放控件引用 | PatPaletteCommand | 在 handler 内加 `_control == null` 守卫 |

### 4.4 两版之间的不一致（降级时统一）

| # | 不一致 | 说明 |
|---|--------|------|
| I1 | 2026 用 SplineLeader，2014 用 Straight | 2007 的 Leader 天然就是直线段，无此问题 |
| I2 | 2026 的 `TextAttachmentType.AttachmentMiddle` vs 2014 的 `MiddleOfTop` | 2007 无此属性（由 DimStyle 控制） |
| I3 | 2026 ConfigLoader 用 `AppDomain.BaseDirectory`，2014 用 `Assembly.Location` | 2007 版用 `Assembly.Location`（正确） |
| I4 | 2026 Palette 标签中文，2014 英文 | 2007 版统一中文 |
| I5 | 2026 有报告保存交互，2014 无 | 2007 版保留（实用功能） |

---

## 五、文件清单（cad-plugin-2007/PatentMarker/）

```
cad-plugin-2007/
  PatentMarker/
    PatentMarker.csproj          ← 旧格式 csproj, .NET 2.0, x86
    PatentMarkerApp.cs           ← IExtensionApplication, 语法降级
    Commands/
      PatMarkCommand.cs          ← 完全重写（Leader + MText）
      PatAlignCommand.cs         ← 重写（操作 Leader/MText 位置）
      PatCheckCommand.cs         ← 重写（遍历 Leader）
    Styles/
      PatStyleInitializer.cs     ← 重写（DimStyle 替代 MLeaderStyle）
    Palette/
      PatPaletteCommand.cs       ← 适配 2007 PaletteSet API
      DictPaletteControl.cs      ← 语法降级 + 修复 D2
    IO/
      ConfigLoader.cs            ← 去 Newtonsoft, 用 SimpleJson
      DictEntry.cs               ← 去 Newtonsoft, 去 LINQ
      SimpleJson.cs              ← 新增：极简 JSON 解析器
      PatEntityHelper.cs         ← 新增：PAT 实体识别工具方法
  README.md
```

---

## 六、C# 语法降级清单

以下 C# 6+/3.5+ 特性在 .NET 2.0 中**不可用**，必须替换：

| 特性 | 示例 | 替代 |
|------|------|------|
| 字符串插值 | `$"text {x}"` | `string.Format("text {0}", x)` 或 `"text " + x` |
| null 条件运算符 | `obj?.Method()` | `if (obj != null) obj.Method();` |
| null 合并赋值 | `x ??= val` | `if (x == null) x = val;` |
| using 声明 | `using var tr = ...;` | `using (var tr = ...) { }` |
| 模式匹配 | `if (x is Foo f)` | `Foo f = x as Foo; if (f != null)` |
| LINQ | `.Where().Select().OrderBy()` | foreach + List.Sort(Comparison) |
| 扩展方法 | `str.Contains(...)` 无问题，但 LINQ 扩展方法不可用 | 手动循环 |
| 自动属性初始化 | `public int X { get; set; } = 5;` | 构造函数中赋值 |
| `nameof()` | `nameof(Field)` | `"Field"` 字符串字面量 |
| `string.IsNullOrWhiteSpace` | — | `s == null || s.Trim().Length == 0` |
| 集合初始化器 | `new List<int> { 1, 2 }` | 可用（C# 3.0 但 .NET 2.0 编译器支持）* |
| 对象初始化器 | `new Foo { X = 1 }` | 可用（同上）* |
| `var` 关键字 | `var x = ...` | 可用（C# 3.0）* |

> *注：若用 VS2008+ 编译（C# 3.0 编译器），`var`、对象/集合初始化器、lambda 均可用，只要不引用 System.Core.dll（LINQ）。若用 VS2005（C# 2.0），则全部不可用。**建议用 VS2010+ 编译，目标 .NET 2.0，可用 C# 3.0 语法但不用 LINQ。**

---

## 七、构建与部署流程

### 7.1 构建

```
方案 A（推荐）：现代 VS（2019/2022）+ 旧格式 csproj
  - 安装 "Microsoft.NET.Sdk" 不需要，直接用经典 csproj
  - 目标框架选 .NET Framework 2.0
  - 引用 AutoCAD 2007 的 acdbmgd.dll / acmgd.dll（从 2007 安装目录拷贝到构建机）
  - Platform Target: x86（或 AnyCPU + Prefer32Bit=false）
  - 输出：PatentMarker.dll（单文件，无外部依赖）

方案 B：VS2008/2010 直接打开
  - 经典 csproj 格式天然兼容
  - 需手动配置引用路径
```

### 7.2 部署（目标 Win7 + AutoCAD 2007）

```
1. 拷贝 PatentMarker.dll 到固定目录（如 C:\PatentMarker\）
2. 运行 install-2007.ps1（写 HKCU 注册表）
3. 重启 AutoCAD 2007
4. 命令行输入 PATPALETTE 验证
```

### 7.3 注册表键

```
HKEY_CURRENT_USER\Software\Autodesk\AutoCAD\R17.0\ACAD-5001:804\Applications\PatentMarker
  DESCRIPTION = "PatentMarker"  (REG_SZ)
  LOADCTRLS   = 1               (REG_DWORD)
  MANAGED     = 1               (REG_DWORD)
  LOADER      = "C:\PatentMarker\PatentMarker.dll"  (REG_SZ)
```

> 产品代码 `ACAD-5001:804` 需在目标机上确认（打开注册表查看 `R17.0` 下的实际子键名）。

---

## 八、实施顺序

| 步骤 | 内容 | 依赖 |
|------|------|------|
| 1 | 创建 `cad-plugin-2007/` 目录 + csproj | 无 |
| 2 | 实现 `IO/SimpleJson.cs` | 无 |
| 3 | 迁移 `IO/ConfigLoader.cs` + `IO/DictEntry.cs`（去 LINQ/Newtonsoft） | 步骤 2 |
| 4 | 实现 `Styles/PatStyleInitializer.cs`（DimStyle） | 无 |
| 5 | 实现 `IO/PatEntityHelper.cs` | 步骤 4 |
| 6 | 重写 `Commands/PatMarkCommand.cs` | 步骤 4 |
| 7 | 重写 `Commands/PatCheckCommand.cs` | 步骤 3, 5 |
| 8 | 重写 `Commands/PatAlignCommand.cs` | 步骤 5 |
| 9 | 适配 `Palette/PatPaletteCommand.cs` + `DictPaletteControl.cs` | 步骤 3, 6 |
| 10 | 适配 `PatentMarkerApp.cs` | 步骤 4 |
| 11 | 编写 `deploy/install-2007.ps1` | 无 |
| 12 | 在 AutoCAD 2007 环境中编译测试 | 全部 |

---

## 九、风险与预案

| 风险 | 影响 | 预案 |
|------|------|------|
| AutoCAD 2007 的 `Leader.Annotation` 属性行为与高版本不同 | MText 可能不跟随 Leader 移动 | 测试后若不支持 Annotation，改为在 Leader 旁独立放置 MText，不建立关联 |
| 2007 的 PaletteSet 不支持某些 Style 标志 | 面板创建失败 | 最小化 Style 设置，仅用 `ShowCloseButton` |
| Win7 上 .NET 2.0 缺少某些 BCL 方法 | 编译通过但运行时 MissingMethodException | 严格只用 .NET 2.0 BCL；编译后用 `peverify` 检查 |
| 32 位 vs 64 位不匹配 | NETLOAD 失败 | 确认目标 AutoCAD 2007 位数，匹配 PlatformTarget |
| `Document.SendStringToExecute` 在 2007 中参数不同 | 双击列表项无法触发 PATMARK | 回退为仅设置 PendingNumber，用户手动输入 PATMARK |
| 旧式 Leader 的 `DimensionText` 与 `Annotation` 互斥 | 设置 Annotation 后 DimensionText 被清空 | 仅用 Annotation 关联 MText，不设 DimensionText |

---

## 十、不变更的部分

| 组件 | 原因 |
|------|------|
| `extractor/`（VBA 宏） | 运行在 Word 中，与 CAD 版本无关 |
| `md-converter/` | Python 服务，与 CAD 无关 |
| `md-converter-standalone.html` | 已支持 Win7/Chrome 109 |
| `md-converter-extension/` | 已支持 Win7/Chrome 109 |
| `config.json` 结构 | 保持不变（删除未使用的 fontName 字段） |
| dict.json 格式 | 保持不变（VBA 提取器输出不变） |

---

## 十一、v2 功能增强（样条曲线引线 + 无限拐点 + 箭头开关）

> 用户在 v1（直线引线 + 固定2拐点 + 默认有箭头）实测可用后提出的需求。

### 11.1 背景与动机

| v1 行为 | 问题 | v2 目标 |
|---------|------|---------|
| 引线为直线段（折线） | 视觉生硬，专利附图标注希望更平滑 | 样条曲线引线 |
| 固定 2 个顶点（附着点 + 1 拐点） | 复杂图面拐点不够，引线需绕开其他图形 | 无限拐点 |
| 默认 `HasArrowHead = true` | 专利标注惯例多不带箭头 | 默认无箭头 |
| 无切换入口 | 用户偶尔需要箭头 | 面板一键开关 |

### 11.2 设计决策

#### 决策 1：样条曲线引线

- **API**：`Leader.IsSplined = true`（反射探测确认 2007 支持）
- **效果**：Leader 的所有 vertex 作为样条曲线的控制点/拟合点，引线平滑连接
- **不影响**：`Annotation`（MText 关联）、`DimensionStyle`（PAT_DIM）仍正常工作
- **PatEntityHelper 识别**：无需改动 —— 仍按 `DimensionStyle == PAT_DIM` 识别，与 `IsSplined` 无关

#### 决策 2：无限拐点

- **交互流程**：
  1. 点击附着点（起点）
  2. 循环采集拐点：`PromptPointOptions` + `AllowNone = true`，每点一次提示"点击拐点（回车结束）"，用户回车/空格结束
  3. 点击文字位置
- **最少拐点数**：1（附着点 + 1拐点 + 文字位置 = 3 点），保证 Leader 有效
- **最多拐点数**：无限制（`AppendVertex` 无数量上限）
- **Esc 处理**：拐点采集阶段按 Esc 取消整个标注；文字位置阶段按 Esc 取消本次

#### 决策 3：默认无箭头

- **静态状态**：`PatPaletteCommand.HasArrowHead`（新增静态字段，初始 `false`）
- **应用方式**：`leader.HasArrowHead = PatPaletteCommand.HasArrowHead`
- **作用范围**：仅影响后续新建引线，不回溯修改已存在引线（避免破坏图纸）

#### 决策 4：面板箭头开关按钮

- **控件**：新增 `Button _btnArrow`，放在字高栏旁边或按钮栏
- **状态显示**：按钮文字根据当前状态显示"箭头:关"（默认）或"箭头:开"
- **点击行为**：切换 `PatPaletteCommand.HasArrowHead`，更新按钮文字，在状态栏提示
- **不触发数据库操作**：仅修改静态状态，类似字高控件

### 11.3 改动文件清单

| 文件 | 改动 |
|------|------|
| `Commands/PatMarkCommand.cs` | 交互流程改循环采集拐点；`IsSplined=true`；`HasArrowHead` 取自静态字段 |
| `Palette/PatPaletteCommand.cs` | 新增 `public static bool HasArrowHead = false;` |
| `Palette/DictPaletteControl.cs` | 新增箭头开关按钮 `_btnArrow`，点击切换静态字段并刷新文字 |
| `Styles/PatStyleInitializer.cs` | 无需改动（`Dimasz` 保留作为箭头开启时的大小） |
| `IO/PatEntityHelper.cs` | 无需改动（按 DimStyle 识别，与 IsSplined 无关） |

### 11.4 风险与预案

| 风险 | 预案 |
|------|------|
| `IsSplined` 在某些 2007 子版本行为异常 | 实测验证；若异常则保留直线 + 多拐点（去掉样条） |
| 样条曲线引线的 `Annotation`（MText）位置偏移 | 测试 MText.Location 是否仍精确；若偏移则手动微调 |
| 拐点采集时用户误按回车（0 拐点） | 强制至少 1 个拐点，0 拐点时提示并重新采集 |
| 面板按钮状态在多文档间不同步 | `HasArrowHead` 为静态字段，全局生效（与 v1 `TextHeight` 一致） |

---

## 十二、v2 命令拼音别名

> 用户反馈 `PATPALETTE` 等英文命令名不易记忆，希望同时保留原命令名和拼音别名。

### 12.1 设计决策

- **保留原英文命令**：保证向后兼容、普适性
- **新增拼音别名**：通过在同一方法上声明多个 `[CommandMethod]` 特性实现，AutoCAD .NET 原生支持命令别名
- **命名规范**：面板主入口用完整拼音 + 缩写；其他命令用 BZ 前缀 + 后缀首字母

### 12.2 命令对照表

| 原命令 | 拼音别名 | 功能 |
|--------|----------|------|
| `PATPALETTE` | `BIAOZHU` / `BZ` | 打开字典面板 |
| `PATMARK` | `BZM` | 创建引线标注 |
| `PATCHECK` | `BZC` | 校验一致性 |
| `PATALIGN` | `BZA` | 对齐引线 |
| `PATSELECTALL` | `BZS` | 选中所有 PAT 文字（v2.2 新增） |

### 12.3 改动文件清单

| 文件 | 改动 |
|------|------|
| `Commands/PatMarkCommand.cs` | 新增 `[CommandMethod("BZM", ...)]` |
| `Commands/PatCheckCommand.cs` | 新增 `[CommandMethod("BZC", ...)]` |
| `Commands/PatAlignCommand.cs` | 新增 `[CommandMethod("BZA", ...)]` |
| `Commands/PatSelectAllCommand.cs` | 新增命令（v2.2），含 `PATSELECTALL` + `BZS` |
| `Palette/PatPaletteCommand.cs` | 新增 `[CommandMethod("BIAOZHU")]` + `[CommandMethod("BZ")]` |
| `README.md` | 命令表新增拼音别名列 |
| `deploy/install-2007.ps1` | 安装完成提示显示别名 |

---

## 十三、v2 交互优化

> 用户实测后发现两个交互痛点：双击面板切换编号不生效；文字位置必须鼠标点击多一步。

### 13.1 热切换修复

**问题**：`ed.GetPoint()` 是阻塞调用。用户在选点期间双击面板设置新编号后，GetPoint 返回，但代码直接继续用旧编号完成标注，中间没有再检查 `PendingNumber`，导致下一次标注还是旧编号。

**修复**：提取 `ApplyPendingIfNeeded(Editor ed)` 方法，在**每个 GetPoint 返回后**都检查 `PatPaletteCommand.PendingNumber`：
1. 附着点返回后
2. 每次拐点返回后
3. 文字位置返回后（创建标注前最后一道保险）

切换时命令行显示 `>> 已切换为: 11`。

### 13.2 回车确定文字位置

**问题**：v2 原实现要求用户必须鼠标点击文字位置，不符合 AutoCAD 2007 原始标注"回车直接用最后拐点"的习惯。

**修复**：文字位置采集 `PromptPointOptions.AllowNone = true`：
- **回车/空格** → 直接用最后一个拐点作为文字位置
- **点击新点** → 使用点击的位置（保留原行为）
- **Esc** → 取消本次

提示语改为 `点击文字位置（回车=最后拐点）:`。

### 13.3 改动文件清单

| 文件 | 改动 |
|------|------|
| `Commands/PatMarkCommand.cs` | 新增 `ApplyPendingIfNeeded` 方法，4 处调用点；文字位置采集加 `AllowNone` |

### 13.4 标注流程（v2 最终版）

1. 点击附着点（起点）
2. 循环点击拐点（每点一个提示继续，回车/空格结束；至少 1 个）
3. 点击文字位置或直接回车（回车=最后拐点）
4. 生成样条曲线引线（默认无箭头）
5. 循环回到第 1 步

期间任何时刻双击面板条目，下一个 GetPoint 返回后立即切换编号。

---

## 十四、v2.1 样式控制增强

> 用户希望箭头大小可在面板调节，且样条/直线可切换（v2 默认样条，但有时需要直线）。

### 14.1 设计决策

- **箭头大小**：新增 `PatPaletteCommand.ArrowSize` 静态字段（默认 2.5），面板 NumericUpDown 调节。创建引线时同步到 PAT_DIM 样式的 `Dimasz`。
- **样条/直线开关**：新增 `PatPaletteCommand.IsSplined` 静态字段（默认 `true`），面板按钮切换。`leader.IsSplined` 取自该字段。
- **影响范围**：仅影响后续新建引线（与 v2 箭头开关一致）

### 14.2 面板布局调整

v2 原把箭头开关放在字高栏，v2.1 拆出独立"样式栏"：

```
样式栏: [箭头:关] [大小: 2.5] [线型:样条]
字高栏: [重置] [字高: 3.5] [字高:]
```

### 14.3 改动文件清单

| 文件 | 改动 |
|------|------|
| `Palette/PatPaletteCommand.cs` | 新增 `ArrowSize`、`IsSplined` 静态字段 |
| `Palette/DictPaletteControl.cs` | 新增样式栏（箭头开关移入 + 大小 NumericUpDown + 线型按钮）；新增 `NumArrowSize_ValueChanged`、`BtnSpline_Click`、`UpdateSplineButtonText` |
| `Commands/PatMarkCommand.cs` | `leader.IsSplined` 取自静态字段；创建引线时同步 `Dimasz` 到 PAT_DIM |

---

## 十五、v2.2 全选与字典对比

> 用户需求：1) 一键选中所有 PAT 文字统一修改样式；2) Word 中标记说明变化后，CAD 面板能直观展示新旧字典差异。

### 15.1 全选 PAT 文字

**新命令** `PATSELECTALL` / `BZS`：
- 遍历 ModelSpace，筛选所有 PAT Leader（`PatEntityHelper.IsPatEntity`）
- 选中 Leader 本身 + 关联的 MText（`leader.Annotation`）
- 用 `ed.SetImpliedSelection(ObjectId[])` 设置选择集
- 用户随后按 `Ctrl+1` 用 AutoCAD 原生属性面板统一修改

**面板入口**：按钮栏新增"全选"按钮，点击 `SendStringToExecute("PATSELECTALL\n")`。

### 15.2 字典对比

#### 15.2.1 数据流

```
Word VBA 重新导出 → 覆盖 <dwg>.dict.json
   ↓ (面板 2 秒轮询检测时间戳变化)
DictLoader 重载：旧缓存 _cachedModel → _previousModel，加载新版到 _cachedModel
   ↓
LoadDict 计算 DictDiff.Compute(prev, new) → 填充对照列 + 高亮
```

#### 15.2.2 双向匹配算法（DictDiff.cs）

1. **按 Number 匹配**：编号相同 → 比较名称
   - 名称也同 → `Unchanged`
   - 名称不同 → `NameChanged`（编号没变）
2. **剩余按 Name 匹配**：名称相同 → `NumberChanged`（编号变了）
3. **都匹配不上**：
   - 旧版独有 → `Removed`
   - 新版独有 → `Added`
   - 两者都变（无法匹配）→ `BothChanged`

#### 15.2.3 面板 UI

- **列表新增两列**："旧编号""旧名称"，默认宽度 0 隐藏
- **对照按钮**：按钮栏新增"对照"按钮，有对比基线时可用，点击切换显示/隐藏对照列
- **高亮颜色**：

  | 状态 | 颜色 | 含义 |
  |------|------|------|
  | Added | 浅绿 | 新增条目 |
  | Removed | 浅粉红 | 已删除条目 |
  | NumberChanged | 浅黄 | 编号变了 |
  | NameChanged | 浅蓝 | 名称变了 |
  | BothChanged | 珊瑚红 | 编号名称都变，无法自动匹配 |

- **状态栏**：检测到变化时显示 `字典已更新 — 新增 X，删除 Y，编号变 Z，名称变 W，无法匹配 V`

#### 15.2.4 设计要点

- **旧字典存内存**（`_previousModel`），关 CAD 重开后丢失（但 dict.json 本身还在，下次变化时重新建立基线）
- **触发时机**：文件时间戳变化即触发，无需手动操作
- **不自动修改图纸**：图纸中的引线文字不会被自动覆盖（专利图纸需可控性），只做可视化对比。用户判断后手动修改，或用 `PATSELECTALL` 选中后改

### 15.3 改动文件清单

| 文件 | 改动 |
|------|------|
| `IO/DictEntry.cs` (DictLoader) | 新增 `_previousModel` 字段、`PreviousModel` 属性、`ClearPrevious` 方法；重载时保留旧版 |
| `IO/DictDiff.cs` | **新建** — 双向匹配算法 + 差异状态枚举 + 概要统计 |
| `Commands/PatSelectAllCommand.cs` | **新建** — `PATSELECTALL` / `BZS` 命令 |
| `Palette/DictPaletteControl.cs` | 新增"全选""对照"按钮；列表新增对照列；`LoadDict` 重构支持对比模式填充与高亮；新增 `ApplyDiffHighlight`、`UpdateCompareColumns`、`BtnSelectAll_Click`、`BtnCompare_Click` |
| `README.md` | 命令表新增 `PATSELECTALL` / `BZS` |

### 15.4 关于 Word 变化后 CAD 反应的设计说明

**当前架构**：CAD 图纸只存"编号文字"（如 "10"），不存名称。名称只在面板展示。

| Word 中的变化 | CAD 面板 | CAD 图纸（已画引线） |
|--------------|---------|---------------------|
| 新增编号 "12" | 自动出现 "12" | 无影响，可双击标注 |
| 删除编号 "10" | "10" 消失 | 旧 "10" 引线还在（孤儿） |
| 改编号 "10"→"10a" | 显示 "10a"，高亮变化 | 旧 "10" 不会自动改 |
| 改名称 | 显示新名称 | 无影响（图纸本就没存名称） |

**设计理由**：图纸是设计交付物，不能被文档修改自动覆盖。通过对比面板让用户**看到**差异，由用户决定是否修改图纸。如需检查差异，用 `PATCHECK` / `BZC` 命令输出报告。

---

## 十六、版本演进总结

| 版本 | 主要内容 |
|------|---------|
| v1 | 基础降级版：Leader + MText，直线引线，固定 2 拐点，默认有箭头 |
| v2 | 样条曲线引线 + 无限拐点 + 默认无箭头 + 面板箭头开关；命令拼音别名；热切换修复；回车确定文字位置 |
| v2.1 | 箭头大小调节 + 样条/直线开关（面板样式栏） |
| v2.2 | 全选 PAT 文字命令 + 字典对比（双向匹配 + 对照列 + 高亮） |

---

## 十七、v2.3 面板布局优化与自适应

### 17.1 按钮自动换行（FlowLayoutPanel）

**问题**：面板按钮增多后（字高栏、样式栏、按钮栏共 10+ 控件），宽度不够时按钮被截断。

**改动**：三个栏从 `Panel` 改为 `FlowLayoutPanel`：
- `WrapContents = true` — 超宽自动换到下一行
- `FlowDirection = LeftToRight` — 从左到右排列
- 按钮改用 `AutoSize = true`（按文字内容自适应宽度）

### 17.2 系统色自适应

**问题**：面板硬编码颜色，与系统主题不一致。

**改动**：去掉 `Color.Gray`、`Color.DarkGreen` 等硬编码，改用 `SystemColors`：
- `SystemColors.GrayText` — 灰色文字
- `SystemColors.ControlText` — 主文字
- `SystemColors.Highlight` — 选中色

**局限**：AutoCAD 2007 的 PaletteSet 标题栏颜色由 CAD 控制（已一致），客户区只能靠 WinForms 系统色近似。差异高亮色（LightGreen/LightPink 等）保留为语义指示色。

### 17.3 修复 AutoSize 导致面板缩小

**问题**：上版把 FlowLayoutPanel 的 `AutoSize = true` 开了——这在 `Dock = Top` 容器上是反模式，导致每次选中条目时面板被拉缩。

**修复**：去掉 `AutoSize = true`，改回固定 `Height`（字高栏 30、样式栏 30、按钮栏 60）。保留 `WrapContents = true`。

---

## 十八、v2.4 部署脚本重构

### 18.1 部署方式结论

**基于 probe 报告的关键发现**：

| 能力 | 状态 | 对部署的影响 |
|------|------|-------------|
| PowerShell .ps1 执行 | **BLOCKED**（Restricted 策略） | 现有 install-2007.ps1 完全不可用 |
| cscript.exe (VBScript) | 存在 | VBS 是首选方案 |
| cmd.exe (BAT) | 存在 | BAT 作为 fallback |
| HKCU 注册表写入 | PASS | 注册表自动加载可行 |
| .NET 2.0/3.5 | PASS | DLL 运行环境满足 |
| WinForms 进程内构造 | PASS | 面板 UI 可用 |
| ANSI code page | 936 (GBK) | 脚本必须 GBK 编码 |

**结论**：PowerShell 不可用，改用 VBScript（首选）+ BAT（fallback）。

### 18.2 CAD 自动加载问题与最终方案

**问题**：HKCU 注册表写入验证通过，但 AutoCAD 2007 启动后命令不可用。

**根因**：ACAD 2007 可能只读 HKLM 的 `Applications` 键，不读 HKCU。HKLM 需要管理员权限，公司电脑受限。

**最终方案**：放弃全自动化，用 **APPLOAD + LSP**（用户认可）：
1. install-2007.vbs 生成 `load-patent-marker.lsp`（内容是 `(command "NETLOAD" "路径")`）
2. 用户在 AutoCAD 中用 APPLOAD 命令把 LSP 加入"启动套件"
3. 之后每次启动 AutoCAD 自动加载，一次设置永久生效

**install-2007.vbs 增强**：
- 记录系统信息（OS、用户名、计算机名）
- 记录 ACAD 安装路径（AcadLocation、ProductName）
- 列出 HKCU 下所有子键
- 尝试写 HKLM（记录成功或失败+错误码）
- 每步读回验证
- 生成 LSP 文件

### 18.3 VBA 安装到 Normal.dotm

**问题**：原 install-vba.vbs 装到 .docx 文档，`doc.Save` 假成功——.docx 不支持宏，保存时宏被静默剥离。

**修复**：改为装到 `Normal.dotm`（全局模板，.dotm 格式支持宏）：
- 通过 `wordApp.NormalTemplate.FullName` 获取路径
- 打开 Normal.dotm → 导入模块 → 保存
- 所有 Word 文档都能使用这些宏
- 前提：关闭所有 Word 窗口（避免 Normal.dotm 被占用只读）

### 18.4 脚本编码与 UX

**编码**：所有 VBS 和 README.txt 用 **GBK 编码**（Win7 中文系统 wscript/cscript 默认用 GBK 解码，UTF-8 会乱码）。

**UX 优化**：
- 所有消息累积到 `output` 字符串，最后用一次 `WScript.Echo` 弹出
- 中文提示（命令名保留英文）
- install-2007.vbs：双击 → 1 个弹框（完整结果）
- install-vba.vbs：双击 → 1 个弹框（完整结果）

### 18.5 最终包结构

```
PatentMarker-2007-v2\
├── PatentMarker.dll            CAD 插件 (61 KB)
├── install-2007.vbs            安装 DLL (中文, GBK, 带 log)
├── install-2007.bat            BAT fallback
├── install-vba.vbs             安装 VBA 到 Normal.dotm (中文, GBK)
├── uninstall-2007.vbs          卸载
├── README.txt                  部署说明 (中文, GBK)
└── vba\                        6 个 VBA 模块
    ├── Patterns.bas
    ├── DictModel.bas
    ├── JsonWriter.bas
    ├── JsonWriter.bas
    ├── PatentExtractor.bas
    ├── AutoExport.bas
    └── clsSaveHook.cls
```

**安装后生成**：`load-patent-marker.lsp`（脚本自动生成，用于 APPLOAD）

---

## 十九、最终命令清单

### CAD 命令（AutoCAD 2007）

| 命令 | 拼音别名 | 功能 |
|------|---------|------|
| `PATPALETTE` | `BIAOZHU` / `BZ` | 打开字典面板 |
| `PATMARK` | `BZM` | 创建引线标注（样条曲线，默认无箭头，无限拐点） |
| `PATCHECK` | `BZC` | 检查一致性 |
| `PATALIGN` | `BZA` | 对齐引线 |
| `PATSELECTALL` | `BZS` | 全选 PAT 标注实体 |

### Word VBA 宏（Normal 工程）

| 宏名 | 功能 |
|------|------|
| `ExtractDict` | 手动导出 dict.json |
| `EnableAutoExport` | 保存时自动导出 |
| `DisableAutoExport` | 关闭自动导出 |

### 面板功能

- 字典列表（编号/名称/次数，自然排序）
- 搜索栏
- 字高调节（NumericUpDown，1.0~20.0）
- 样式栏：箭头开关 / 箭头大小 / 线型（样条/直线）开关
- 按钮：重载 / 打开 / 冲突 / 删除引线 / 全选 / 对照
- 自动刷新（2 秒轮询 dict.json 时间戳）
- 字典对比（双向匹配 + 6 色高亮 + 对照列切换）

---

## 二十、项目状态（2007 部分）

**状态**：✅ 完成，已部署到目标环境验证可用

**最终交付包**：`PatentMarker-2007-v2-10.zip`

**部署方式**：
- CAD：APPLOAD + LSP（一次设置，永久自动加载）
- VBA：install-vba.vbs 装到 Normal.dotm（所有 Word 文档可用）

**已知限制**：
- 注册表 HKCU 自动加载在 ACAD 2007 上不生效（用 APPLOAD 替代）
- PaletteSet 客户区颜色只能靠 WinForms 系统色近似（不完全跟 CAD 主题）
- 字典对比只能展示差异，不会自动修改图纸（设计如此，保证图纸可控性）
