from pathlib import Path
import datetime, json, os, subprocess, sys
root = Path(__file__).resolve().parent
project = root / 'framework-probe'
env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE='en-US')
results = []
for snapshot in ('baseline-run', 'candidate-fixed'):
    source = root / snapshot
    for year, tfm, nj in [('2013','net40','net35'), ('2015','net45','net45')]:
        tag = 'framework-' + snapshot + '-' + year
        obj = project / ('obj/' + tag)
        out = project / ('bin/' + tag)
        parser = source / ('cad-plugin/Shared/IO/SimpleJson.cs' if snapshot == 'candidate-fixed' else 'cad-plugin/2007/PatentMarker/IO/SimpleJson.cs')
        newtonsoft = Path(os.environ['USERPROFILE']) / ('.nuget/packages/newtonsoft.json/13.0.3/lib/' + nj + '/Newtonsoft.Json.dll')
        command = ['dotnet','build',str(project / 'FrameworkProbe.csproj'),'-c','Release','--nologo','-v','quiet','-p:CadYear='+year,'-p:SourceRoot='+str(source),'-p:ParserFile='+str(parser),'-p:NewtonsoftAssembly='+str(newtonsoft),'-p:BaseIntermediateOutputPath='+str(obj)+os.sep,'-p:MSBuildProjectExtensionsPath='+str(obj)+os.sep,'-p:BaseOutputPath='+str(out)+os.sep]
        build = subprocess.run(command,cwd=root,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        (root / (tag + '-build.log')).write_bytes(build.stdout)
        entry = {'snapshot':snapshot,'year':year,'build_command':command,'build_exit':build.returncode}
        if build.returncode == 0:
            command = [str(out / ('Release/' + tfm + '/FrameworkProbe.exe'))]
            run = subprocess.run(command,cwd=root,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
            (root / (tag + '.log')).write_bytes(run.stdout)
            entry.update({'command':command,'exit':run.returncode,'time_local':datetime.datetime.now().astimezone().isoformat()})
        results.append(entry)
        (root / 'framework-independent-runs.json').write_text(json.dumps(results,indent=2)+'\n',encoding='utf-8')
        print(tag,'build='+str(build.returncode),'run='+str(entry.get('exit')),flush=True)
        if build.returncode != 0 or entry.get('exit') != (1 if snapshot == 'baseline-run' else 0): sys.exit(1)
sys.exit(0)
