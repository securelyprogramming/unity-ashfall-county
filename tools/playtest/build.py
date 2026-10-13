#!/usr/bin/env python3
"""Build a native, isolated current-source Unity playtest; licensed sources stay private."""
import argparse,hashlib,json,os,platform,re,shutil,subprocess,sys,uuid
from datetime import datetime,timezone
from pathlib import Path,PurePosixPath
ROOT=Path(__file__).resolve().parents[2]
TARGETS={'macos':('Darwin','StandaloneOSX','MacStandaloneSupport','Game.app'),'windows':('Windows','StandaloneWindows64','WindowsStandaloneSupport','Game.exe'),'linux':('Linux','StandaloneLinux64','LinuxStandaloneSupport','Game.x86_64')}
VERSION='6000.6.0f1'
def sha(p):
 h=hashlib.sha256()
 with p.open('rb')as f:
  for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
 return h.hexdigest()
def restore(project,manifest):
 cache=Path(os.environ.get('FRONTIER_ASSET_CACHE',str(Path.home()/'code/game_asset_cache/frontier-synty'))).expanduser().resolve();rows=manifest.get('files',[])
 if sum(r['size']for r in rows)>250*1024*1024:raise RuntimeError('Licensed selection exceeds reviewed bound.')
 restored=[]
 for r in rows:
  rel=PurePosixPath(r['relative_path'])
  if rel.is_absolute()or'..'in rel.parts:raise RuntimeError('Unsafe licensed selection path.')
  source=cache.joinpath(*rel.parts)
  if not source.is_file():raise RuntimeError('Restore the purchased selection into the private FRONTIER_ASSET_CACHE first: '+str(rel))
  if source.stat().st_size!=r['size']or sha(source)!=r['sha256']:raise RuntimeError('Private licensed source differs from selection: '+str(rel))
  destination=project/'Assets/Resources/LicensedFrontier'/rel;destination.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,destination);restored.append({'path':str(rel),'sha256':r['sha256'],'bytes':r['size']})
 return restored
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--platform',required=True,choices=TARGETS);parser.add_argument('--output',required=True,type=Path);parser.add_argument('--check',action='store_true');args=parser.parse_args();host,target,module,artifact=TARGETS[args.platform]
 if platform.system()!=host:raise RuntimeError('Build this profile on its native '+host+' host; cross-platform release validation is separate.')
 output=args.output.expanduser().resolve()
 if output==ROOT or ROOT in output.parents:raise RuntimeError('Choose a fresh output outside the source checkout.')
 if output.exists()and any(output.iterdir()):raise RuntimeError('Preserve previous builds; output must be fresh or empty.')
 defaults={'Darwin':Path('/Applications/Unity/Hub/Editor')/VERSION/'Unity.app/Contents/MacOS/Unity','Windows':Path(os.environ.get('ProgramFiles','C:/Program Files'))/'Unity/Hub/Editor'/VERSION/'Editor/Unity.exe','Linux':Path.home()/'Unity/Hub/Editor'/VERSION/'Editor/Unity'};editor=Path(os.environ.get('UNITY_EDITOR',str(defaults[host]))).expanduser()
 if not editor.is_file():raise RuntimeError('Install the required Unity '+VERSION+' native editor or set UNITY_EDITOR.')
 actual=subprocess.run([str(editor),'-version'],capture_output=True,text=True,timeout=30)
 if actual.returncode or VERSION not in actual.stdout+actual.stderr:raise RuntimeError('Unity version does not match '+VERSION)
 base=editor.parent.parent if host=='Darwin'else editor.parent/'Data'
 moduleCandidates=[base/'PlaybackEngines'/module]
 if host=='Darwin':moduleCandidates.append(editor.parents[3]/'PlaybackEngines'/module)
 if not any(p.is_dir()for p in moduleCandidates):raise RuntimeError('The native '+module+' module is not installed. Do not bypass its platform/toolchain checks.')
 manifest=json.loads((ROOT/'ContentSource/Art/selection.json').read_text());plan={'editorVersion':VERSION,'platform':args.platform,'target':target,'output':str(output/artifact),'licensedFiles':len(manifest['files'])}
 if args.check:print(json.dumps(plan));return
 output.mkdir(parents=True,exist_ok=True);logs=output.parent/(output.name+'.logs');logs.mkdir(exist_ok=True);source=logs/('source-'+uuid.uuid4().hex);source.mkdir()
 command=['git','-C',str(ROOT),'ls-files','-z','--cached','--others','--exclude-standard'];listed=subprocess.check_output(command).split(b'\0');files={}
 for value in sorted(set(listed)):
  if not value:continue
  rel=Path(os.fsdecode(value));p=ROOT/rel
  if p.is_symlink()or not p.is_file():continue
  if any(part in {'.git','Library','Temp','Logs','obj','Obj','.local','__pycache__'}for part in rel.parts):continue
  destination=source/rel;destination.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,destination);files[str(rel)]=sha(p)
 paid=restore(source/'Game',manifest)
 report={'schemaVersion':1,'status':'building','profile':plan,'sourceCommit':subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD'],text=True).strip()if subprocess.run(['git','-C',str(ROOT),'rev-parse','--verify','HEAD'],capture_output=True).returncode==0 else 'initial uncommitted source','sourceFiles':files,'licensedRestoration':paid,'sourceSnapshot':str(source),'started':datetime.now(timezone.utc).isoformat()}
 reportPath=logs/'build-result.json';reportPath.write_text(json.dumps(report,indent=2)+'\n')
 cmd=[str(editor),'-batchmode','-quit','-nographics','-projectPath',str(source/'Game'),'-executeMethod','PrepareFrontier.Build','-frontierOutput='+str(output/artifact),'-frontierTarget='+target,'-logFile',str(logs/'editor.log'),'-job-worker-count','2','-import-worker-count','1']
 with(logs/'text-import-process.log').open('w')as stream:
  textCmd=[str(editor),'-batchmode','-nographics','-projectPath',str(source/'Game'),'-executeMethod','PrepareFrontier.ImportTextResources','-logFile',str(logs/'text-import.log'),'-job-worker-count','2','-import-worker-count','1']
  imported=subprocess.run(textCmd,stdout=stream,stderr=subprocess.STDOUT,timeout=300)
  if imported.returncode:raise RuntimeError('Native text resources import failed; inspect '+str(logs/'text-import.log'))
 with(logs/'process.log').open('w')as stream:result=subprocess.run(cmd,stdout=stream,stderr=subprocess.STDOUT,timeout=900)
 report.update(status='built'if result.returncode==0 and(output/artifact).exists()else'failed',exitCode=result.returncode,finished=datetime.now(timezone.utc).isoformat());reportPath.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({'status':report['status'],'artifact':str(output/artifact),'log':str(logs/'editor.log')}))
 if report['status']!='built':raise RuntimeError('Native Unity build failed; inspect its preserved editor log.')
if __name__=='__main__':
 try:main()
 except Exception as e:print('Build error:',e,file=sys.stderr);sys.exit(1)
