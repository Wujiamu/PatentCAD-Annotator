# Word 字典文件契约

PatentCAD-Annotator 的 Word 导出器负责生成 `.dict.json`；本仓库的 PowerPoint / Visio 加载项只读取该文件，不通过 COM 调用 CAD，也不回写字典。Word、CAD、PPT、Visio 在同一主仓库维护，以下字段和比较规则是各宿主之间的文件接口。C# 消费端共用 `cad-plugin/Shared/IO/NumberIdentity.cs`。

## 当前结构

```json
{
  "metadata": {
    "source_file": "drawing.dwg",
    "extracted_at": "2026-10-09T12:00:00",
    "version": "1.0"
  },
  "entries": [
    { "number": "1", "name": "底座", "occurrences": 1 }
  ],
  "warnings": []
}
```

以上 JSON 仅展示字段结构，示例值不代表实际导出时间或文档。

- Word 以 UTF-8 无 BOM 写出 JSON。`number` 和 `name` 是字符串；编号不得转成整数、重排或改写成另一种显示形式。
- 加载项要求 `entries` 中每条记录有非空 `number`；`name` 为空时按空文本显示。旧字典可缺少可选字段，解析器忽略未知字段。
- 字典项顺序和编号原文由 Word 保留。加载项把扫描到的标注编号与字典编号按去首尾空格、忽略大小写规则比较；界面仍显示字典原文。
- 绑定路径和标注身份保存在 PPTX / VSDX 内，由宿主文档保存；它们不是 `.dict.json` 的扩展字段。

## 变更约定

改变字段、编码、编号比较或缺省处理时，应在同一变更中更新本契约和受影响的 Word / CAD / Office 实现，并安排跨端回归。不能让 Office 在解析失败时把字典当作空列表，也不能静默修改原 JSON。