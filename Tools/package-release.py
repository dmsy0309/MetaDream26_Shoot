#!/usr/bin/env python3
from pathlib import Path
import zipfile,tarfile,hashlib,json,sys
root=Path(__file__).resolve().parents[1]
items=[('Builds/Windows/MetadreamShoot','Builds/Windows/MetadreamShoot-Windows-x64.zip'),('Builds/Server/Windows/MetadreamShootServer','Builds/Server/MetadreamShootServer-Windows-x64.zip'),('Builds/Server/Linux/MetadreamShootServer','Builds/Server/MetadreamShootServer-Linux-x64.tar.gz'),('MapEditorProject/Builds/Windows/MetadreamShootMapMaker','MapEditorProject/Builds/Windows/MetadreamShootMapMaker-Windows-x64.zip')]
for source,dest in items:
 folder=root/source;archive=root/dest
 print('PACK',dest,flush=True)
 if dest.endswith('.zip'):
  with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
   for p in sorted(folder.rglob('*')):
    if p.is_file() and p.name!='.DS_Store':z.write(p,p.relative_to(folder.parent))
  with zipfile.ZipFile(archive) as z:assert z.testzip() is None
 else:
  with tarfile.open(archive,'w:gz') as t:t.add(folder,arcname=folder.name)
  with tarfile.open(archive) as t:assert any(n.name.endswith('.x86_64') for n in t.getmembers())
manifest=[{'path':d,'bytes':(root/d).stat().st_size,'sha256':hashlib.sha256((root/d).read_bytes()).hexdigest()} for _,d in items]
(root/'Builds/release-manifest.json').write_text(json.dumps(manifest,indent=2))
print(json.dumps(manifest,indent=2),flush=True)
