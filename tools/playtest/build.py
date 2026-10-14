#!/usr/bin/env python3
"""Build a native, isolated current-source Unity playtest; licensed sources stay private."""
import argparse,hashlib,json,os,platform,re,shutil,stat,subprocess,sys,uuid
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
def source_inputs(root,manifest_path=None):
 """Use Git in a checkout, or verify Hub's explicit inventory in a Git-free snapshot."""
 root=Path(root).resolve()
 if(root/'.git').exists():
  listed=subprocess.check_output(['git','-C',str(root),'ls-files','-z','--cached','--others','--exclude-standard']).split(b'\0')
  names=sorted({os.fsdecode(value)for value in listed if value})
  commit=subprocess.run(['git','-C',str(root),'rev-parse','--verify','HEAD'],capture_output=True,text=True)
  return [(name,None)for name in names],{'sourceCommit':commit.stdout.strip()if commit.returncode==0 else 'initial uncommitted source','sourceKind':'working-tree'}
 if not manifest_path:raise RuntimeError('Git-free source requires Playtest Hub\'s MAYO_BUILD_SOURCE_MANIFEST inventory. Update the launcher and retry the build.')
 manifest_file=Path(manifest_path)
 if not manifest_file.is_absolute()or manifest_file.is_symlink()or not manifest_file.is_file()or manifest_file.stat().st_size>16*1024*1024:raise RuntimeError('Invalid source snapshot manifest.')
 data=json.loads(manifest_file.read_text(encoding='utf-8'))
 if not isinstance(data,dict)or data.get('schema_version')!=1 or data.get('snapshot_path')!=str(root):raise RuntimeError('Source snapshot manifest belongs to a different directory or schema.')
 info=data.get('source');rows=data.get('files')
 if not isinstance(info,dict)or info.get('source_kind')!='working-tree-snapshot'or not isinstance(rows,list)or len(rows)>50000:raise RuntimeError('Invalid source snapshot inventory.')
 commit=info.get('commit','')
 if not isinstance(commit,str)or commit and not re.fullmatch(r'[a-f0-9]{40}|[a-f0-9]{64}',commit):raise RuntimeError('Invalid source snapshot commit.')
 inventory=[];seen=set();digest=hashlib.sha256()
 for row in rows:
  if not isinstance(row,dict):raise RuntimeError('Invalid source snapshot entry.')
  name,expected,executable=row.get('path'),row.get('sha256'),row.get('executable')
  if not isinstance(name,str)or not name or len(name)>4096 or '\\'in name or '\0'in name or not isinstance(expected,str)or not re.fullmatch(r'[a-f0-9]{64}',expected)or not isinstance(executable,bool):raise RuntimeError('Invalid source snapshot entry.')
  rel=PurePosixPath(name)
  if rel.is_absolute()or re.match(r'^[a-zA-Z]:',name)or any(part in {'','.','..','.git'}for part in name.split('/'))or name in seen:raise RuntimeError('Unsafe or duplicate source snapshot path.')
  seen.add(name);source=root.joinpath(*rel.parts)
  if any(root.joinpath(*rel.parts[:i]).is_symlink()for i in range(1,len(rel.parts)+1))or not source.is_file()or not stat.S_ISREG(source.stat().st_mode):raise RuntimeError('Source snapshot files must be regular files without links.')
  if sha(source)!=expected or bool(source.stat().st_mode&0o111)!=executable:raise RuntimeError('Source snapshot changed: '+name)
  digest.update(json.dumps([name,expected,executable]).encode());inventory.append((name,expected))
 if info.get('snapshot_files')!=len(rows)or info.get('snapshot_sha256')!=digest.hexdigest():raise RuntimeError('Source snapshot inventory hash does not match.')
 return inventory,{'sourceCommit':commit or 'initial uncommitted source','sourceKind':'working-tree-snapshot','sourceDirty':info.get('dirty')is True,'inputSnapshotSha256':digest.hexdigest()}
def capture_source(root,destination,manifest_path=None):
 inventory,provenance=source_inputs(root,manifest_path);files={}
 for name,expected in inventory:
  rel=Path(name);source=Path(root)/rel
  if source.is_symlink()or not source.is_file():
   if expected is not None:raise RuntimeError('Source snapshot changed while copying: '+name)
   continue
  if any(part in {'.git','Library','Temp','Logs','obj','Obj','.local','__pycache__'}for part in rel.parts):continue
  target=Path(destination)/rel;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
  actual=sha(target)
  if expected is not None and actual!=expected:raise RuntimeError('Source snapshot changed while copying: '+name)
  files[rel.as_posix()]=actual
 return files,provenance
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
 files,provenance=capture_source(ROOT,source,os.environ.get('MAYO_BUILD_SOURCE_MANIFEST'))
 paid=restore(source/'Game',manifest)
 report={'schemaVersion':1,'status':'building','profile':plan,**provenance,'sourceFiles':files,'licensedRestoration':paid,'sourceSnapshot':str(source),'started':datetime.now(timezone.utc).isoformat()}
 reportPath=logs/'build-result.json';reportPath.write_text(json.dumps(report,indent=2)+'\n')
 cmd=[str(editor),'-batchmode','-quit','-nographics','-projectPath',str(source/'Game'),'-executeMethod','PrepareFrontier.Build','-frontierOutput='+str(output/artifact),'-frontierTarget='+target,'-logFile',str(logs/'editor.log'),'-job-worker-count','2','-import-worker-count','1']
 with(logs/'text-import-process.log').open('w')as stream:
  print('Log: '+str(logs/'text-import.log'),flush=True)
  textCmd=[str(editor),'-batchmode','-nographics','-projectPath',str(source/'Game'),'-executeMethod','PrepareFrontier.ImportTextResources','-logFile',str(logs/'text-import.log'),'-job-worker-count','2','-import-worker-count','1']
  imported=subprocess.run(textCmd,stdout=stream,stderr=subprocess.STDOUT,timeout=300)
  if imported.returncode:raise RuntimeError('Native text resources import failed; inspect '+str(logs/'text-import.log'))
 print('Log: '+str(logs/'editor.log'),flush=True)
 with(logs/'process.log').open('w')as stream:result=subprocess.run(cmd,stdout=stream,stderr=subprocess.STDOUT,timeout=900)
 report.update(status='built'if result.returncode==0 and(output/artifact).exists()else'failed',exitCode=result.returncode,finished=datetime.now(timezone.utc).isoformat());reportPath.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({'status':report['status'],'artifact':str(output/artifact),'log':str(logs/'editor.log')}))
 if report['status']!='built':raise RuntimeError('Native Unity build failed; inspect its preserved editor log.')
if __name__=='__main__':
 try:main()
 except Exception as e:print('Build error:',e,file=sys.stderr);sys.exit(1)
