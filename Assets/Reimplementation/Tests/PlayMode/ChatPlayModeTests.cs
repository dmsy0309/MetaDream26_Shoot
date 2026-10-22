using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metadream.Shoot.Network;
using Metadream.Shoot.Online;
using Metadream.Shoot.Player;

namespace Metadream.Shoot.Tests
{
    public class ChatPlayModeTests
    {
        private TcpClientConnection _a, _b;
        [TearDown] public void Cleanup() { _a?.Close(); _b?.Close(); NetHostSession.Active?.Close(); }
        private IEnumerator Connect(TcpClientConnection c, int port, string name)
        {
            c.Connect("127.0.0.1",port);
            float end=Time.realtimeSinceStartup+5;
            while(c.State!=TcpClientConnection.ConnState.Connected && Time.realtimeSinceStartup<end) { c.Poll(new List<byte[]>()); yield return null; }
            Assert.AreEqual(TcpClientConnection.ConnState.Connected,c.State);
            c.Send(Protocol.Pack(MsgType.Hello,w=>{
                w.Write(Protocol.Version);w.Write(Content.ContentDatabase.Hash??"");w.Write(name);w.Write(name+"-device");
                w.Write((byte)0);w.Write((byte)0);w.Write((byte)0);CharacterLook.Default.WriteDetails(w);
            }));
            var messages=new List<byte[]>(); bool welcomed=false;
            while(!welcomed && Time.realtimeSinceStartup<end)
            { c.Poll(messages); foreach(var m in messages) if(Protocol.Peek(m)==MsgType.Welcome)welcomed=true; messages.Clear(); yield return null; }
            Assert.IsTrue(welcomed);
        }
        [UnityTest] public IEnumerator DedicatedServerRelaysAuthenticatedKoreanChatToBothPeers()
        {
            var host=NetHostSession.Open(new HostOptions{Dedicated=true,Port=18980,MasterServer=false,LanBroadcast=false,SaveReplays=false,UploadReplays=false},out string error);
            Assert.IsNotNull(host,error);_a=new TcpClientConnection();_b=new TcpClientConnection();
            yield return Connect(_a,host.Port,"보낸이");yield return Connect(_b,host.Port,"받는이");
            _a.Send(Protocol.Pack(MsgType.ChatSubmit,w=>w.Write("한글 채팅 <b>그대로</b>")));
            var messages=new List<byte[]>();bool received=false;float end=Time.realtimeSinceStartup+5;
            while(!received && Time.realtimeSinceStartup<end)
            { _b.Poll(messages); foreach(var m in messages) if(Protocol.Peek(m)==MsgType.ChatMessage)
              {using(var r=Protocol.Body(m)){var e=ChatEntry.Read(r);Assert.AreEqual("보낸이",e.Name);Assert.AreEqual("한글 채팅 <b>그대로</b>",e.Text);received=true;}} messages.Clear();yield return null; }
            Assert.IsTrue(received);Assert.AreEqual(1,host.Chat.Entries.Count);
            bool echo=false;_a.Poll(messages);foreach(var m in messages)echo|=Protocol.Peek(m)==MsgType.ChatMessage;Assert.IsTrue(echo);
        }
    }
}
