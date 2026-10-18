using UnityEngine;

namespace Metadream.Shoot.Player
{
    /// <summary>
    /// Player motion formulas recovered from CIL. All per-FixedUpdate. Mass cancels out wherever the
    /// original multiplies by mass and then PlayerVelocity.AddForce divides by it.
    /// </summary>
    public static class MotionModel
    {
        /// <summary>PlayerVelocity.AddForce (0x060008d1): Force mode multiplies by 0.02; Impulse by 1.</summary>
        public const float ForceModeScale = 0.02f;
        /// <summary>PlayerVelocity.FixedUpdate (0x060008d0): velocity += down * fixedDeltaTime * timeScale * 20.</summary>
        public const float BaseGravity = 20f;
        /// <summary>PlayerMovement..ctor airControl = 0.3; UpdateMultiplier uses it while airborne.</summary>
        public const float AirControl = 0.3f;
        /// <summary>PlayerVelocity..ctor mass = 100.</summary>
        public const float BaseMass = 100f;
        public const float JumpCooldown = 0.1f;
        public const float WallJumpWindow = 0.1f;
        public const float GroundedCoyoteTime = 0.05f;
        public static readonly Vector2 WallJumpUp = Vector2.up * 0.8f;
        public const float WallJumpNormal = 0.4f;

        public static float Multiplier(bool grounded) => grounded ? 1f : AirControl;

        /// <summary>PlayerMovement.Move (0x0600042a): y input clamped to [-1,0] then doubled.</summary>
        public static Vector2 MoveDelta(Vector2 direction, float force, float movementSpeed, float slow, float multiplier, float timeScale = 1f)
        {
            direction.y = Mathf.Clamp(direction.y, -1f, 0f) * 2f;
            return direction * timeScale * (1f - slow) * movementSpeed * force * 0.01f * multiplier * ForceModeScale;
        }

        /// <summary>PlayerMovement.FixedUpdate (0x06000428): v -= v * timeScale * 0.01 * 0.1 * extraDrag * multiplier.</summary>
        public static Vector2 DragDelta(Vector2 velocity, float extraDrag, float multiplier, float timeScale = 1f)
            => velocity * timeScale * 0.01f * 0.1f * extraDrag * multiplier;

        public static Vector2 BaseGravityDelta(float fixedDeltaTime, float timeScale = 1f)
            => Vector2.down * fixedDeltaTime * timeScale * BaseGravity;

        /// <summary>
        /// Gravity.FixedUpdate (0x06000286): airTime = min(sinceGrounded, sinceWallGrab);
        /// force = down * pow(airTime, exponent) * gravityForce (airTime &gt; 0) or down * airTime * gravityForce.
        /// </summary>
        public static Vector2 AirGravityDelta(float sinceGrounded, float sinceWallGrab, float gravityForce, float exponent, float timeScale = 1f)
        {
            float airTime = sinceWallGrab < sinceGrounded ? sinceWallGrab : sinceGrounded;
            float shaped = airTime > 0f ? Mathf.Pow(airTime, exponent) : airTime;
            return Vector2.down * timeScale * shaped * gravityForce * ForceModeScale;
        }

        /// <summary>PlayerJump.Jump (0x060003fc): impulse = dir * 0.01 * jump * (1 - slow) * upForce.</summary>
        public static Vector2 JumpImpulse(Vector2 direction, float jumpStat, float slow, float upForce)
            => direction * 0.01f * jumpStat * (1f - slow) * upForce;

        /// <summary>PlayerJump.Jump side impulse: right * x * sideForce * 0.01 * jump * (1 - slow) * velocity.x (sideForce=1).</summary>
        public static float JumpSideImpulse(float velocityX, float jumpStat, float slow, float sideForce = 1f)
            => sideForce * 0.01f * jumpStat * (1f - slow) * velocityX;

        public static Vector2 WallJumpDirection(Vector2 wallNormal) => WallJumpUp + wallNormal * WallJumpNormal;

        /// <summary>
        /// HealthHandler.TakeForce (0x060002af): upward force shortens the air timer, clamped to [-0.5, 100].
        /// forceY is the already mass-normalised impulse.
        /// </summary>
        public static float SinceGroundedAfterForce(float sinceGrounded, float forceY)
        {
            if (forceY > 0f) sinceGrounded -= forceY * 0.003f;
            return Mathf.Clamp(sinceGrounded, -0.5f, 100f);
        }
    }

    /// <summary>
    /// Serialized movement values are not readable without a type tree (UNKNOWN-01/02).
    /// These are PROVISIONAL development values chosen by simulation, not original data.
    /// </summary>
    [System.Serializable]
    public sealed class MotionTuning
    {
        public float force = 5200f;
        public float extraDrag = 80f;
        public float upForce = 2200f;
        public float gravityForce = 25f;
        public float gravityExponent = 1.2f;
        public float edgeImpulse = 45f;        // original constant 200 (Impulse) is paired with an unknown drag
        public float edgeImpulseBlocking = 70f; // original constant 400 while blocking
        public float edgeDamage = 51f;         // OutOfBoundsHandler.LateUpdate: 51
    }
}

