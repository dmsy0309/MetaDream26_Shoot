using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Player;

namespace Metadream.Shoot.Tests
{
    public class CombatRulesTests
    {
        [Test]
        public void BlockWindowIsStrictlyBelowPointThree()
        {
            Assert.IsTrue(CombatRules.IsBlocking(0.299f));
            Assert.IsFalse(CombatRules.IsBlocking(0.300f));
            Assert.IsFalse(CombatRules.IsBlocking(0.301f));
        }

        [Test]
        public void BlockCooldownComposition()
        {
            Assert.AreEqual(2.5f, CombatRules.EffectiveBlockCooldown(4f, 1f, 0.5f), 1e-5f);
        }

        [Test]
        public void ReloadTimeComposition()
        {
            Assert.AreEqual(1.0f, CombatRules.EffectiveReloadTime(1.5f, 0.5f, 0.5f), 1e-5f);
        }

        [Test]
        public void DeathBranchIsStrictlyBelowZero()
        {
            var positive = CombatRules.ApplyDamage(100f, 55f);
            Assert.AreEqual(45f, positive.Health, 1e-4f);
            Assert.IsFalse(positive.Died);
            var zero = CombatRules.ApplyDamage(55f, 55f);
            Assert.AreEqual(0f, zero.Health, 1e-6f);
            Assert.IsFalse(zero.Died, "health == 0 does not take the < 0 death branch");
            Assert.IsTrue(CombatRules.ApplyDamage(45f, 55f).Died);
            Assert.AreEqual(90f, CombatRules.ApplyDamage(100f, -10f).Health, 1e-4f, "damage magnitude is used");
        }

        [Test]
        public void AttackGateAndCount()
        {
            Assert.IsFalse(CombatRules.CanAttack(0.1f, 0.3f, false, false));
            Assert.IsTrue(CombatRules.CanAttack(0.3f, 0.3f, false, false));
            Assert.IsFalse(CombatRules.CanAttack(1f, 0.3f, true, false));
            Assert.IsTrue(CombatRules.CanAttack(0f, 0.3f, true, true));
            Assert.AreEqual(1, CombatRules.AttackCount(0f, 0.3f, false));
            Assert.AreEqual(10, CombatRules.AttackCount(100f, 0.3f, false));
            Assert.AreEqual(1, CombatRules.AttackCount(100f, 0.3f, true));
        }

        [Test]
        public void AmmoConsumesPerProjectileAndReloads()
        {
            var a = new AmmoState { maxAmmo = 3 };
            a.Refill();
            a.Shoot(1.5f);
            a.Shoot(1.5f);
            Assert.IsFalse(a.isReloading);
            a.Shoot(1.5f);
            Assert.IsTrue(a.isReloading);
            Assert.AreEqual(0, a.currentAmmo);
            Assert.IsFalse(a.Tick(1.4f, 1.5f, true));
            Assert.IsTrue(a.Tick(0.2f, 1.5f, true));
            Assert.AreEqual(3, a.currentAmmo);
            Assert.IsFalse(a.isReloading);
        }

        [Test]
        public void PartialMagazineAutoRefillsAfterReloadTime()
        {
            var a = new AmmoState { maxAmmo = 3 };
            a.Refill();
            a.Shoot(1.5f);
            Assert.IsFalse(a.Tick(1.5f, 1.5f, true), "requires counter > reloadTime");
            Assert.IsTrue(a.Tick(0.01f, 1.5f, true));
            Assert.AreEqual(3, a.currentAmmo);
            var b = new AmmoState { maxAmmo = 3 };
            b.Refill();
            b.Shoot(1.5f);
            Assert.IsFalse(b.Tick(5f, 1.5f, false), "no free reload without automaticReload");
        }

        [Test]
        public void BlockStateCooldownAndWindow()
        {
            var b = new BlockState();
            Assert.IsTrue(b.TryBlock(4f));
            Assert.IsTrue(b.IsBlocking);
            Assert.IsFalse(b.TryBlock(4f));
            b.Tick(0.31f);
            Assert.IsFalse(b.IsBlocking);
            Assert.IsFalse(b.TryBlockMe());
            b.Tick(3.7f);
            Assert.IsTrue(b.TryBlock(4f));
        }

        [Test]
        public void MotionFormulas()
        {
            // Move: (1,0) * force * 0.01 * mult * 0.02
            var d = MotionModel.MoveDelta(new Vector2(1f, 0f), 5000f, 1f, 0f, 1f);
            Assert.AreEqual(1f, d.x, 1e-5f);
            // Up input is clamped away; down input is doubled.
            var down = MotionModel.MoveDelta(new Vector2(0f, -1f), 5000f, 1f, 0f, 1f);
            Assert.AreEqual(-2f, down.y, 1e-5f);
            Assert.AreEqual(0f, MotionModel.MoveDelta(new Vector2(0f, 1f), 5000f, 1f, 0f, 1f).y, 1e-6f);
            Assert.AreEqual(0.8f, MotionModel.Multiplier(false), 1e-6f);
            Assert.AreEqual(0.4f, MotionModel.BaseGravityDelta(0.02f).magnitude, 1e-5f);
            Assert.AreEqual(22f, MotionModel.JumpImpulse(Vector2.up, 1f, 0f, 2200f).y, 1e-4f);
            var wall = MotionModel.WallJumpDirection(Vector2.right);
            Assert.AreEqual(0.4f, wall.x, 1e-6f);
            Assert.AreEqual(0.8f, wall.y, 1e-6f);
            Assert.AreEqual(-0.5f, MotionModel.SinceGroundedAfterForce(0f, 1000f), 1e-6f);
            // Air gravity uses min(sinceGrounded, sinceWallGrab).
            var g = MotionModel.AirGravityDelta(2f, 1f, 10f, 2f);
            Assert.AreEqual(-0.2f, g.y, 1e-5f);
        }

        [Test]
        public void UpwardsCompensationIgnoresHeight()
        {
            float a = CombatRules.UpwardsCompensation(new Vector2(0, 0), new Vector2(10, 0), 30f, 70f);
            float b = CombatRules.UpwardsCompensation(new Vector2(0, 5), new Vector2(10, -3), 30f, 70f);
            Assert.AreEqual(a, b, 1e-5f);
            Assert.AreEqual(Mathf.Pow(10f, 2.06f) * 30f / 70f * 0.012f, a, 1e-4f);
        }
    }
}

