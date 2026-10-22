import UnityPy,json,hashlib
from pathlib import Path
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
root=Path('~/Library/Application Support/Steam/steamapps/common/ROUNDS/ROUNDS.app/Contents/Resources/Data')
gen=TypeTreeGenerator('2022.3.34f1');gen.load_local_dll_folder(str(root/'Managed'));out=[]
for path in list(root.glob('*.assets'))+[root/'level0']:
 env=UnityPy.load(str(path));env.typetree_generator=gen
 for obj in env.objects:
  if obj.type.name!='MonoBehaviour': continue
  try:
   head=obj.parse_monobehaviour_head();script=head.m_Script.deref_parse_as_object();name=script.m_ClassName
   if name in ['PlayerMovement','PlayerJump','Gravity','PlayerVelocity','CardChoice','CharacterCreatorHandler']:
    d=obj.parse_as_dict();out.append({'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'id':obj.path_id,'type':name,'data':d});print(path.name,name,json.dumps(d)[:2400],flush=True)
  except Exception as ex: pass
 print(path.name,'done',flush=True)
Path('ReverseAnalysis/evidence/parity-serialized.json').write_text(json.dumps(out,indent=2))
