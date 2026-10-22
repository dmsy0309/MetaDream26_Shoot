using UnityEngine;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Player
{
    /// <summary>Presentation-only rig. Four curved ten-segment limbs, gun hand and inactive shield hand.
    /// Does not add colliders or forces, or change the authoritative projectile origin.</summary>
    public sealed class CharacterModel
    {
        private readonly Transform _root, _gun, _shield, _ammoRoot;
        private readonly Limb _leftArm, _rightArm, _leftLeg, _rightLeg;
        private readonly SpriteRenderer _shieldDisc, _shieldEdge, _gunBarrel, _gunGrip;
        private readonly SpriteRenderer[] _bullets = new SpriteRenderer[99];
        private readonly LineRenderer _reloadArc;
        private readonly TextMesh _ammoText;
        private readonly MeshRenderer _ammoRenderer;
        private readonly RaycastHit2D[] _groundHits = new RaycastHit2D[16];
        private float _step, _kick;
        private int _lastAmmo = -1, _lastMax = -1;
        private Color _color;
        public Transform GunHand { get; }
        public Transform ShieldHand { get; }
        public SpriteRenderer Body { get; }
        public SpriteRenderer Outline { get; }
        public CharacterFace Face { get; }
        public int VisibleAmmo { get; private set; }
        public Vector3 AmmoPosition => _ammoRoot.position;

        public CharacterModel(Transform root, Color color, SpriteRenderer body = null)
        {
            _root = root; _color = color;
            Body = body != null ? body : Make(root, "Body", OriginalCharacterArt.GetSprite(1979), Vector2.zero, Vector2.one / 6.4f, color, 10);
            Body.sprite = OriginalCharacterArt.GetSprite(1979); Body.color = color;
            // A root SpriteRenderer must retain unit bounds because Fighter owns its physical scale.
            if (body != null) Body.sprite = UnitBody();
            Body.sharedMaterial = SpriteFactory.Material; Body.sortingOrder = 10;
            Outline = Make(root, "BodyOutline", OriginalCharacterArt.GetSprite(2076), Vector2.zero, Vector2.one * 0.1651f, Dark(color), 9);
            _leftArm = new Limb(root, "LeftArm", 8); _rightArm = new Limb(root, "RightArm", 8);
            _leftLeg = new Limb(root, "LeftLeg", 8); _rightLeg = new Limb(root, "RightLeg", 8);
            GunHand = Child(root, "RightHand_Gun"); ShieldHand = Child(root, "LeftHand_Shield");
            _gun = Child(GunHand, "Gun");
            // WeaponBase's up-facing barrel/handle transformed into the right-facing aim frame.
            _gunBarrel = Make(_gun, "Barrel", SpriteFactory.Square, new Vector2(0.4f,0.15f), new Vector2(0.640386f,0.150281f), Dark(color), 12);
            _gunGrip = Make(_gun, "Grip", SpriteFactory.Square, new Vector2(0.12f,-0.01f), new Vector2(0.352469f,0.150281f), Dark(color), 12);
            _gunGrip.transform.localRotation = Quaternion.Euler(0,0,-105);
            Make(GunHand,"Fingers",SpriteFactory.Circle,Vector2.zero,Vector2.one*0.14f,color,13);
            _shield = Child(ShieldHand,"ShieldStone");
            _shieldDisc = Make(_shield,"Shield",OriginalCharacterArt.GetSprite(1979),Vector2.zero,Vector2.one*0.05f,Color.white,12);
            _shieldEdge = Make(_shield,"ShieldRecharge",SpriteFactory.Ring,Vector2.zero,Vector2.one*0.38f,Color.white,13);
            _ammoRoot = Child(root,"GunAmmo");
            for (int i=0;i<_bullets.Length;i++)
            {
                _bullets[i]=Make(_ammoRoot,"Bullet"+i,SpriteFactory.Circle,Vector2.zero,Vector2.one*0.095f,Color.white,22);
                _bullets[i].gameObject.SetActive(false);
            }
            var count = Child(_ammoRoot,"AmmoCount"); count.localPosition = new Vector3(0,0.16f,0);
            _ammoText = count.gameObject.AddComponent<TextMesh>();
            _ammoText.font = Resources.Load<Font>("Fonts/NotoSansKR-Bold");
            _ammoText.fontSize=40; _ammoText.characterSize=0.035f; _ammoText.anchor=TextAnchor.MiddleCenter; _ammoText.alignment=TextAlignment.Center;
            _ammoRenderer = count.GetComponent<MeshRenderer>(); _ammoRenderer.sharedMaterial=_ammoText.font.material; _ammoRenderer.sortingOrder=23;
            var ring = Child(_ammoRoot,"ReloadProgress"); _reloadArc=ring.gameObject.AddComponent<LineRenderer>();
            _reloadArc.useWorldSpace=false; _reloadArc.sharedMaterial=SpriteFactory.Material; _reloadArc.startWidth=_reloadArc.endWidth=0.035f;
            _reloadArc.sortingOrder=24; _reloadArc.numCapVertices=3; _reloadArc.startColor=_reloadArc.endColor=Color.white;
            Face = new CharacterFace(root,color);
        }
        private static Sprite _unitBody;
        private static Sprite UnitBody()
        {
            if (_unitBody != null) return _unitBody;
            var s=OriginalCharacterArt.GetSprite(1979);
            return _unitBody=Sprite.Create(s.texture,s.rect,s.pivot/s.rect.size,s.rect.width,0,SpriteMeshType.FullRect);
        }
        public void SetVisible(bool visible)
        {
            _ammoRenderer.enabled=visible; _reloadArc.enabled=visible;
            Face.SetVisible(visible);
        }
        public void Tick(Vector2 aim, Vector2 velocity, bool grounded, bool dead, int ammo, int maxAmmo, bool reloading, float reload, float blockReady, bool blocking, Color tint, bool preview=false)
        {
            aim=aim.sqrMagnitude>0.001f?aim.normalized:Vector2.right;
            float dt=Time.unscaledDeltaTime, side=aim.x<0?-1:1;
            _step += dt * Mathf.Min(14,Mathf.Abs(velocity.x)*1.6f);
            if (_lastAmmo>=0 && ammo<_lastAmmo) _kick=0.13f;
            _kick=Mathf.MoveTowards(_kick,0,dt*1.2f);
            Vector2 gunHand=aim*(0.65f-_kick)+Vector2.down*0.08f;
            Vector2 shieldHand=new Vector2(-side*0.85f,-0.26f+(blocking?0.55f:0)) - Vector2.ClampMagnitude(velocity*0.008f,0.12f);
            GunHand.localPosition=gunHand; ShieldHand.localPosition=shieldHand;
            _gun.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(aim.y,aim.x)*Mathf.Rad2Deg);
            _gun.localScale=new Vector3(1,side,1);
            _leftArm.Draw(new Vector2(-0.38f,0),side>0?shieldHand:gunHand,new Vector2(-0.6f,-0.52f),Dark(tint));
            _rightArm.Draw(new Vector2(0.38f,0),side>0?gunHand:shieldHand,new Vector2(0.6f,-0.52f),Dark(tint));
            DrawLeg(_leftLeg,-1,velocity,grounded,dead,preview,tint);
            DrawLeg(_rightLeg,1,velocity,grounded,dead,preview,tint);
            _shieldDisc.color=Color.Lerp(new Color(0.25f,0.25f,0.25f),Color.white,blockReady);
            _shield.localScale=Vector3.one*(blocking?1.55f:1);
            _shieldEdge.color=new Color(1,1,1,blocking?1:0.15f+0.45f*blockReady);
            _gunBarrel.color=_gunGrip.color=Dark(tint); Outline.color=Dark(tint);
            _ammoRoot.localPosition=(Vector3)gunHand + new Vector3(aim.x*0.3f,0.54f+Mathf.Max(0,aim.y*0.5f),0);
            maxAmmo=Mathf.Clamp(maxAmmo,1,99);ammo=Mathf.Clamp(ammo,0,maxAmmo);VisibleAmmo=ammo;
            if (ammo!=_lastAmmo || maxAmmo!=_lastMax)
            {
                _ammoText.text=ammo+" / "+maxAmmo;
                float spacing=Mathf.Min(0.15f,1.7f/maxAmmo),size=Mathf.Min(0.095f,spacing*0.7f);
                for(int i=0;i<_bullets.Length;i++)
                {
                    _bullets[i].gameObject.SetActive(i<maxAmmo);
                    _bullets[i].transform.localPosition=new Vector3((i-(maxAmmo-1)*0.5f)*spacing,0,0);
                    _bullets[i].transform.localScale=Vector3.one*size;
                    _bullets[i].color=i<ammo?Color.white:new Color(1,1,1,0.12f);
                }
                _lastAmmo=ammo;_lastMax=maxAmmo;
            }
            _ammoRenderer.enabled=Body.enabled;
            _reloadArc.enabled=Body.enabled && reloading;
            if(reloading)
            {
                _reloadArc.positionCount=33;
                for(int i=0;i<33;i++) {float a=(90-i/32f*Mathf.Clamp01(reload)*360)*Mathf.Deg2Rad;_reloadArc.SetPosition(i,new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*0.15f);}
            }
            Face.Tick(aim,dead);Face.TintCovers(tint);
        }
        private void DrawLeg(Limb limb,float side,Vector2 velocity,bool grounded,bool dead,bool preview,Color tint)
        {
            float stride=Mathf.Sin(_step+(side<0?0:Mathf.PI))*Mathf.Clamp01(Mathf.Abs(velocity.x)/3);
            var hip=new Vector2(side*0.2f,-0.3f);
            var foot=new Vector2(side*0.33f+stride*0.28f,preview?-0.92f:-0.96f);
            if(!preview && !dead)
            {
                Vector2 origin=_root.TransformPoint(new Vector3(foot.x,0,0));
                int count=Physics2D.Raycast(origin,Vector2.down,new ContactFilter2D().NoFilter(),_groundHits,1.3f*_root.lossyScale.y);
                float nearest=float.MaxValue;
                for(int i=0;i<count;i++)
                {
                    var hit=_groundHits[i];
                    if(hit.collider.isTrigger || hit.collider.GetComponentInParent<Fighter>()!=null || hit.collider.GetComponent<Combat.Projectile>()!=null)continue;
                    if(hit.distance<nearest){nearest=hit.distance;foot=(Vector2)_root.InverseTransformPoint(hit.point)+Vector2.up*0.025f;}
                }
                if(nearest<float.MaxValue && grounded)foot.y+=Mathf.Max(0,stride)*0.16f;
                else foot.x-=Mathf.Clamp(velocity.x*0.015f,-0.2f,0.2f);
            }
            limb.Draw(hip,foot,new Vector2(side*0.48f,-0.45f),Dark(tint));
        }
        private static Color Dark(Color c)=>Color.Lerp(c,new Color(0.12f,0.12f,0.14f),0.48f);
        private static Transform Child(Transform parent,string name){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        private static SpriteRenderer Make(Transform parent,string name,Sprite sprite,Vector2 p,Vector2 size,Color color,int order)
        {
            var t=Child(parent,name);t.localPosition=p;t.localScale=new Vector3(size.x,size.y,1);
            var sr=t.gameObject.AddComponent<SpriteRenderer>();sr.sprite=sprite;sr.sharedMaterial=SpriteFactory.Material;sr.color=color;sr.sortingOrder=order;return sr;
        }
        private sealed class Limb
        {
            private readonly SpriteRenderer[] _links=new SpriteRenderer[10],_joints=new SpriteRenderer[11];
            public Limb(Transform parent,string name,int order)
            {
                var root=Child(parent,name);
                for(int i=0;i<10;i++)_links[i]=Make(root,"Segment"+i,SpriteFactory.Square,Vector2.zero,Vector2.one,Color.white,order);
                for(int i=0;i<11;i++)_joints[i]=Make(root,"Joint"+i,SpriteFactory.Circle,Vector2.zero,Vector2.one*0.1f,Color.white,order);
            }
            public void Draw(Vector2 start,Vector2 end,Vector2 bend,Color color)
            {
                Vector2 previous=start;
                for(int i=0;i<=10;i++)
                {
                    float t=i/10f;Vector2 p=(1-t)*(1-t)*start+2*(1-t)*t*bend+t*t*end;
                    _joints[i].transform.localPosition=p;_joints[i].color=color;
                    if(i>0){Vector2 d=p-previous;var s=_links[i-1];s.transform.localPosition=(p+previous)*0.5f;s.transform.localScale=new Vector3(d.magnitude,0.1f,1);s.transform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg);s.color=color;}
                    previous=p;
                }
            }
        }
    }
}
