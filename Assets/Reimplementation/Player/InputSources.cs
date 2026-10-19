using UnityEngine;
using UnityEngine.InputSystem;

namespace Metadream.Shoot.Player
{
    /// <summary>One frame of intents, shaped after GeneralInput (direction, aimDirection, *WasPressed/*IsPressed).</summary>
    public struct InputFrame
    {
        public Vector2 Move;
        public Vector2 Aim;
        public bool JumpPressed;
        public bool JumpHeld;
        public bool ShootPressed;
        public bool ShootHeld;
        public bool BlockPressed;
        public bool ConfirmPressed;
        public bool NavLeft;
        public bool NavRight;
        public bool NavUp;
        public bool NavDown;
        /// <summary>Card reroll request: 0 none, 1..n = card index + 1, 255 = the selected card.</summary>
        public int Reroll;
    }

    public struct InputContext
    {
        public Vector2 Position;
        public Camera Camera;
    }

    public interface IInputSource
    {
        string Label { get; }
        InputFrame Read(in InputContext context);
    }

    public sealed class KeyboardBinding
    {
        public Key Left, Right, Up, Down, Jump, AltJump, Shoot, AltShoot, Block, AltBlock;
        public string Help;

        public static KeyboardBinding PlayerOne() => new KeyboardBinding
        {
            Left = Key.A, Right = Key.D, Up = Key.W, Down = Key.S,
            Jump = Key.W, AltJump = Key.Space, Shoot = Key.F, AltShoot = Key.F, Block = Key.G, AltBlock = Key.LeftShift,
            Help = "이동 A/D · 점프 W/Space · 조준 WASD/마우스 · 사격 F/좌클릭 · 방어 G/우클릭"
        };

        public static KeyboardBinding PlayerTwo() => new KeyboardBinding
        {
            Left = Key.LeftArrow, Right = Key.RightArrow, Up = Key.UpArrow, Down = Key.DownArrow,
            Jump = Key.UpArrow, AltJump = Key.RightShift, Shoot = Key.Period, AltShoot = Key.Numpad1, Block = Key.Slash, AltBlock = Key.Numpad2,
            Help = "이동 ←/→ · 점프 ↑ · 조준 방향키 · 사격 . (Num1) · 방어 / (Num2)"
        };
    }

    /// <summary>Keyboard half + optional mouse + optional gamepad index, merged.</summary>
    public sealed class DeviceInput : IInputSource
    {
        private readonly KeyboardBinding _keys;
        private readonly bool _useMouse;
        private readonly int _gamepadIndex;
        private Vector2 _lastAim = Vector2.right;
        private float _lastMouseActivity = -100f;
        private float _prevStickX;
        private float _prevStickY;

        public DeviceInput(KeyboardBinding keys, bool useMouse, int gamepadIndex, string label)
        {
            _keys = keys;
            _useMouse = useMouse;
            _gamepadIndex = gamepadIndex;
            Label = label;
        }

        public string Label { get; }
        public string Help => _keys != null ? _keys.Help : "게임패드";

        private static bool Pressed(Keyboard kb, Key k) => kb != null && k != Key.None && kb[k].wasPressedThisFrame;
        private static bool Held(Keyboard kb, Key k) => kb != null && k != Key.None && kb[k].isPressed;

