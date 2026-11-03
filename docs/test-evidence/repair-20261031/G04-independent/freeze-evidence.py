from pathlib import Path
import datetime, hashlib, json, os, platform, re, sys

root = Path(__file__).resolve().parent
live = root.parent / 'G04-integrated'
def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for b in iter(lambda: f.read(1024 * 1024), b''): h.update(b)
    return h.hexdigest()
def inventory(snapshot):
    found = {}
    for rel in ('cad-plugin', 'tools/G04JsonRegression'):
        base = snapshot / rel
        for folder, dirs, files in os.walk(base):
            dirs[:] = [d for d in dirs if d.lower() not in ('bin','obj','.vs','testresults')]
            for name in files:
                p = Path(folder) / name
                found[p.relative_to(snapshot).as_posix()] = digest(p)
    return dict(sorted(found.items()))
def dump(name, obj):
    (root / name).write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

snapshots = {name:inventory(path) for name,path in [('original_candidate',root/'candidate'),('fixed_candidate',root/'candidate-fixed'),('integrated_candidate',live)]}
same = snapshots['fixed_candidate'] == snapshots['integrated_candidate']
allnames = sorted(set(snapshots['original_candidate']) | set(snapshots['fixed_candidate']))
changes = [{'path':name,'old':snapshots['original_candidate'].get(name),'fixed':snapshots['fixed_candidate'].get(name)} for name in allnames if snapshots['original_candidate'].get(name) != snapshots['fixed_candidate'].get(name)]
dump('source-inventory.json',{'algorithm':'SHA-256','snapshots':snapshots})
dump('source-diff.json',{'fixed_snapshot_matches_live_integrated':same,'changes':changes})
if not same: raise SystemExit('Frozen candidate no longer matches integrated candidate')

toolpaths = [Path(sys.executable), Path(r'C:\Program Files\dotnet\dotnet.exe'),Path(r'C:\Program Files\dotnet\sdk\8.0.422\Roslyn\bincore\csc.dll')]
for tfm in ('net35','net40','net45'):
    toolpaths.append(Path(os.environ['USERPROFILE']) / ('.nuget/packages/newtonsoft.json/13.0.3/lib/' + tfm + '/Newtonsoft.Json.dll'))
for tfm in ('net40','net45'):
    base=Path(os.environ['USERPROFILE']) / ('.nuget/packages/microsoft.netframework.referenceassemblies.'+tfm)
    toolpaths.extend(base.glob('*/build/.NETFramework/v*/mscorlib.dll'))
toolpaths.extend([root/'IndependentEdgeProbe-v2.cs',root/'run-independent-edges-v2.py',root/'framework-legacy-probe/Program.cs',root/'framework-legacy-probe/FrameworkLegacyProbe.csproj',root/'framework-probe/Program.cs',root/'framework-probe/FrameworkProbe.csproj'])
dump('tool-hashes.json',{'tools':[{'path':str(p),'sha256':digest(p)} for p in toolpaths if p.is_file()]})

def results(path): return json.loads((root/path).read_text(encoding='utf-8'))
edge = results('independent-edge-runs-v2.json')
legacy = results('framework-legacy-runs.json')
framework = results('framework-independent-runs.json')
author = results('author-probes-fixed-runs.json')
assert len(edge)==10 and all(e['build_exit']==0 and e['exit']==(1 if e['snapshot']=='candidate' else 0) for e in edge)
assert len(legacy)==4 and all(e['build_exit']==0 and e['exit']==(1 if e['snapshot']=='candidate' else 0) for e in legacy)
assert len(framework)==4 and all(e['build_exit']==0 and e['exit']==(1 if e['snapshot']=='baseline-run' else 0) for e in framework)
assert len(author)==5 and all(e['build_exit']==0 and e['exit']==0 for e in author)
for year in ('2007','2010','2013','2015','2025'):
    red=(root/('edge-v2-candidate-'+year+'.log')).read_text(encoding='utf-8')
    green=(root/('edge-v2-candidate-fixed-'+year+'.log')).read_text(encoding='utf-8')
    assert red.count('FAIL ')==4 and 'SUMMARY failures=4' in red
    assert green.count('FAIL ')==0 and 'SUMMARY failures=0' in green
    assert all(x in green for x in ('PASS legal UTF-8 no-BOM', 'PASS legal UTF-8 BOM', 'PASS current bytes and attributes preserved', 'PASS backup bytes and attributes preserved'))
    assert 'failures=0' in (root/('candidate-fixed-'+year+'-probe.log')).read_text(encoding='utf-8')

