from pathlib import Path
import datetime, json, os, shutil, subprocess, sys
root = Path(__file__).resolve().parent
project = root / 'framework-legacy-probe'
project.mkdir()
original = (root / 'candidate/tools/G04JsonRegression/G04JsonRegression.csproj').read_text(encoding='utf-8')
original = original.replace('<TargetFramework>net8.0</TargetFramework>','<TargetFramework>net40</TargetFramework>').replace('<ImplicitUsings>enable</ImplicitUsings>','<ImplicitUsings>disable</ImplicitUsings>')
(project / 'FrameworkLegacyProbe.csproj').write_text(original,encoding='utf-8')
shutil.copy2(root / 'candidate/tools/G04JsonRegression/HostStubs.cs', project / 'HostStubs.cs')
program = (root / 'IndependentEdgeProbe-v2.cs').read_text(encoding='utf-8')
old = 'return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();'
new = 'using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();'
assert program.count(old) == 1
program = program.replace(old,new)
(project / 'Program.cs').write_text(program,encoding='utf-8')
env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE='en-US')
results = []
for snapshot in ('candidate', 'candidate-fixed'):
    source = root / snapshot
    for year in ('2007', '2010'):
        tag = 'framework-legacy-' + snapshot + '-' + year
        obj = project / ('obj/' + tag)
        out = project / ('bin/' + tag)
        parser = source / 'cad-plugin/Shared/IO/SimpleJson.cs'
        command = ['dotnet','build',str(project / 'FrameworkLegacyProbe.csproj'),'-c','Release','--nologo','-v','quiet','-p:CadYear='+year,'-p:SourceRoot='+str(source),'-p:ParserFile='+str(parser),'-p:BaseIntermediateOutputPath='+str(obj)+os.sep,'-p:MSBuildProjectExtensionsPath='+str(obj)+os.sep,'-p:BaseOutputPath='+str(out)+os.sep]
        build = subprocess.run(command,cwd=root,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        (root / (tag + '-build.log')).write_bytes(build.stdout)
        entry = {'snapshot':snapshot,'year':year,'build_command':command,'build_exit':build.returncode}
        if build.returncode == 0:
            command = [str(out / 'Release/net40/FrameworkLegacyProbe.exe'),str(root / ('framework-legacy-data-' + snapshot + '-' + year))]
            run = subprocess.run(command,cwd=root,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
            (root / (tag + '.log')).write_bytes(run.stdout)
            entry.update({'command':command,'exit':run.returncode,'time_local':datetime.datetime.now().astimezone().isoformat()})
        results.append(entry)
        (root / 'framework-legacy-runs.json').write_text(json.dumps(results,indent=2)+'\n',encoding='utf-8')
        print(tag,'build='+str(build.returncode),'run='+str(entry.get('exit')),flush=True)
        if build.returncode != 0 or entry.get('exit') != (1 if snapshot == 'candidate' else 0): sys.exit(1)
sys.exit(0)