        public InputFrame Read(in InputContext context)
        {
            var frame = new InputFrame();
            var kb = Keyboard.current;
            var gp = _gamepadIndex >= 0 && _gamepadIndex < Gamepad.all.Count ? Gamepad.all[_gamepadIndex] : null;
            Vector2 keyDir = Vector2.zero;

            if (_keys != null && kb != null)
            {
                if (Held(kb, _keys.Left)) keyDir.x -= 1f;
                if (Held(kb, _keys.Right)) keyDir.x += 1f;
                if (Held(kb, _keys.Up)) keyDir.y += 1f;
                if (Held(kb, _keys.Down)) keyDir.y -= 1f;
                frame.JumpPressed = Pressed(kb, _keys.Jump) || Pressed(kb, _keys.AltJump);
                frame.JumpHeld = Held(kb, _keys.Jump) || Held(kb, _keys.AltJump);
                frame.ShootPressed = Pressed(kb, _keys.Shoot) || Pressed(kb, _keys.AltShoot);
                frame.ShootHeld = Held(kb, _keys.Shoot) || Held(kb, _keys.AltShoot);
                frame.BlockPressed = Pressed(kb, _keys.Block) || Pressed(kb, _keys.AltBlock);
                frame.NavLeft = Pressed(kb, _keys.Left);
                frame.NavRight = Pressed(kb, _keys.Right);
                frame.NavUp = Pressed(kb, _keys.Up);
                frame.NavDown = Pressed(kb, _keys.Down);
                frame.ConfirmPressed = frame.ShootPressed || Pressed(kb, _keys.AltJump);
                if (Pressed(kb, Key.R)) frame.Reroll = 255;
            }

            Vector2 move = keyDir;
            Vector2 aim = Vector2.zero;

            if (gp != null)
            {
                Vector2 stick = gp.leftStick.ReadValue();
                if (stick.magnitude < 0.25f) stick = Vector2.zero;
                Vector2 dpad = gp.dpad.ReadValue();
                Vector2 pad = stick + dpad;
                if (pad.sqrMagnitude > 0f) move = Vector2.ClampMagnitude(move + pad, 1.5f);
                Vector2 right = gp.rightStick.ReadValue();
                if (right.magnitude > 0.4f) aim = right;
                else if (pad.sqrMagnitude > 0.05f) aim = pad;

                frame.JumpPressed |= gp.buttonSouth.wasPressedThisFrame;
                if (gp.buttonNorth.wasPressedThisFrame) frame.Reroll = 255;
                frame.JumpHeld |= gp.buttonSouth.isPressed;
                bool shootNow = gp.rightTrigger.wasPressedThisFrame || gp.buttonWest.wasPressedThisFrame;
                frame.ShootPressed |= shootNow;
                frame.ShootHeld |= gp.rightTrigger.isPressed || gp.buttonWest.isPressed;
                frame.BlockPressed |= gp.leftTrigger.wasPressedThisFrame || gp.rightShoulder.wasPressedThisFrame || gp.leftShoulder.wasPressedThisFrame;
                frame.ConfirmPressed |= gp.buttonSouth.wasPressedThisFrame || shootNow;

                float sx = stick.x + dpad.x;
                float sy = stick.y + dpad.y;
                frame.NavLeft |= sx < -0.5f && _prevStickX >= -0.5f;
                frame.NavRight |= sx > 0.5f && _prevStickX <= 0.5f;
                frame.NavUp |= sy > 0.5f && _prevStickY <= 0.5f;
                frame.NavDown |= sy < -0.5f && _prevStickY >= -0.5f;
                _prevStickX = sx;
                _prevStickY = sy;
            }

            if (_useMouse)
            {
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    if (mouse.delta.ReadValue().sqrMagnitude > 0.5f || mouse.leftButton.wasPressedThisFrame)
                        _lastMouseActivity = Time.unscaledTime;
                    frame.ShootPressed |= mouse.leftButton.wasPressedThisFrame;
                    frame.ShootHeld |= mouse.leftButton.isPressed;
                    frame.BlockPressed |= mouse.rightButton.wasPressedThisFrame;
                    if (aim == Vector2.zero && context.Camera != null && Time.unscaledTime - _lastMouseActivity < 2.5f)
                    {
                        Vector3 world = context.Camera.ScreenToWorldPoint(mouse.position.ReadValue());
                        Vector2 d = (Vector2)world - context.Position;
                        if (d.sqrMagnitude > 0.01f) aim = d;
                    }
                }
            }

            if (aim == Vector2.zero && keyDir != Vector2.zero) aim = keyDir;
            if (aim != Vector2.zero) _lastAim = aim.normalized;
            frame.Aim = _lastAim;
            frame.Move = Vector2.ClampMagnitude(move, 1f);
            return frame;
        }
    }
}