findings = {
 'schema':1,
 'reviewer':'independent agent /root/g01 (did not implement G04); all executions in isolated temporary copies',
 'time_local':datetime.datetime.now().astimezone().isoformat(),
 'overall':'PASS at isolated production-source/function and .NET Framework layer; actual host and deployment are SKIP/BLOCKED',
 'snapshots':{'old_original_candidate':str(root/'candidate'),'fixed_source_snapshot':str(root/'candidate-fixed'),'integrated_candidate':str(live),'fixed_snapshot_matches_integrated':same,'baseline_run_note':'Original baseline production IO for five versions injected into isolated candidate-derived tree; see source inventory and author-probes-runs.json.'},
 'checks':[
  {'id':'R002_invalid_utf8_and_backup','before':'FAIL: five loaders accept byte FF as replacement character, recovery overwrites current and deletes backup','after':'PASS: five loaders reject, return error, preserve bytes and exact attributes','versions':['2007','2010','2013','2015','2025'],'selector':'IndependentEdgeProbe-v2: InvalidUtf8AndBackupRecovery','red_exit':1,'green_exit':0,'logs':'edge-v2-candidate[-fixed]-<version>.log','current_sha256':'d3440e9b69609a924e429b959fbf1daffb975293fb050b0efca8817b4ec568b7','backup_sha256':'db6b201e5b9107e5689ae11bfcf715db775144f5acee2127118dbde2a84cecef','current_attributes':32,'backup_attributes':38},
  {'id':'R002_valid_utf8','result':'PASS','selector':'IndependentEdgeProbe-v2: LegalUtf8Controls','details':'UTF-8 BOM and no BOM, exact English Chinese emoji values, five versions'},
  {'id':'R002_adjacent_json','result':'PASS','selector':'IndependentEdgeProbe-v2: AdditionalGrammar','details':'12 extra invalid grammar cases rejected; nested unknown field accepted in all five'},
  {'id':'R002_R019_author_source_harness','result':'PASS','selector':'all G04JsonRegression selectors; R002_Syntax, R002_Root, R002_Compat, R002_Restore, R002_Utf8, R002_Cache, R019_Parse, R019_Write, R019_CRLF; R002_Config additionally on 2007/2010','details':'five versions built actual production files with host stubs; 2007/2010 parser is Shared/IO/SimpleJson.cs'},
  {'id':'malformed_utf8_author_probe','result':'PASS','selector':'R002_Utf8','details':'FF, incomplete C3 and overlong C0 80, each with and without UTF-8 BOM; rejected and current/backup byte and attribute preservation independently rerun in five versions'},
  {'id':'framework_legacy','before':'FAIL','after':'PASS','selector':'framework-legacy-probe same UTF8, restore and grammar checks','details':'2007 and 2010 production sources compiled net40 and executed on .NET Framework 4.x; not actual .NET 2/3.5 CLR or CAD'},
  {'id':'framework_newtonsoft','before':'FAIL','after':'PASS','selector':'R002_Framework_SyntaxRoot[0..8], R002_Framework_Compat[0..4], R002_Framework_ValidatorAcceptsHugeNumber, R002_Framework_ExistingNewtonsoftHugeLimit','details':'2013 target net40, 2015 net45; Newtonsoft on actual .NET Framework process'},
  {'id':'vstest_2025','result':'PASS','total':142,'passed':142,'failed':0,'command':"dotnet test .\\cad-plugin\\2025\\PatentMarker.Tests\\PatentMarker.Tests.csproj --configuration Release --nologo --logger 'console;verbosity=normal'",'exit':0,'log':'vstest-2025-fixed.log'},
  {'id':'real_hosts','result':'SKIP/BLOCKED','details':'No CAD host, installation or deployment was launched; four old CAD versions require GitHub external host validation; 2025 real CAD not validated by these isolated tests.'},
  {'id':'huge_unknown_number','result':'SKIP/BLOCKED','details':'For 2013/2015 Newtonsoft on .NET Framework, an unknown numeric field such as 1e400 remains rejected by both baseline and fixed candidate; ordinary unknown fields pass.'}
 ],
 'G05_contract':'Invalid syntax, non-object root and tested malformed UTF-8 return null from DictLoader.Load; normal old optional and unknown fields load. ResolveRestoreCad rejects tested corrupt backups with an error and preserves current and backup bytes/attributes. Legacy writers serialize valid JSON with CRLF structure, UTF-8 no BOM and exact CAD marker; legacy string contents normalize lone CR and CRLF to LF, so they do not promise byte-for-byte string roundtrip. The 2013/2015 Framework Newtonsoft extreme-number limitation remains.'}
dump('independent-findings.json',findings)

