// v4.0 平移辅助：把 2025 版文件降级为 C# 7.3 兼容语法，写入 2015 版目录
// 降级规则：null! → null；object? → object；string?/DictModel?/DictEntry? → 去 ?；??= 展开
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname);
const args = process.argv.slice(2);
const targetVersion = args.find(a => !a.startsWith('--')) || '2015';
const writeEnabled = args.includes('--write');
if (targetVersion !== '2015') {
    throw new Error('Only the tested 2025-to-2015 conversion is supported by this script.');
}
const src = path.join(root, 'cad-plugin', '2025', 'PatentMarker');
const dst = path.join(root, 'cad-plugin', targetVersion, 'PatentMarker');

function writeOutput(relativePath, content) {
    const target = path.join(dst, relativePath);
    if (!writeEnabled) {
        console.log('DRY-RUN', target);
        return;
    }
    if (fs.existsSync(target)) {
        const stamp = new Date().toISOString().replace(/[:.]/g, '-');
        const backup = target + '.bak.' + stamp;
        fs.copyFileSync(target, backup);
        console.log('BACKUP', backup);
    }
    fs.writeFileSync(target, content, 'utf8');
    console.log('DONE', target);
}

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
    'IO/NumberIdentity.cs',
    'IO/PatSettings.cs',
];
for (const f of files) {
    const text = fs.readFileSync(path.join(src, f), 'utf8');
    const out = downLevel(text);
    writeOutput(f, out);
}
// 直接复制：MarkingTextParser.cs（纯 .NET 4.x 兼容）
const mp = fs.readFileSync(path.join(src, 'IO/MarkingTextParser.cs'), 'utf8');
writeOutput('IO/MarkingTextParser.cs', mp);

// 校验：检查降级后文件是否仍残留 C# 8+ 语法
const check = ['IO/DictConflict.cs','Palette/PasteRecognizeDialog.cs','Palette/EditEntryDialog.cs','Palette/ArbitrateDialog.cs','Palette/DictPaletteControl.cs','IO/MarkingTextParser.cs','IO/NumberIdentity.cs','IO/PatSettings.cs'];
let issues = 0;
for (const f of check) {
    if (!writeEnabled) continue;
    const lines = fs.readFileSync(path.join(dst, f), 'utf8').split('\n');
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
if (!writeEnabled) {
    console.log('No files changed. Pass --write to apply the conversion; existing files are backed up first.');
}
