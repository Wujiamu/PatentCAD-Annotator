from pathlib import Path
import hashlib
import json
import shutil

root = Path(__file__).resolve().parent
for source, name in (("baseline-run", "command-baseline"), ("candidate", "command-candidate")):
    destination = root / name
    destination.mkdir()
    shutil.copytree(root / source / "cad-plugin", destination / "cad-plugin",
                    ignore=shutil.ignore_patterns("bin", "obj"))
    shared = destination / "cad-plugin/RuntimeContract.Tests/Shared"
    for file in ("IndependentPromptStubs.cs", "G13IndependentCommandTests.cs"):
        shutil.copy2(root / file, shared / file)
    stub = shared / "AutodeskSimulationStubs.cs"
    data = stub.read_bytes()
    before = b"public sealed class Editor"
    if data.count(before) != 1:
        raise RuntimeError("Editor anchor missing or not unique")
    stub.write_bytes(data.replace(before, b"public sealed partial class Editor"))
    project = destination / "cad-plugin/RuntimeContract.Tests/PatentMarker.RuntimeContract.2007.Tests.csproj"
    data = project.read_bytes()
    before = b'<Compile Include="Shared\\AutodeskSimulationStubs.cs" />'
    if data.count(before) != 1:
        raise RuntimeError("project anchor missing or not unique")
    added = (before + b'\n    <Compile Include="Shared\\IndependentPromptStubs.cs" />'
             + b'\n    <Compile Include="Shared\\G13IndependentCommandTests.cs" />'
             + b'\n    <Compile Include="..\\Shared\\Commands\\PatBraceCommand.cs" Link="Edition\\Commands\\PatBraceCommand.cs" />')
    project.write_bytes(data.replace(before, added))
    files = [
        shared / "AutodeskSimulationStubs.cs",
        shared / "IndependentPromptStubs.cs",
        shared / "G13IndependentCommandTests.cs",
        project,
        destination / "cad-plugin/Shared/Commands/PatBraceCommand.cs",
        destination / "cad-plugin/Shared/Commands/PatBraceGeometry.cs",
        destination / "cad-plugin/Shared/Commands/PatBraceEntity.cs"
    ]
    hashes = {str(f.relative_to(destination)).replace("\\", "/"): hashlib.sha256(f.read_bytes()).hexdigest() for f in files}
    (root / (name + "-hashes.json")).write_text(json.dumps(hashes, indent=2) + "\n", encoding="utf-8")
print("Independent command harness prepared in temporary copies only.")
