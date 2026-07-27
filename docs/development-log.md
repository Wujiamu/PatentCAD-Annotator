# 变更记录

本项目遵循语义化版本管理。

---

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
