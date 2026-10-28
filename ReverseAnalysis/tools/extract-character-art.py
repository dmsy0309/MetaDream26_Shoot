import os
"""Read-only extraction of the supplied ROUNDS character catalog and sprite geometry.
Requires UnityPy 1.25.4 and TypeTreeGeneratorAPI 0.0.10. No game code is executed.
"""
import UnityPy,json,hashlib,struct,math
from pathlib import Path
from PIL import Image,ImageDraw
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
ROOT=Path(__file__).resolve().parents[2]
SOURCE=Path(os.environ.get('ROUNDS_DATA', str(Path.home() / 'Library/Application Support/Steam/steamapps/common/ROUNDS/ROUNDS.app/Contents/Resources/Data')))
OUT=ROOT/'Assets/Reimplementation/Resources/CharacterArt';OUT.mkdir(parents=True,exist_ok=True)
gen=TypeTreeGenerator('2022.3.34f1');gen.load_local_dll_folder(str(SOURCE/'Managed'))
env=UnityPy.load(str(SOURCE/'sharedassets0.assets'));env.typetree_generator=gen;f=list(env.files.values())[0]
level=UnityPy.load(str(SOURCE/'level0'));level.typetree_generator=gen
loader=next(o.read_typetree() for o in level.objects if o.type.name=='MonoBehaviour' and o.path_id==3707)
catalog={'eyes':[],'mouths':[],'accessories':[],'sprites':[]}
exported={}
def sprite(id):
 if id in exported:return
 s=f.objects[id].read();im=s.image.convert('RGBA');rd=s.m_RD
 if s.m_SpriteAtlas:
  atlas=s.m_SpriteAtlas.deref_parse_as_object();rd=next(v for k,v in atlas.m_RenderDataMap if k==s.m_RenderDataKey)
 w,h=round(s.m_Rect.width),round(s.m_Rect.height);off=rd.textureRectOffset
 canvas=Image.new('RGBA',(w,h));canvas.paste(im,(round(off.x),round(h-off.y-im.height)))
 path=OUT/(str(id)+'.png');canvas.save(path)
 info={'id':id,'name':s.m_Name,'width':w,'height':h,'pivot':{'x':s.m_Pivot.x,'y':s.m_Pivot.y},'ppu':s.m_PixelsToUnits,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
 exported[id]=info;catalog['sprites'].append(info)
def animation(controller):
 ctrl=f.objects[controller].read_typetree();ref=ctrl['m_AnimationClips'][0];clip=f.objects[ref['m_PathID']].read_typetree();mc=clip['m_MuscleClip'];data=mc['m_Clip']['data'];stream=data['m_StreamedClip'];curves=[[] for _ in range(stream['curveCount'])]
 raw=struct.pack('<'+'I'*len(stream['data']),*stream['data']);at=0
 while at<len(raw):
  time,count=struct.unpack_from('<fi',raw,at);at+=8
  for _ in range(count):
   index,a,b,c,d=struct.unpack_from('<i4f',raw,at);at+=20
   if math.isfinite(time) and time>=0:curves[index].append({'time':time,'a':a,'b':b,'c':c,'d':d})
 assert data['m_DenseClip']['m_CurveCount']==0
 curves += [[{'time':0,'a':0,'b':0,'c':0,'d':v}] for v in data['m_ConstantClip']['data']]
 channels=[];index=0
 for b in clip['m_ClipBindingConstant']['genericBindings']:
  assert b['path']==0
  attrs={1:['px','py','pz'],3:['sx','sy','sz']} if b['typeID']==4 else {304273561:['ca'],2526845255:['cr'],4215373228:['cg'],2334886179:['cb']}
  for prop in attrs[b['attribute']]:channels.append({'property':prop,'keys':curves[index]});index+=1
 return {'name':clip['m_Name'],'duration':mc['m_StopTime'],'channels':channels}

def node(go,root=False):
 tr=next(c.component.deref_parse_as_object() for c in go.m_Component if c.component.deref().type.name=='Transform')
 def vec(v,keys):return {k:getattr(v,k) for k in keys}
 n={'name':go.m_Name,'position':vec(tr.m_LocalPosition,'xyz'),'rotation':vec(tr.m_LocalRotation,'xyzw'),'size':vec(tr.m_LocalScale,'xyz'),'active':bool(go.m_IsActive),'children':[],'sprite':0,'animations':[]}
 for c in go.m_Component:
  o=c.component.deref()
  if o.type.name=='SpriteRenderer':
   sr=o.read();sid=sr.m_Sprite.path_id
   if sid:sprite(sid)
   n.update(sprite=sid,color=vec(sr.m_Color,'rgba'),order=sr.m_SortingOrder,enabled=sr.m_Enabled,flipX=sr.m_FlipX,flipY=sr.m_FlipY)
  elif o.type.name=='Animator':
   a=o.read();n['animations'].append(animation(a.m_Controller.path_id))
 for child in tr.m_Children:n['children'].append(node(child.deref_parse_as_object().m_GameObject.deref_parse_as_object()))
 return n
for kind in ['eyes','mouths','accessories']:
 for index,p in enumerate(loader[kind]):
  o=f.objects[p['m_PathID']];head=o.parse_monobehaviour_head();go=head.m_GameObject.deref_parse_as_object();d=o.read_typetree()
  sr=next(c.component.deref_parse_as_object() for c in go.m_Component if c.component.deref().type.name=='SpriteRenderer')
  sid=d['sprite']['m_PathID']
  if sid:sprite(sid)
  children=next(c.component.deref_parse_as_object() for c in go.m_Component if c.component.deref().type.name=='Transform').m_Children
  hierarchy=node(go,True);hierarchy['sprite']=sid;hierarchy['active']=True
  catalog[kind].append({'id':index,'name':go.m_Name,'sprite':sid,'scale':d['scale'],'offset':d['offset'],'order':sr.m_SortingOrder,'color':{k:getattr(sr.m_Color,k) for k in 'rgba'},'healthBarUp':d['moveHealthBarUp'],'node':hierarchy})
for id in [1979,1981,2076,2375]:sprite(id)
# Preserve the exact tree using child indices; recursive field types exceed Unity's serializer depth limit.
catalog['nodes']=[]
def flatten(n):
 children=n['children'];n['children']=[]
 for child in children:
  index=len(catalog['nodes']);catalog['nodes'].append(child);n['children'].append(index);flatten(child)
for kind in ['eyes','mouths','accessories']:
 for item in catalog[kind]:flatten(item['node'])
(OUT/'catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2)+'\n')
evidence={'source':'${ROUNDS_DATA}','files':{n:hashlib.sha256((SOURCE/n).read_bytes()).hexdigest() for n in ['sharedassets0.assets','level0','Managed/Assembly-CSharp.dll']},'counts':{k:len(catalog[k]) for k in ['eyes','mouths','accessories']},'sprite_count':len(exported),'method':'Native MonoBehaviour header resolves script/game object; generated type tree fields follow byte 32. Original trimmed sprite restored in its original rectangle with textureRectOffset; original pivot and pixels-per-unit retained.'}
(ROOT/'ReverseAnalysis/evidence/character-art-extraction.json').write_text(json.dumps(evidence,indent=2)+'\n')
for kind in ['eyes','mouths','accessories']:
 sheet=Image.new('RGB',(1000,((len(catalog[kind])+9)//10)*112),(100,105,115));draw=ImageDraw.Draw(sheet)
 for i,it in enumerate(catalog[kind]):
  if it['sprite']:
   im=Image.open(OUT/(str(it['sprite'])+'.png'));im.thumbnail((90,85));sheet.paste(im,(i%10*100+(100-im.width)//2,i//10*112),im)
  draw.text((i%10*100+5,i//10*112+88),str(i),fill='white')
 sheet.save(ROOT/('ReverseAnalysis/evidence/character-'+kind+'.png'))
print(json.dumps(evidence,indent=2))
