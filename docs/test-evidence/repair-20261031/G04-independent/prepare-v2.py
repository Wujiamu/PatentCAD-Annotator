from pathlib import Path
import shutil

root = Path(__file__).resolve().parent
original = (root / 'IndependentEdgeProbe.cs').read_text(encoding='utf-8')
control = '''    private static void LegalUtf8Controls()
    {
        string expected = "English 中文 😀";
        byte[] body = Encoding.UTF8.GetBytes("{\\\"entries\\\":[{\\\"number\\\":\\\"A1\\\",\\\"name\\\":\\\"" + expected + "\\\"}]}");
        foreach (bool bom in new[] { false, true })
        {
            string path = Path.Combine(directory, bom ? "valid-bom.json" : "valid-no-bom.json");
            File.WriteAllBytes(path, bom ? new byte[] { 0xef, 0xbb, 0xbf }.Concat(body).ToArray() : body);
            DictModel model = DictLoader.Load(path);
            Check(model != null && model.Entries.Count == 1 && model.Entries[0].Number == "A1" && model.Entries[0].Name == expected,
                  "legal UTF-8 " + (bom ? "BOM" : "no-BOM") + " English Chinese emoji exact value");
        }
    }

'''
needle = '    private static void InvalidUtf8AndBackupRecovery()'
assert original.count(needle) == 1
original = original.replace(needle, control + needle).replace('        InvalidUtf8AndBackupRecovery();', '        LegalUtf8Controls();\n        InvalidUtf8AndBackupRecovery();')
(root / 'IndependentEdgeProbe-v2.cs').write_text(original, encoding='utf-8')

fixed = Path(r'<USERPROFILE>\AppData\Local\Temp\PatentCAD-repair-w5hkb8c3\G04-integrated')
target = root / 'candidate-fixed'
target.mkdir()
shutil.copytree(fixed / 'cad-plugin', target / 'cad-plugin', ignore=shutil.ignore_patterns('bin', 'obj'))
shutil.copytree(fixed / 'tools/G04JsonRegression', target / 'tools/G04JsonRegression', ignore=shutil.ignore_patterns('bin', 'obj'))

runner = (root / 'run-independent-edges.py').read_text(encoding='utf-8')
replacements = {
    'root / "edge-probe"': 'root / "edge-probe-v2"',
    'root / "IndependentEdgeProbe.cs"': 'root / "IndependentEdgeProbe-v2.cs"',
    'cases = [("baseline-run", "2007")] + [("candidate", v) for v in ("2007", "2010", "2013", "2015", "2025")]': 'cases = [(snapshot, v) for snapshot in ("candidate", "candidate-fixed") for v in ("2007", "2010", "2013", "2015", "2025")]',
    'if snapshot == "candidate"': 'if snapshot in ("candidate", "candidate-fixed")',
    '"edge-" + tag + "-build.log"': '"edge-v2-" + tag + "-build.log"',
    '"edge-" + tag + ".log"': '"edge-v2-" + tag + ".log"',
    '"edge-data-" + tag': '"edge-v2-data-" + tag',
    '"independent-edge-runs.json"': '"independent-edge-runs-v2.json"',
}
for old, new in replacements.items():
    assert runner.count(old) == 1, old
    runner = runner.replace(old, new)
(root / 'run-independent-edges-v2.py').write_text(runner, encoding='utf-8')
