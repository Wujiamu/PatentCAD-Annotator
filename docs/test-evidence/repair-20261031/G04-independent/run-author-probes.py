from pathlib import Path
import datetime
import json
import os
import subprocess
import sys

root = Path(__file__).resolve().parent
env = dict(os.environ)
env["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
results = []
for snapshot in ("baseline-run", "candidate"):
    source = root / snapshot
    project_dir = source / "tools/G04JsonRegression"
    for year in ("2007", "2010", "2013", "2015", "2025"):
        tag = snapshot + "-" + year
        parser = (source / "cad-plugin/Shared/IO/SimpleJson.cs" if snapshot == "candidate"
                  else source / ("cad-plugin/" + ("2010" if year == "2010" else "2007") + "/PatentMarker/IO/SimpleJson.cs"))
        obj = project_dir / ("obj/" + tag)
        out = project_dir / ("bin/" + tag)
        command = ["dotnet", "build", str(project_dir / "G04JsonRegression.csproj"), "-c", "Release", "--nologo", "-v", "quiet",
                   "-p:CadYear=" + year, "-p:SourceRoot=" + str(source), "-p:ParserFile=" + str(parser),
                   "-p:BaseIntermediateOutputPath=" + str(obj) + os.sep,
                   "-p:MSBuildProjectExtensionsPath=" + str(obj) + os.sep,
                   "-p:BaseOutputPath=" + str(out) + os.sep]
        build = subprocess.run(command, cwd=source, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (root / (tag + "-build.log")).write_bytes(build.stdout)
        entry = {"snapshot": snapshot, "year": year, "build_command": command, "build_exit": build.returncode}
        if build.returncode == 0:
            dll = out / "Release/net8.0/G04JsonRegression.dll"
            command = ["dotnet", str(dll)]
            run = subprocess.run(command, cwd=source, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            (root / (tag + "-probe.log")).write_bytes(run.stdout)
            entry.update({"command": command, "exit": run.returncode,
                          "time_local": datetime.datetime.now().astimezone().isoformat()})
        results.append(entry)
        (root / "author-probes-runs.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
        print(tag, "build=" + str(build.returncode), "run=" + str(entry.get("exit")), flush=True)
        expected = 1 if snapshot == "baseline-run" else 0
        if build.returncode != 0 or entry.get("exit") != expected:
            sys.exit(1)
sys.exit(0)
