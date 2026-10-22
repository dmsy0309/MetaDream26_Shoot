using System.IO;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Player;
using Metadream.Shoot.Network;
using Metadream.Shoot.Online;

namespace Metadream.Shoot.Tests
{
    public class ParityAndChatTests
    {
        [Test] public void ChatIsBoundedPrintableAndPreservesKorean()
        {
            Assert.AreEqual("안녕 모두",ChatRules.Clean("  안녕\n 모두\u202e  "));
            Assert.AreEqual(160,ChatRules.Clean(new string('가',1000)).Length);
            var rate = new ChatRateLimit();
            Assert.IsTrue(rate.Allow(0,0)); Assert.IsFalse(rate.Allow(0,0.2f));
            Assert.IsTrue(rate.Allow(1,0.2f)); Assert.IsTrue(rate.Allow(0,0.8f));
            var h = new ChatHistory(); for(int i=0;i<100;i++)h.Add(new ChatEntry{Text=i.ToString()});
            Assert.AreEqual(64,h.Entries.Count); Assert.AreEqual("36",h.Entries[0].Text);
        }
        [Test] public void PointerSelectionSurvivesNetworkAccumulationExactlyOnce()
        {
            var data=Protocol.Pack(MsgType.Input,w=>Protocol.WriteInput(w,new InputFrame{SelectCard=4,ConfirmPressed=true}));
            var n=new NetworkInput("remote");
            using(var r=Protocol.Body(data)) n.Push(Protocol.ReadInput(r));
            n.Push(default);
            var first=n.Read(default); Assert.AreEqual(4,first.SelectCard);Assert.IsTrue(first.ConfirmPressed);
            var next=n.Read(default);Assert.AreEqual(0,next.SelectCard);Assert.IsFalse(next.ConfirmPressed);
        }
        [Test] public void NewFacePartsRoundTripAndOldReplayStillReads()
        {
            var look=new CharacterLook(0,76,19){Mouth=40,Accessory2=45,EyeOffset=new Vector2(.2f,-.1f),MouthOffset=new Vector2(0,.15f)};
            var s=new MatchSnapshot();s.Looks.Add(look);
            var ms=new MemoryStream();s.Write(new BinaryWriter(ms));var bytes=ms.ToArray();
            var copy=new MatchSnapshot();copy.Read(new BinaryReader(new MemoryStream(bytes)));
            Assert.AreEqual(look,copy.Looks[0]);
            // Details are appended after the exact v9 layout, 34 bytes per face.
            var old=new MatchSnapshot();old.Read(new BinaryReader(new MemoryStream(bytes,0,bytes.Length-34)));
            Assert.AreEqual(new CharacterLook(0,76,19),old.Looks[0]);
            look.EyeOffset=new Vector2(float.NaN,999);look.Mouth=255;
            Assert.AreEqual(new Vector2(0,.25f),look.Sanitized.EyeOffset);Assert.AreEqual(OriginalCharacterArt.MouthStart,look.Sanitized.Mouth);
        }
        [Test] public void HeldJumpHasOriginalShortBoostWindow()
        {
            Assert.Greater(MotionModel.HeldJumpDelta(true,.1f,.02f,1,2200).y,0);
            Assert.AreEqual(Vector2.zero,MotionModel.HeldJumpDelta(false,.1f,.02f,1,2200));
            Assert.AreEqual(Vector2.zero,MotionModel.HeldJumpDelta(true,.2f,.02f,1,2200));
            var a=MotionModel.HeldJumpDelta(true,.1f,.01f,1,2200);
            Assert.AreEqual(a*2,MotionModel.HeldJumpDelta(true,.1f,.02f,1,2200));
        }
    }
}
