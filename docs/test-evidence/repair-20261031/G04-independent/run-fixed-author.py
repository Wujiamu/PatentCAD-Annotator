from pathlib import Path
import datetime, json, os, subprocess, sys
root = Path(__file__).resolve().parent
source = root / 'candidate-fixed'
project = source / 'tools/G04JsonRegression'
env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE='en-US')
results = []
for year in ('2007', '2010', '2013', '2015', '2025'):
    tag = 'candidate-fixed-' + year
    obj = project / ('obj/' + tag)
    out = project / ('bin/' + tag)
    command = ['dotnet','build',str(project / 'G04JsonRegression.csproj'),'-c','Release','--nologo','-v','quiet', '-p:CadYear=' + year, '-p:SourceRoot=' + str(source), '-p:ParserFile=' + str(source / 'cad-plugin/Shared/IO/SimpleJson.cs'), '-p:BaseIntermediateOutputPath=' + str(obj) + os.sep, '-p:MSBuildProjectExtensionsPath=' + str(obj) + os.sep, '-p:BaseOutputPath=' + str(out) + os.sep]
    build = subprocess.run(command, cwd=source, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (root / (tag + '-build.log')).write_bytes(build.stdout)
    entry = {'year':year,'build_command':command,'build_exit':build.returncode}
    if build.returncode == 0:
        command = ['dotnet',str(out / 'Release/net8.0/G04JsonRegression.dll')]
        run = subprocess.run(command,cwd=source,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        (root / (tag + '-probe.log')).write_bytes(run.stdout)
        entry.update({'command':command,'exit':run.returncode,'time_local':datetime.datetime.now().astimezone().isoformat()})
    results.append(entry)
    (root / 'author-probes-fixed-runs.json').write_text(json.dumps(results,indent=2)+'\n',encoding='utf-8')
    print(tag,'build='+str(build.returncode),'run='+str(entry.get('exit')),flush=True)
    if build.returncode != 0 or entry.get('exit') != 0: sys.exit(1)
sys.exit(0)
