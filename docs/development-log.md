# 变更记录

本项目遵循语义化版本管理。

---

## v3.0 (2026-08-03)

- VBA v3.0：多格式附图标记识别（括号/连字符/英文标点/裸列表）
- VBA 支持新格式（名称+编号）提取 + 自动导出检测 DWG 命名 + `<br/>` 标签处理
- C# 面板取消 JSON 排序，按原文顺序显示（用户自定义顺序优先）
- 部署包 VBA 全部同步 v3.0（含 2007-v2 / 2007-deploy 的 JsonWriter / PatentExtractor 统一）
- 全部 5 个版本重新编译并更新部署包 DLL（含取消排序改动）

工程基础设施（不改变插件运行时行为）：

- 新增 `AGENTS.md` 代理工作指南：版本矩阵、目录约定、MLeader API 陷阱对照表、跨版本同步规则、部署包逐版本差异
- 新增 `build.ps1` 构建与环境检查脚本：SDK DLL 齐全性检查、2025 版 `dotnet build` 编译、`-Check` doctor 模式、`-Structure` 结构检查
- 新增 `.github/workflows/build.yml`：push/PR 时执行结构完整性检查；因 SDK DLL 版权不入库，真编译需本地执行 `build.ps1`
- 新增 `PatentCAD.sln` 解决方案文件（含 2025 版，可直接 `dotnet build`）

## v2.5 (2026-07-27)

- 修复 Word 2010 无法导入 `clsSaveHook.cls` 的兼容性问题（改为代码注入方式）
- 修复 2007/2010 版箭头大小修改后不能立即生效的问题
- 所有部署包补充 `install-vba.vbs` 脚本

## v2.4 (2026-07-26)

- 完成多版本适配：2010 / 2013 / 2015 / 2025，全部通过编译验证
- 修复 MLeader API 名称适配问题（`TextLocation`、`AddLastVertex` 等）
- 修复 ArrowSize / TextHeight 实例同步问题
- 统一部署包结构：根目录 `PatentMarker-{version}-deploy/`

## v2.0 (2026-07)

- 2007 版完成：样条曲线引线 + 无限拐点 + 默认无箭头
- 面板控制：字高调节、箭头开关、箭头大小、线型切换
- 字典自动刷新（2 秒轮询 `.dict.json` 时间戳）
- 字典对比功能（双向匹配 + 6 色高亮 + 对照列切换）
- 命令拼音别名（`BZ` / `BZM` / `BZC` / `BZA` / `BZS`）
- 全选 PAT 标注实体命令（`PATSELECTALL` / `BZS`）
- VBA 安装脚本：自动导入模块到 Word Normal 模板
- 部署方式：VBScript 注册表 + APPLOAD/LSP 兜底

## v1.0 (2026-06)

- 初始版本：Leader + MText 组合标注方案
- 基础面板：字典列表、搜索、字高调节
- Word VBA 提取器：从说明书提取附图标记字典
