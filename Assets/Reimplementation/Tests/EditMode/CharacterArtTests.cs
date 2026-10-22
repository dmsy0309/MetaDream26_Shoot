using System.IO;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Player;
using Metadream.Shoot.Network;
namespace Metadream.Shoot.Tests
{
    public class CharacterArtTests
    {
        [Test] public void CompleteOriginalCatalogAndLegacyIndicesArePreserved()
        {
            var d=OriginalCharacterArt.Data;
            Assert.AreEqual(85,d.eyes.Length);Assert.AreEqual(58,d.mouths.Length);Assert.AreEqual(54,d.accessories.Length);
            Assert.AreEqual(246,d.sprites.Length);
            Assert.AreEqual(162,d.nodes.Length);
            foreach(var node in d.nodes)foreach(var child in node.children)Assert.That(child,Is.InRange(0,d.nodes.Length-1));
            Assert.AreEqual("화남",CharacterLook.ExpressionNames[1]);Assert.AreEqual("안경",CharacterLook.AccessoryNames[2]);
            Assert.AreEqual("왕관",CharacterLook.HatNames[1]);
            foreach(var info in d.sprites)
            {
                var s=OriginalCharacterArt.GetSprite(info.id);
                Assert.AreEqual(info.width,s.texture.width,info.name);Assert.AreEqual(info.height,s.texture.height,info.name);
                Assert.AreEqual(info.ppu,s.pixelsPerUnit);
            }
        }
        [Test] public void LastOriginalPartsAndOffsetsRoundTripInExistingSnapshot()
        {
            var look=new CharacterLook(0,92,60){Mouth=65,Accessory2=59,EyeOffset=new Vector2(.1f,-.2f)};
            var snapshot=new MatchSnapshot();snapshot.Looks.Add(look);
            var bytes=new MemoryStream();snapshot.Write(new BinaryWriter(bytes));bytes.Position=0;
            var decoded=new MatchSnapshot();decoded.Read(new BinaryReader(bytes));Assert.AreEqual(look,decoded.Looks[0]);
            Assert.AreEqual(84,OriginalCharacterArt.Resolve(1,92).id);
            Assert.AreEqual(57,OriginalCharacterArt.Resolve(3,65).id);
            Assert.AreEqual(53,OriginalCharacterArt.Resolve(2,60).id);
        }
        [Test] public void OriginalAnimationCoefficientsInterpolateAndSwitchAtKeyBoundary()
        {
            var keys=new[]{new OriginalCharacterArt.Key{time=0,a=1,b=2,c=3,d=4},new OriginalCharacterArt.Key{time=2,d=20}};
            Assert.AreEqual(4,OriginalCharacterArt.Evaluate(keys,0));Assert.AreEqual(10,OriginalCharacterArt.Evaluate(keys,1));
            Assert.AreEqual(20,OriginalCharacterArt.Evaluate(keys,2));
        }
    }
}
