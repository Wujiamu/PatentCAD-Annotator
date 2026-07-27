# PatentCAD-Annotator 2007 — AutoCAD 2007~2009 / Win7 适配版

> **状态：已完成（v2）** — 含样条曲线、无箭头、箭头尺寸、全选、字典比对等增强功能。
>
> **兼容性：本版本仅适用于 AutoCAD 2007 / 2008 / 2009，不可用于其他版本的 AutoCAD。**

## 目标环境

| 项目 | 值 |
|------|-----|
| 操作系统 | Windows 7 SP1 (x86/x64) |
| AutoCAD | 2007, 2008, 2009 (R17.0—R17.2) |
| .NET | 2.0 (CLR 2.0) |
| Word | 2010（VBA 宏宿主） |
| 编译器 | VS2010+ (C# 3.0 语法，无 LINQ) |

## 与 2013+ 版的核心差异

| 特性 | 2013+ 版 | 2007 版 |
|------|----------|---------|
| 引线类型 | MLeader（一体式） | Leader + MText（组合） |
| 样式管理 | MLeaderStyle (PAT_STYLE) | DimStyle (PAT_DIM) |
| JSON | Newtonsoft.Json | 内置 SimpleJson（~200 行，零依赖） |
| LINQ | 可用 | 不可用（手动循环 + List.Sort） |
| 部署 | ApplicationPlugins bundle | HKCU 注册表 |
| accoremgd | 有 | 无（2007 无此程序集） |

## v2 增强功能

| 功能 | 说明 |
|------|------|
| 样条曲线引线 | `Leader.IsSplined = true`，面板可一键切换直线/样条 |
| 默认无箭头 | `Leader.HasArrowHead = false`，面板 'Arrow: Off/On' 切换 |
| 箭头尺寸可调 | NumericUpDown 控件（0.5—20.0，步进 0.5，默认 2.5），同步 PAT_DIM 的 Dimasz |
| 无限拐点 | 循环采集点击直到 Enter/Space 结束 |
| 全选标注 | `PATSELECTALL` / `BZS` 命令，一键选中全部 PAT 引线及文字 |
| 字典比对 | `.dict.json` 变更时高亮：绿(新增)/红(删除)/黄(编号变)/蓝(名称变)/珊瑚(两者变) |
| 自动同步 | 面板 2 秒 Timer 检测 `.dict.json` 变更，自动刷新；'Reload' 按钮强制重载 |
| 面板自适应 | FlowLayoutPanel 自动换行，跟随 Win7 系统主题色 |

## 构建

1. 用 VS2010+ 打开 [PatentMarker/PatentMarker.csproj](PatentMarker/PatentMarker.csproj)
2. 将本机 AutoCAD 2007 安装目录下的 `acdbmgd.dll` / `acmgd.dll` 复制到 [PatentMarker/lib/](PatentMarker/lib/)（详见 [lib/README.txt](PatentMarker/lib/README.txt)）
3. 目标框架选 .NET Framework 2.0
4. Platform Target: x86
5. 编译输出：`PatentMarker.dll`（单文件，零外部依赖）

## 部署

```powershell
# 1. 拷贝 PatentMarker-2007-deploy/ 目录到非 C 盘固定位置
mkdir D:\PatentMarker
copy PatentMarker-2007-deploy\* D:\PatentMarker\

# 2. 运行安装脚本（写入 HKCU 注册表）
cd D:\PatentMarker
.\install-2007.vbs          # CAD 插件
.\install-vba.vbs           # Word VBA 模块

# 3. 重启 AutoCAD 2007
```

若 HKCU 自动加载不生效（ACAD 2007 已知问题），用 APPLOAD 加载 `load-patent-marker.lsp` 或手动 NETLOAD。

详见 [PatentMarker-2007-deploy/README.txt](../../PatentMarker-2007-deploy/README.txt)。

## 可用命令

| 命令 | 拼音别名 | 说明 |
|------|----------|------|
| `PATPALETTE` | `BIAOZHU` / `BZ` | 打开字典面板（可停靠侧栏） |
| `PATMARK` | `BZM` | 创建引线标注（Leader + MText） |
| `PATCHECK` | `BZC` | 校验图纸编号与字典的一致性 |
| `PATALIGN` | `BZA` | 对齐引线（选择模式 / 框边模式） |
| `PATSELECTALL` | `BZS` | 选中所有 PAT 引线及文字（配合 Ctrl+1 改属性） |

## 字典文件

将 `<dwg文件名>.dict.json` 放在 DWG 同目录。由 `PatentMarker-2007-deploy/vba/` 下的 VBA 宏从 Word 说明书提取生成。

## 配置

可选：在 DLL 旁放 `config.json`：

```json
{
    "defaultDictPath": "",
    "patStyle": { "textHeight": 3.5 },
    "align": { "marginToFrame": 5.0 }
}
```

## 日志

DLL 旁自动生成 `PatentMarker.log`。

## 已知限制

- Leader + MText 是分离实体，移动时需同时选中两者（用 `BZS` 全选）
- PaletteSet 样式标志少于高版本（无 NameEditable / ShowPropertiesMenu）
- 无 LINQ，列表过滤/排序性能略低（对专利标注场景无影响）
- ACAD 2007 可能不读 HKCU 自动加载，需 APPLOAD 或 NETLOAD 兜底
