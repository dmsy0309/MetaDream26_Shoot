import os
import UnityPy,json,struct
from pathlib import Path
root=Path(os.environ.get('ROUNDS_DATA', str(Path.home() / 'Library/Application Support/Steam/steamapps/common/ROUNDS/ROUNDS.app/Contents/Resources/Data')))
env=UnityPy.load(str(root/'sharedassets0.assets'))
items=json.loads(Path('ReverseAnalysis/evidence/parity-serialized.json').read_text())
ids={x['id']:x['type'] for x in items if x['file']=='sharedassets0.assets'}
for o in env.objects:
 if o.path_id in ids:
  h=o.parse_monobehaviour_head();raw=o.get_raw_data();print(ids[o.path_id],o.path_id,'GO',h.m_GameObject.deref_parse_as_object().m_Name,'bytes',len(raw),'tail',raw[-48:].hex());print([(i,struct.unpack_from('<f',raw,i)[0]) for i in range(28,len(raw)-3,4)])
