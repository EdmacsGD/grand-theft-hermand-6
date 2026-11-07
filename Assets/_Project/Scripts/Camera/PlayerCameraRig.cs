using UnityEngine;
using UnityEngine.InputSystem;

namespace GTH6
{
    /// <summary>
    /// Turns mouse/stick input into yaw and pitch and applies them to the CameraTarget.
    /// Both Cinemachine cameras use Third Person Follow on that target, so they share the same
    /// orientation and the switch to the aim camera is seamless. Also handles cursor lock and
    /// follows the ragdoll's hips while knocked down.
    /// Runs before PlayerController so movement uses this frame's camera yaw.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PlayerCameraRig : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] PlayerInputReader input;
        [SerializeField] PlayerController controller;
        [SerializeField] RagdollController ragdoll;
        [Tooltip("Empty child of the Player. Both Cinemachine cameras track this.")]
        [SerializeField] Transform cameraTarget;
        [Tooltip("The aim Cinemachine Camera GameObject. Enabled while aiming.")]
        [SerializeField] GameObject aimCamera;
        [Tooltip("The vehicle Cinemachine Camera GameObject (phase 3). Enabled while driving.")]
        [SerializeField] GameObject vehicleCamera;

        [Header("Sensitivity")]
        [Tooltip("Degrees per pixel of mouse movement.")]
        [SerializeField] float mouseSensitivity = 0.1f;
        [Tooltip("Degrees per second at full stick.")]
        [SerializeField] float stickSensitivity = 160f;
        [SerializeField, Range(0.1f, 1f)] float aimSensitivityMultiplier = 0.6f;
        [SerializeField] bool invertY;

        [Header("Pitch Limits (negative = looking up)")]
        [SerializeField] float minPitch = -50f;
        [SerializeField] float maxPitch = 70f;

        [Header("Target Height")]
        [SerializeField] float standingHeight = 1.55f;
        [SerializeField] float crouchingHeight = 1.0f;
        [SerializeField] float heightSmoothing = 10f;
        [SerializeField] float ragdollHeightOffset = 0.4f;

        [Header("Vehicles")]
        [Tooltip("Seconds without mouse/stick input before the camera swings back behind the car.")]
        [SerializeField] float vehicleRecenterDelay = 1.5f;
        [Tooltip("Degrees per second while swinging back.")]
        [SerializeField] float vehicleRecenterSpeed = 120f;
        [SerializeField] float vehiclePitch = 10f;

        [Header("Cursor")]
        [SerializeField] bool lockCursorOnStart = true;

        float yaw;
        float pitch;
        float currentHeight;
        float lastLookTime;
        VehicleController vehicle;

        public float Yaw => yaw;
        public float Pitch => pitch;

        /// <summary>Settings menu (phase 8) writes here.</summary>
        public float MouseSensitivity
        {
            get => mouseSensitivity;
            set => mouseSensitivity = Mathf.Max(0.001f, value);
        }

        void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (controller == null) controller = GetComponent<PlayerController>();
            if (ragdoll == null) ragdoll = GetComponent<RagdollController>();

            if (cameraTarget == null)
            {
                Debug.LogError("[PlayerCameraRig] Assign Camera Target (an empty child of the Player).", this);
                enabled = false;
                return;
            }

            yaw = transform.eulerAngles.y;
            pitch = 10f;
            currentHeight = standingHeight;
        }

        void Start()
        {
            if (lockCursorOnStart) SetCursorLocked(true);
            if (aimCamera != null) aimCamera.SetActive(false);
            if (vehicleCamera != null) vehicleCamera.SetActive(false);
        }

        /// <summary>Called by PlayerVehicleInteraction when the player sits down in a car.</summary>
        public void EnterVehicle(VehicleController v)
        {
            vehicle = v;
            if (vehicleCamera != null) vehicleCamera.SetActive(true);
        }

        public void ExitVehicle()
        {
            vehicle = null;
            if (vehicleCamera != null) vehicleCamera.SetActive(false);
        }

        bool PlayerAiming => controller != null && controller.enabled && controller.IsAiming;

        void Update()
        {
            HandleCursor();
            ReadLook();
            UpdateTarget();
            UpdateAimCamera();
        }

        void HandleCursor()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            else if (Cursor.lockState != CursorLockMode.Locked && mouse != null && mouse.leftButton.wasPressedThisFrame) SetCursorLocked(true);
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void ReadLook()
        {
            if (input == null) return;

            Vector2 delta = Vector2.zero;
            // Mouse delta is already "per frame", so it is NOT multiplied by deltaTime.
            if (Cursor.lockState == CursorLockMode.Locked) delta += input.LookMouseDelta * mouseSensitivity;
            // A stick is a rate (how far it is pushed), so it IS multiplied by deltaTime.
            delta += input.LookStick * (stickSensitivity * Time.deltaTime);

            if (PlayerAiming) delta *= aimSensitivityMultiplier;
            if (delta.sqrMagnitude > 0.0001f) lastLookTime = Time.time;

            yaw = Mathf.Repeat(yaw + delta.x, 360f);
            pitch = Mathf.Clamp(pitch + (invertY ? delta.y : -delta.y), minPitch, maxPitch);

            // Driving: after a moment without input, swing back behind the car (not while reversing).
            if (vehicle != null && Time.time - lastLookTime > vehicleRecenterDelay && vehicle.SpeedKmh > 5f && !vehicle.IsReversing)
            {
                float step = vehicleRecenterSpeed * Time.deltaTime;
                yaw = Mathf.MoveTowardsAngle(yaw, vehicle.transform.eulerAngles.y, step);
                pitch = Mathf.MoveTowards(pitch, vehiclePitch, step * 0.5f);
            }
        }

        void UpdateTarget()
        {
            bool ragdolled = ragdoll != null && ragdoll.IsRagdoll && ragdoll.Hips != null;
            if (vehicle != null)
            {
                cameraTarget.position = vehicle.CameraAnchor.position;
            }
            else if (ragdolled)
            {
                cameraTarget.position = ragdoll.Hips.position + Vector3.up * ragdollHeightOffset;
            }
            else
            {
                float targetHeight = controller != null && controller.IsCrouching ? crouchingHeight : standingHeight;
                currentHeight = Mathf.Lerp(currentHeight, targetHeight, 1f - Mathf.Exp(-heightSmoothing * Time.deltaTime));
                cameraTarget.localPosition = new Vector3(0f, currentHeight, 0f);
            }

            // World rotation: ignores the player's own rotation, so turning the body does not swing the camera.
            cameraTarget.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void UpdateAimCamera()
        {
            if (aimCamera == null) return;
            bool aim = PlayerAiming && vehicle == null && !(ragdoll != null && ragdoll.IsRagdoll);
            if (aimCamera.activeSelf != aim) aimCamera.SetActive(aim);
        }
    }
}
