// v4.0 平移辅助：把 2025 版文件降级为 C# 7.3 兼容语法，写入 2015 版目录
// 降级规则：null! → null；object? → object；string?/DictModel?/DictEntry? → 去 ?；??= 展开
const fs = require('fs');
const path = require('path');
const src = 'c:/Users/wjm/WorkBuddy/2026-06-20-00-50-28/cad-plugin/2025/PatentMarker/';
const dst = 'c:/Users/wjm/WorkBuddy/2026-06-20-00-50-28/cad-plugin/2015/PatentMarker/';

function downLevel(srcText) {
    let s = srcText;
    // 1. null! → null
    s = s.replace(/ = null!;/g, ' = null;');
    // 2. object? sender → object sender
    s = s.replace(/\bobject\? sender/g, 'object sender');
    // 3. 类型标注去 ?（小写 string / 大写类型名）
    s = s.replace(/\bstring\?(\s|[,;)])/g, 'string$1');
    s = s.replace(/\b([A-Z][A-Za-z0-9_<>.,\[\]]*)\?(\s|[,;)])/g, '$1$2');
    // 4. ??= 展开（仅 DictConflict.cs 中 restored.Metadata ??= new DictMetadata();）
    s = s.replace(/(\w[\w.]*) \?\?= (\w+)\.(\w+)\(\);/g, 'if ($1 == null) $1 = new $2();');
    // 兜底：仍残留的 ??= 形式（a ??= b;）
    s = s.replace(/([A-Za-z_][\w.]*) \?\?= ([^;]+);/g, 'if ($1 == null) $1 = $2;');
    return s;
}

const files = [
    'IO/DictConflict.cs',
    'Palette/PasteRecognizeDialog.cs',
    'Palette/EditEntryDialog.cs',
    'Palette/ArbitrateDialog.cs',
    'Palette/DictPaletteControl.cs',
];
for (const f of files) {
    const text = fs.readFileSync(src + f, 'utf8');
    const out = downLevel(text);
    fs.writeFileSync(dst + f, out, 'utf8');
    console.log('DONE', f);
}
// 直接复制：MarkingTextParser.cs（纯 .NET 4.x 兼容）
const mp = fs.readFileSync(src + 'IO/MarkingTextParser.cs', 'utf8');
fs.writeFileSync(dst + 'IO/MarkingTextParser.cs', mp, 'utf8');
console.log('DONE IO/MarkingTextParser.cs');

// 校验：检查降级后文件是否仍残留 C# 8+ 语法
const check = ['IO/DictConflict.cs','Palette/PasteRecognizeDialog.cs','Palette/EditEntryDialog.cs','Palette/ArbitrateDialog.cs','Palette/DictPaletteControl.cs','IO/MarkingTextParser.cs'];
let issues = 0;
for (const f of check) {
    const lines = fs.readFileSync(dst + f, 'utf8').split('\n');
    for (let i = 0; i < lines.length; i++) {
        const L = lines[i];
        if (/null!/.test(L) || /\?\?=/.test(L) || /is not /.test(L) || /new\(\)/.test(L) ||
            /\bobject\? /.test(L) || /\bstring\? /.test(L) || /\bDictModel\? /.test(L) || /\bDictEntry\? /.test(L) ||
            /\bMarkingHit\? /.test(L)) {
            console.log('REMAIN', f + ':' + (i + 1) + ': ' + L.trim());
            issues++;
        }
    }
}
console.log('remaining issues:', issues);