lines = [
 '# G04 独立验收', '',
 '结论：生产源码隔离回归 PASS；实际 CAD 宿主和部署链路 SKIP/BLOCKED。本验收者 /root/g01 未参与 G04 实现。', '',
 '旧候选五版对含 FF 的 UTF-8 损坏备份均错误接受，覆盖当前文件并删除备份，4 项断言 FAIL、退出 1；固定后的五版均拒绝读取，返回错误，保留当前/备份全部字节与属性，退出 0。当前文件 SHA-256 d3440e9b69609a924e429b959fbf1daffb975293fb050b0efca8817b4ec568b7，属性 32 (Archive)；备份 SHA-256 db6b201e5b9107e5689ae11bfcf715db775144f5acee2127118dbde2a84cecef，属性 38 (Hidden|System|Archive)。两文件修复后前后完全相等。', '',
 '同一 IndependentEdgeProbe-v2 探针逐版重新编译旧候选和修复候选，验证 12 项相邻语法边界、嵌套未知字段、BOM/无 BOM 的英文中文和 emoji、坏备份恢复及文件保护。命令、精确参数和退出码见 independent-edge-runs-v2.json，详细输出见 edge-v2-*.log。', '',
 '固定候选五版 G04JsonRegression 的全部 selector 均通过（原始候选/基线已留下红绿输出）；旧版生产 SimpleJson 和加载器还通过真正 .NET Framework 4.0 进程的 2007/2010 红绿测试，2013 net40 与 2015 net45 的 Newtonsoft 语法兼容探针通过。2025 VSTest 完整 142/142。程序没有调用 G01 FactRunner；所有 CAD 类型均为测试桩。', '',
 'Framework 层限制：2007/2010 测试运行于 .NET Framework 4.x，并不宣称旧 CLR 2.0/3.5 或真实 CAD 成功。2013/2015 在 Framework 上的 Newtonsoft 对 1e400 这类极大未知数原本就拒绝，此行为固定候选保持不变；普通未知字段正常。四组旧版 CAD 的用户环境需通过 GitHub 外部宿主验收；本机没有探测或安装旧版 CAD，2025 真实宿主及部署也未执行。', '',
 'G05 可用契约：已测坏 JSON、非对象根和错误 UTF-8 读取返回 null；合法旧可选字段与普通未知字段仍可读取；已测损坏备份恢复返回错误，当前和备份字节及属性保持不变。2007/2010 写出合法 JSON、结构行尾 CRLF、UTF-8 无 BOM 并保留精确 CAD 标记；字符串内容中的单独 CR 和 CRLF 会规范化为 LF，不能作为原样往返契约。R002_Utf8 还额外复跑 FF、不完整 C3、过长 C0 80，各含/不含 BOM 六种损坏样本。', '',
 'source-inventory.json 是两份候选及实际集成候选的 SHA-256 清单，source-diff.json 列出修复差异，tool-hashes.json 为工具和探针；frozen candidate 与集成候选完整源码树经逐文件哈希匹配。运行命令、选择器、退出码、状态矩阵见 independent-findings.json 和对应 runs.json；evidence-sha256.json 固化本目录的重要日志、探针源码、测试程序集和样本。', ''
]
(root/'independent-acceptance.md').write_text('\n'.join(lines),encoding='utf-8')

evidence=[]
for p in root.iterdir():
    if p.is_file() and p.name != 'evidence-sha256.json': evidence.append(p)
for pattern in ('edge-v2-data-*/*','framework-legacy-data-*/*','edge-probe-v2/bin/*/Release/net8.0/G04JsonRegression.dll','framework-legacy-probe/bin/*/Release/net40/FrameworkLegacyProbe.exe','framework-probe/bin/*/Release/net*/FrameworkProbe.exe','candidate-fixed/tools/G04JsonRegression/bin/*/Release/net8.0/G04JsonRegression.dll','candidate-fixed/cad-plugin/2025/PatentMarker.Tests/bin/Release/**/PatentMarker.Tests.dll'):
    evidence.extend(p for p in root.glob(pattern) if p.is_file())
for directory in (root/'edge-probe-v2',root/'framework-legacy-probe',root/'framework-probe'):
    evidence.extend(p for p in directory.iterdir() if p.is_file() and p.suffix in ('.cs','.csproj'))
unique=sorted(set(evidence),key=lambda p:str(p).lower())
dump('evidence-sha256.json',{'algorithm':'SHA-256','files':[{'path':str(p.relative_to(root)),'sha256':digest(p),'bytes':p.stat().st_size} for p in unique]})
print('source_count',len(snapshots['fixed_candidate']),'fixed_matches_integrated',same,'changed',len(changes),'evidence_count',len(unique))
for x in changes: print(x['path'],x['old'],x['fixed'])
for name in ('independent-findings.json','independent-acceptance.md','source-inventory.json','source-diff.json','tool-hashes.json','evidence-sha256.json'): print(name,digest(root/name))
