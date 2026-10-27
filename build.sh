#!/usr/bin/env bash
set -euo pipefail
scanner_root="$(cd -- "$(dirname -- "$0")" && pwd)"
scanner_dotnet="${PLAYLI_DOTNET:-dotnet}"
scanner_python="${PLAYLI_PYTHON:-python3}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
cd "$scanner_root"
"$scanner_dotnet" restore "$scanner_root/Windows/Windows.csproj" --runtime win-x64 --locked-mode
"$scanner_dotnet" run --project "$scanner_root/Tests/Tests.csproj" --configuration Release
"$scanner_dotnet" publish "$scanner_root/Windows/Windows.csproj" --configuration Release --runtime win-x64 --self-contained true --output "$scanner_root/artifacts/win-x64"
"$scanner_python" - "$scanner_root/artifacts/win-x64/PLAYLI-PC-Check.exe" "$scanner_dotnet" <<'PY'
from pathlib import Path
import hashlib,json,shutil,struct,sys,zipfile,xml.etree.ElementTree as ET
p=Path(sys.argv[1]);data=p.read_bytes()
assert data[:2]==b'MZ'
offset=struct.unpack_from('<I',data,0x3c)[0]
assert data[offset:offset+4]==b'PE\0\0'
assert struct.unpack_from('<H',data,offset+4)[0]==0x8664
digest=hashlib.sha256(data).hexdigest()
p.with_suffix('.exe.sha256').write_text(digest+'  '+p.name+'\n')
root=p.parents[2];notices=p.parent/'licenses';notices.mkdir(exist_ok=True)
assets=json.loads((root/'Windows/obj/project.assets.json').read_text())
runtime=json.loads((root/'Windows/bin/Release/net10.0-windows/win-x64/PLAYLI-PC-Check.runtimeconfig.json').read_text())
for folder in assets['packageFolders']:
 base=Path(folder)
 targets=[base/k.lower() for k,v in assets['libraries'].items() if v['type']=='package']
 targets += [base/(f['name'].lower()+'.runtime.win-x64')/f['version'] for f in runtime['runtimeOptions']['includedFrameworks']]
 for target in targets:
  if not target.exists(): continue
  for source in target.iterdir():
   if source.is_file() and ('LICENSE' in source.name.upper() or 'NOTICE' in source.name.upper()):
    shutil.copyfile(source,notices/(target.parent.name+'-'+source.name))
resolved=shutil.which(sys.argv[2]) or sys.argv[2]
for name in ['LICENSE.txt','ThirdPartyNotices.txt']:
 source=Path(resolved).resolve().parent/name
 if source.exists(): shutil.copyfile(source,notices/('dotnet-sdk-'+name))
assert any(notices.iterdir()), 'Missing redistribution notices'
for name in ['README.md','THIRD-PARTY-NOTICES.md','LICENSE','PRIVACY.md','SIGNING.md','SECURITY.md']:shutil.copyfile(root/name,p.parent/name)
for extra in p.parent.glob('*.pdb'):extra.unlink()
archive=p.parent.parent/'PLAYLI-PC-Check-win-x64.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as output:
 for item in sorted(p.parent.rglob('*')):
  if item.is_file():output.write(item,item.relative_to(p.parent))
version=ET.parse(root/'Windows/Windows.csproj').find('.//Version').text
source_paths=[root/name for name in ['build.sh','global.json','README.md','THIRD-PARTY-NOTICES.md','LICENSE','PRIVACY.md','SIGNING.md','SECURITY.md','NuGet.Config','.gitignore','.gitattributes']]
for folder in ['Core','Windows','Tests','scripts','.github']:
 source_paths += [file for file in (root/folder).rglob('*') if file.is_file() and not {'bin','obj'}.intersection(file.relative_to(root).parts)]
source_files={file.relative_to(root).as_posix():hashlib.sha256(file.read_bytes()).hexdigest() for file in sorted(source_paths)}
source_sha=hashlib.sha256(json.dumps(source_files,sort_keys=True,separators=(',',':')).encode()).hexdigest()
receipt={'version':version,'sourceSha256':source_sha,'sourceFiles':source_files,'exeSha256':digest,'windowsExecuted':False}
(root/'artifacts/build-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
release={'schemaVersion':1,'version':version,'nativeSourceSha256':source_sha,'signed':False,'artifacts':{kind:{'filename':file.name,'bytes':file.stat().st_size,'sha256':hashlib.sha256(file.read_bytes()).hexdigest()} for kind,file in [('exe',p),('zip',archive)]}}
release_path=root/'artifacts/pc-check-release.json'
release_path.write_text(json.dumps(release,indent=2)+'\n')
archive.with_suffix('.zip.sha256').write_text(release['artifacts']['zip']['sha256']+'  '+archive.name+'\n')
print('Verified Windows x64 PE:',p.name,'bytes:',len(data),'SHA-256:',digest)
print('Distribution archive:',archive,'bytes:',archive.stat().st_size)
print('Windows execution, signing and public release remain separate checks.')
PY
"$scanner_python" "$scanner_root/scripts/verify-release.py"
