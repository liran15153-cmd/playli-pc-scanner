"""Verify local artifact identity/PE resources, never execute or publish it."""
from pathlib import Path
import hashlib,json,re,struct,zipfile,xml.etree.ElementTree as ET

repo=Path(__file__).resolve().parent.parent
scanner=repo
release=json.loads((scanner/'artifacts/pc-check-release.json').read_text())
receipt=json.loads((scanner/'artifacts/build-receipt.json').read_text())
version=ET.parse(scanner/'Windows/Windows.csproj').find('.//Version').text
assert re.fullmatch(r'\d+\.\d+\.\d+',version) and release['version']==receipt['version']==version
digest=lambda data:hashlib.sha256(data).hexdigest()
for name,expected in receipt['sourceFiles'].items():assert digest((scanner/name).read_bytes())==expected, 'Stale native source: '+name
source_sha=digest(json.dumps(receipt['sourceFiles'],sort_keys=True,separators=(',',':')).encode())
assert source_sha==receipt['sourceSha256']==release['nativeSourceSha256']
paths={'exe':scanner/'artifacts/win-x64/PLAYLI-PC-Check.exe','zip':scanner/'artifacts/PLAYLI-PC-Check-win-x64.zip'}
for kind,path in paths.items():
 data=path.read_bytes();item=release['artifacts'][kind]
 assert item['filename']==path.name and item['bytes']==len(data) and item['sha256']==digest(data), 'Artifact mismatch: '+kind
 assert path.with_suffix(path.suffix+'.sha256').read_text().split()[0]==digest(data)
data=paths['exe'].read_bytes();assert data[:2]==b'MZ'
pe=struct.unpack_from('<I',data,0x3c)[0];assert data[pe:pe+4]==b'PE\0\0'
assert struct.unpack_from('<H',data,pe+4)[0]==0x8664
optional=pe+24;assert struct.unpack_from('<H',data,optional)[0]==0x20b
assert struct.unpack_from('<H',data,optional+68)[0]==2, 'Expected Windows GUI subsystem'
certificate=struct.unpack_from('<II',data,optional+112+4*8);assert certificate==(0,0) and release['signed'] is False
section_count=struct.unpack_from('<H',data,pe+6)[0];section_table=optional+struct.unpack_from('<H',data,pe+20)[0]
sections=[struct.unpack_from('<IIII',data,section_table+40*i+8) for i in range(section_count)]
def offset(rva):
 for virtual_size,start,raw_size,raw_offset in sections:
  if start<=rva<start+max(virtual_size,raw_size):return raw_offset+rva-start
 raise AssertionError('Unmapped PE resource RVA')
resource_rva=struct.unpack_from('<I',data,optional+112+2*8)[0];base=offset(resource_rva);resources=[]
def walk(relative=0,ids=()):
 assert len(ids)<8
 named,count=struct.unpack_from('<HH',data,base+relative+12)
 for i in range(named+count):
  name,target=struct.unpack_from('<II',data,base+relative+16+8*i)
  if target&0x80000000:walk(target&0x7fffffff,ids+(name,))
  else:
   rva,size=struct.unpack_from('<II',data,base+target);start=offset(rva);resources.append((ids+(name,),data[start:start+size]))
walk();types={ids[0] for ids,_ in resources};assert {3,14,16,24}<=types, 'Missing icon/version/manifest resources'
manifest=ET.fromstring(next(payload for ids,payload in resources if ids[0]==24).decode('utf-8-sig'))
execution=manifest.find('.//{urn:schemas-microsoft-com:asm.v3}requestedExecutionLevel');assert execution.attrib=={'level':'asInvoker','uiAccess':'false'}
assert manifest.find('{urn:schemas-microsoft-com:asm.v1}assemblyIdentity').get('version')==version+'.0'
payload=next(payload for ids,payload in resources if ids[0]==16);fixed=payload.index(struct.pack('<I',0xfeef04bd));_,_,ms,ls,product_ms,product_ls=struct.unpack_from('<IIIIII',payload,fixed)
file_version=(ms>>16,ms&65535,ls>>16,ls&65535);assert file_version==tuple(map(int,(version+'.0').split('.')));assert (product_ms,product_ls)==(ms,ls)
with zipfile.ZipFile(paths['zip']) as archive:
 assert archive.testzip() is None
 names=archive.namelist();assert all(not Path(name).is_absolute() and '..' not in Path(name).parts for name in names)
 assert archive.read('PLAYLI-PC-Check.exe')==data
 for name in ['README.md','THIRD-PARTY-NOTICES.md','LICENSE','PRIVACY.md','SIGNING.md','SECURITY.md']:
  assert archive.read(name)==(scanner/name).read_bytes()
 assert archive.read('THIRD-PARTY-NOTICES.md')==(scanner/'THIRD-PARTY-NOTICES.md').read_bytes()
 notices=[name for name in names if name.startswith('licenses/')];assert len(notices)>=3
 for name in notices:assert archive.read(name)==(scanner/'artifacts/win-x64'/name).read_bytes()
result={'version':version,'nativeSourceSha256':source_sha,'nativeSourceFiles':len(receipt['sourceFiles']),'artifacts':release['artifacts'],'peMachine':'x86-64','subsystem':'Windows GUI','fileVersion':list(file_version),'resourceTypes':sorted(types),'executionLevel':'asInvoker','signed':False,'archiveFiles':len(names),'redistributedNoticeFiles':len(notices),'crcVerified':True,'nestedExeIdentical':True,'windowsExecuted':False,'httpsHandoffVerified':False,'publicDownloadVerified':False}
out=scanner/'artifacts/distribution-verification.json';out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result,indent=2))
