using UnityEngine;
using UnityEngine.InputSystem;

namespace GTH6
{
    /// <summary>
    /// Builds the player's input actions in code (keyboard, mouse and gamepad) and exposes
    /// them as simple read-only properties. Other scripts read from here instead of talking
    /// to the Input System directly, so rebinding later only touches this file.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        InputAction move;
        InputAction lookMouse;
        InputAction lookStick;
        InputAction jump;
        InputAction sprint;
        InputAction walk;
        InputAction crouch;
        InputAction aim;

        /// <summary>WASD / left stick. Length 0..1.</summary>
        public Vector2 Move => move.ReadValue<Vector2>();
        /// <summary>Mouse movement this frame, in pixels.</summary>
        public Vector2 LookMouseDelta => lookMouse.ReadValue<Vector2>();
        /// <summary>Right stick, -1..1 on each axis.</summary>
        public Vector2 LookStick => lookStick.ReadValue<Vector2>();
        public bool JumpPressed => jump.WasPressedThisFrame();
        public bool SprintHeld => sprint.IsPressed();
        public bool WalkHeld => walk.IsPressed();
        public bool CrouchPressed => crouch.WasPressedThisFrame();
        public bool AimHeld => aim.IsPressed();

        void Awake()
        {
            move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            move.AddBinding("<Gamepad>/leftStick");

            lookMouse = new InputAction("LookMouse", InputActionType.Value, "<Mouse>/delta", expectedControlType: "Vector2");
            lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick", expectedControlType: "Vector2");

            jump = new InputAction("Jump", InputActionType.Button);
            jump.AddBinding("<Keyboard>/space");
            jump.AddBinding("<Gamepad>/buttonSouth");

            sprint = new InputAction("Sprint", InputActionType.Button);
            sprint.AddBinding("<Keyboard>/leftShift");
            sprint.AddBinding("<Gamepad>/leftStickPress");

            walk = new InputAction("Walk", InputActionType.Button);
            walk.AddBinding("<Keyboard>/leftAlt");

            crouch = new InputAction("Crouch", InputActionType.Button);
            crouch.AddBinding("<Keyboard>/c");
            crouch.AddBinding("<Keyboard>/leftCtrl");
            crouch.AddBinding("<Gamepad>/buttonEast");

            aim = new InputAction("Aim", InputActionType.Button);
            aim.AddBinding("<Mouse>/rightButton");
            aim.AddBinding("<Gamepad>/leftTrigger");
        }

        void OnEnable() => SetEnabled(true);
        void OnDisable() => SetEnabled(false);

        void OnDestroy()
        {
            move?.Dispose();
            lookMouse?.Dispose();
            lookStick?.Dispose();
            jump?.Dispose();
            sprint?.Dispose();
            walk?.Dispose();
            crouch?.Dispose();
            aim?.Dispose();
        }

        void SetEnabled(bool on)
        {
            foreach (InputAction action in new[] { move, lookMouse, lookStick, jump, sprint, walk, crouch, aim })
            {
                if (action == null) continue;
                if (on) action.Enable();
                else action.Disable();
            }
        }
    }
}
