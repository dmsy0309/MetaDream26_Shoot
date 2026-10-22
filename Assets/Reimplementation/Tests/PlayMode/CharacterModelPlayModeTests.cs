using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metadream.Shoot.Player;
namespace Metadream.Shoot.Tests
{
    public class CharacterModelPlayModeTests
    {
        private GameObject _root;
        [TearDown] public void Cleanup(){if(_root!=null)Object.Destroy(_root);CharacterPreview.Hide();}
        [UnityTest] public IEnumerator RigTracksAmmoAndAimWithoutAddingPhysics()
        {
            _root=new GameObject("VisualRigTest");var rig=new CharacterModel(_root.transform,Color.red);
            rig.Tick(Vector2.right,Vector2.zero,true,false,3,3,false,1,1,false,Color.red,true);
            Assert.Greater(rig.GunHand.localPosition.x,0);Assert.Less(rig.ShieldHand.localPosition.x,0);
            Assert.Greater(rig.AmmoPosition.y,rig.GunHand.position.y);
            rig.Tick(Vector2.left,Vector2.up,false,false,1,7,true,.4f,.3f,false,Color.red,true);
            Assert.AreEqual(1,rig.VisibleAmmo);Assert.AreEqual("1 / 7",_root.GetComponentInChildren<TextMesh>().text);
            Assert.Less(rig.GunHand.localPosition.x,0);Assert.Greater(rig.ShieldHand.localPosition.x,0);
            Assert.IsEmpty(_root.GetComponentsInChildren<Collider2D>());Assert.IsEmpty(_root.GetComponentsInChildren<Rigidbody2D>());
            Assert.AreEqual(10,_root.transform.Find("LeftArm").GetComponentsInChildren<SpriteRenderer>().Length-11);
            rig.SetVisible(false);Assert.IsFalse(_root.GetComponentInChildren<TextMesh>().GetComponent<MeshRenderer>().enabled);
            yield return null;
        }
        [UnityTest] public IEnumerator All197OriginalPartsInstantiateAndPreviewRenders()
        {
            _root=new GameObject("AllOriginalParts");
            foreach(var group in new[]{OriginalCharacterArt.Data.eyes,OriginalCharacterArt.Data.mouths,OriginalCharacterArt.Data.accessories})
                foreach(var item in group)
                {
                    var part=OriginalCharacterArt.Create(item,_root.transform,Vector2.zero,true);
                    foreach(var sr in part.GetComponentsInChildren<SpriteRenderer>(true))Assert.IsNotNull(sr.sprite,item.name);
                    yield return null;Object.Destroy(part);
                }
            Directory.CreateDirectory("Logs/character-previews");
            var looks=new[]{CharacterLook.Default,new CharacterLook(0,76,19){Mouth=40,Accessory2=45},new CharacterLook(0,90,36){Mouth=61},new CharacterLook(0,8,14){Mouth=8,Accessory2=16}};
            for(int i=0;i<looks.Length;i++)
            {
                CharacterPreview.Show(looks[i],new Color(.95f,.48f,.25f),new Vector2(1,.1f));
                yield return null;yield return null;
                var rt=CharacterPreview.Texture;Assert.IsNotNull(rt);var prior=RenderTexture.active;RenderTexture.active=rt;
                var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=prior;
                File.WriteAllBytes("Logs/character-previews/look-"+i+".png",image.EncodeToPNG());Object.Destroy(image);
            }
        }
        [UnityTest] public IEnumerator PreviewAmmoRemainsInsideFrameWhenAimingUp()
        {
            CharacterPreview.Show(CharacterLook.Default,Color.yellow,Vector2.up);
            yield return null;yield return null;
            var preview=Object.FindFirstObjectByType<CharacterPreview>();
            var camera=preview.GetComponentInChildren<Camera>();
            var bounds=preview.GetComponentInChildren<TextMesh>().GetComponent<MeshRenderer>().bounds;
            var lower=camera.WorldToViewportPoint(bounds.min);var upper=camera.WorldToViewportPoint(bounds.max);
            Assert.That(lower.x,Is.InRange(0f,1f));Assert.That(lower.y,Is.InRange(0f,1f));
            Assert.That(upper.x,Is.InRange(0f,1f));Assert.That(upper.y,Is.InRange(0f,1f));
        }
    }
}
