# PatentMarker 2007 — AutoCAD 2007 / Win7 适配版

## 目标环境

| 项目 | 值 |
|------|-----|
| 操作系统 | Windows 7 (x86/x64) |
| AutoCAD | 2007 (R17.0) |
| .NET | 2.0 (CLR 2.0) |
| 编译器 | VS2010+ (C# 3.0 语法，无 LINQ) |

## 与 2014/2026 版的核心差异

| 特性 | 2026/2014 版 | 2007 版 |
|------|-------------|---------|
| 引线类型 | MLeader | Leader + MText 组合 |
| 样式管理 | MLeaderStyle (PAT_STYLE) | DimStyle (PAT_DIM) |
| JSON | Newtonsoft.Json 13.x | 内置 SimpleJson (~200 行) |
| LINQ | ✓ | ✗（手动循环 + List.Sort） |
| 部署 | ApplicationPlugins bundle | HKCU 注册表 |
| accoremgd | ✓ | ✗（2007 无此程序集） |

## 构建

1. 用 VS2010+ 打开 `PatentMarker\PatentMarker.csproj`
2. 修改 csproj 中 acdbmgd.dll / acmgd.dll 的 HintPath 为本机 AutoCAD 2007 安装路径
3. 目标框架选 .NET Framework 2.0
4. Platform Target: x86（或与 AutoCAD 2007 位数匹配）
5. 编译输出：`PatentMarker.dll`（单文件，零外部依赖）

## 部署

```powershell
# 1. 拷贝 DLL 到固定目录
mkdir C:\PatentMarker
copy bin\Release\PatentMarker.dll C:\PatentMarker\

# 2. 运行部署脚本（写入 HKCU 注册表）
.\deploy\install-2007.ps1

# 3. 重启 AutoCAD 2007
```

## 可用命令

| 命令 | 拼音别名 | 说明 |
|------|----------|------|
| `PATPALETTE` | `BIAOZHU` / `BZ` | 打开字典面板（可停靠侧栏） |
| `PATMARK` | `BZM` | 创建引线标注（Leader + MText） |
| `PATCHECK` | `BZC` | 校验图纸编号与字典的一致性 |
| `PATALIGN` | `BZA` | 对齐引线（选择模式 / 框边模式） |
| `PATSELECTALL` | `BZS` | 选中所有 PAT 引线及文字（配合 Ctrl+1 改属性） |

## 字典文件

将 `<dwg文件名>.dict.json` 放在 DWG 同目录。由 `extractor/` VBA 宏从 Word 说明书提取生成。

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

- Leader + MText 是分离实体，移动时需同时选中两者
- PaletteSet 样式标志少于高版本（无 NameEditable / ShowPropertiesMenu）
- 无 LINQ，列表过滤/排序性能略低（对专利标注场景无影响）
