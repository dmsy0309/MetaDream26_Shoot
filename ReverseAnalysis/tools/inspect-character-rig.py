import UnityPy,json
from pathlib import Path
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
r=Path('~/Library/Application Support/Steam/steamapps/common/ROUNDS/ROUNDS.app/Contents/Resources/Data')
e=UnityPy.load(str(r/'sharedassets0.assets'));gen=TypeTreeGenerator('2022.3.34f1');gen.load_local_dll_folder(str(r/'Managed'));e.typetree_generator=gen
f=e.file
if not hasattr(f,'objects'):f=list(e.files.values())[0]
def tree(go,depth=0):
 d=go.read();res={'id':go.path_id,'name':d.m_Name,'active':d.m_IsActive,'components':[]}
 tr=None
 for pair in d.m_Component:
  o=pair.component.deref();data=o.read_typetree();typ=o.type.name
  if typ=='MonoBehaviour':
   h=o.parse_monobehaviour_head();typ=h.m_Script.deref_parse_as_object().m_ClassName
  if typ=='Transform':tr=o.read()
  res['components'].append({'id':o.path_id,'type':typ,'data':data})
 if tr:res['children']=[tree(p.deref_parse_as_object().m_GameObject.deref(),depth+1) for p in tr.m_Children]
 return res

out={str(id):tree(f.objects[id]) for id in [2724,2586]}
Path('ReverseAnalysis/evidence/character-rig-native.json').write_text(json.dumps(out,indent=2))
