using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Network;

namespace Metadream.Shoot.Tests
{
    public class NetworkTests
    {
        [Test]
        public void PlayerPackageRoundTripsInObservedOrder()
        {
            var p = new PlayerSyncPackage
            {
                Position = new Vector2(1, 2), Direction = new Vector2(-1, 0), AimDirection = new Vector2(0.6f, 0.8f),
                JumpIsPressed = true, Velocity = new Vector2(3, -4), DidJump = true, SinceGrounded = 0.25f, TimeDelta = 0.5f
            };
            var bytes = SyncCodec.Encode(p);
            Assert.AreEqual(3 * 8 + 1 + 8 + 1 + 4 + 4, bytes.Length);
            Assert.AreEqual(1f, System.BitConverter.ToSingle(bytes, 0), "position.x comes first");
            Assert.AreEqual(1, bytes[24], "jumpIsPressed follows the three vectors");
            var q = SyncCodec.DecodePlayer(bytes);
            Assert.AreEqual(p.Position, q.Position);
            Assert.AreEqual(p.AimDirection, q.AimDirection);
            Assert.AreEqual(p.Velocity, q.Velocity);
            Assert.IsTrue(q.DidJump);
            Assert.AreEqual(0.1f, q.ClampedTimeDelta, 1e-6f);
            q.TimeDelta = -1f;
            Assert.AreEqual(0f, q.ClampedTimeDelta);
        }

        [Test]
        public void LocalTransportDeliversOnPollAndDedupesEvents()
        {
            LocalTransport.CreatePair(out var a, out var b);
            int received = 0;
            var dedupe = new EventDeduplicator();
            b.Received += (ch, data) =>
            {
                var e = GameEvent.FromBytes(data);
                if (dedupe.Accept(e)) received++;
            };
            var ev = new GameEvent { Type = GameEventType.PointResult, SenderId = 0, Sequence = 7, IntArg = 1 };
            a.Send(NetworkChannel.Event, ev.ToBytes());
            a.Send(NetworkChannel.Event, ev.ToBytes());
            Assert.AreEqual(0, received, "nothing delivered before Poll");
            b.Poll();
            Assert.AreEqual(1, received, "re-delivered event ignored");
            a.Disconnect();
            Assert.IsFalse(a.IsConnected);
            Assert.IsFalse(b.IsConnected);
        }
    }
}

