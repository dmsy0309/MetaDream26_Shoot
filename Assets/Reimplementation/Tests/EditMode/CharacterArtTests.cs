using System.IO;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Player;
using Metadream.Shoot.Network;
namespace Metadream.Shoot.Tests
{
    public class CharacterArtTests
    {
        [Test] public void CompleteOriginalCatalogHasKoreanNamesAndStableExtractedIds()
        {
            var d=OriginalCharacterArt.Data;
            Assert.AreEqual(85,d.eyes.Length);Assert.AreEqual(58,d.mouths.Length);Assert.AreEqual(54,d.accessories.Length);
            Assert.AreEqual(246,d.sprites.Length);
            Assert.AreEqual(162,d.nodes.Length);
            foreach(var node in d.nodes)foreach(var child in node.children)Assert.That(child,Is.InRange(0,d.nodes.Length-1));
            Assert.AreEqual(85,CharacterLook.ExpressionNames.Length);Assert.AreEqual(58,CharacterLook.MouthNames.Length);Assert.AreEqual(54,CharacterLook.AccessoryNames.Length);
            foreach(var names in new[]{CharacterLook.ExpressionNames,CharacterLook.MouthNames,CharacterLook.AccessoryNames})
            {
                var unique=new System.Collections.Generic.HashSet<string>();
                foreach(var name in names)
                {
                    Assert.IsTrue(unique.Add(name),name);
                    Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(name,@"[\uAC00-\uD7A3]"),name);
                    Assert.IsFalse(name.Contains("ROUNDS"));
                }
            }
            Assert.IsNull(OriginalCharacterArt.Resolve(1,1));
            for(int i=0;i<100;i++)
            {
                var look=CharacterLook.Random(new System.Random(i));
                Assert.AreEqual(0,look.Hat);Assert.AreEqual(look,look.Sanitized);
                Assert.IsNotNull(OriginalCharacterArt.Resolve(1,look.Expression));
                Assert.IsNotNull(OriginalCharacterArt.Resolve(3,look.Mouth));
                Assert.IsNotNull(OriginalCharacterArt.Resolve(2,look.Accessory));
                Assert.IsNotNull(OriginalCharacterArt.Resolve(4,look.Accessory2));
            }
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
