using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GTH6
{
    /// <summary>
    /// Getting in and out of cars, and driving them. Press F near a car's door to get in, F again to
    /// get out (below 10 km/h). While seated, the player's walking scripts are off and this script
    /// feeds the car: W/S (or triggers) for throttle and brake/reverse, A/D for steering, Space for handbrake.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerVehicleInteraction : MonoBehaviour
    {
        static readonly int InVehicleHash = Animator.StringToHash("InVehicle");
        static readonly int EnterCarHash = Animator.StringToHash("EnterCar");
        static readonly int DrivingHash = Animator.StringToHash("Driving");

        [SerializeField] PlayerCameraRig cameraRig;
        [SerializeField] CarRadio radio;
        [Tooltip("How close (m) to a car's door you must be to get in.")]
        [SerializeField] float enterRange = 2.5f;
        [SerializeField] float maxExitSpeedKmh = 10f;
        [Tooltip("Seconds the Entering Car animation plays before the player is put in the seat. Only used if the Animator has a real EnterCar clip.")]
        [SerializeField] float enterAnimationTime = 1.2f;
        [Tooltip("Hide the character while driving. Useful with a placeholder car that has no interior.")]
        [SerializeField] bool hideBodyWhileDriving;

        PlayerController controller;
        CharacterController cc;
        Animator animator;
        RagdollController ragdoll;
        Renderer[] bodyRenderers;

        InputAction interact, throttle, brake, steer, handbrake;
        bool busy;

        public VehicleController CurrentVehicle { get; private set; }
        public bool InVehicle => CurrentVehicle != null;

        void Awake()
        {
            controller = GetComponent<PlayerController>();
            cc = GetComponent<CharacterController>();
            animator = GetComponentInChildren<Animator>();
            ragdoll = GetComponent<RagdollController>();
            if (cameraRig == null) cameraRig = GetComponent<PlayerCameraRig>();
            if (radio == null) radio = GetComponent<CarRadio>();
            bodyRenderers = animator != null ? animator.GetComponentsInChildren<Renderer>(true) : new Renderer[0];

            interact = new InputAction("EnterExit", InputActionType.Button);
            interact.AddBinding("<Keyboard>/f");
            interact.AddBinding("<Gamepad>/buttonNorth");

            throttle = new InputAction("Throttle", InputActionType.Value, expectedControlType: "Axis");
            throttle.AddBinding("<Keyboard>/w");
            throttle.AddBinding("<Keyboard>/upArrow");
            throttle.AddBinding("<Gamepad>/rightTrigger");

            brake = new InputAction("Brake", InputActionType.Value, expectedControlType: "Axis");
            brake.AddBinding("<Keyboard>/s");
            brake.AddBinding("<Keyboard>/downArrow");
            brake.AddBinding("<Gamepad>/leftTrigger");

            steer = new InputAction("Steer", InputActionType.Value, expectedControlType: "Axis");
            steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
            steer.AddBinding("<Gamepad>/leftStick/x");

            handbrake = new InputAction("Handbrake", InputActionType.Button);
            handbrake.AddBinding("<Keyboard>/space");
            handbrake.AddBinding("<Gamepad>/buttonSouth");
        }

        void OnEnable()
        {
            foreach (InputAction a in Actions()) a.Enable();
            if (ragdoll != null) ragdoll.RagdollStarted += OnRagdoll;
        }

        void OnDisable()
        {
            foreach (InputAction a in Actions()) a.Disable();
            if (ragdoll != null) ragdoll.RagdollStarted -= OnRagdoll;
        }

        void OnDestroy()
        {
            foreach (InputAction a in Actions()) a.Dispose();
        }

        InputAction[] Actions() => new[] { interact, throttle, brake, steer, handbrake };

        void Update()
        {
            if (busy) return;

            if (!InVehicle)
            {
                if (interact.WasPressedThisFrame() && !(ragdoll != null && ragdoll.IsRagdoll))
                {
                    VehicleController nearest = FindNearestVehicle();
                    if (nearest != null) StartCoroutine(EnterRoutine(nearest));
                }
                return;
            }

            CurrentVehicle.SetInput(throttle.ReadValue<float>(), brake.ReadValue<float>(), steer.ReadValue<float>(), handbrake.IsPressed());

            if (interact.WasPressedThisFrame() && CurrentVehicle.SpeedKmh <= maxExitSpeedKmh) TryExit();
        }

        VehicleController FindNearestVehicle()
        {
            VehicleController best = null;
            float bestDistance = enterRange;
            foreach (VehicleController v in VehicleController.All)
            {
                // Free cars, and cars with an AI driver (you can take those, GTA style). Not other players' cars.
                if (v == null || (v.HasDriver && !(v.Driver is IVehicleAI))) continue;
                Vector3 door = v.ExitPoint != null ? v.ExitPoint.position : v.transform.position;
                float d = Vector3.Distance(transform.position, door);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = v;
                }
            }
            return best;
        }

        IEnumerator EnterRoutine(VehicleController v)
        {
            busy = true;
            CurrentVehicle = v;
            if (v.Driver is IVehicleAI ai) ai.Eject();
            v.SetDriver(this); // claim it at once so nothing else can take the seat
            controller.enabled = false;
            cc.enabled = false;

            if (animator != null && animator.HasState(0, EnterCarHash) && v.ExitPoint != null && enterAnimationTime > 0f)
            {
                Vector3 toSeat = v.DriverSeat.position - v.ExitPoint.position;
                toSeat.y = 0f;
                transform.SetPositionAndRotation(v.ExitPoint.position, toSeat.sqrMagnitude > 0.001f ? Quaternion.LookRotation(toSeat) : transform.rotation);
                animator.Play(EnterCarHash, 0, 0f);
                yield return new WaitForSeconds(enterAnimationTime);
            }

            // The car might have been destroyed or taken while the animation played.
            if (CurrentVehicle != v || v == null)
            {
                busy = false;
                yield break;
            }

            transform.SetParent(v.DriverSeat, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            if (animator != null)
            {
                animator.SetBool(InVehicleHash, true);
                if (animator.HasState(0, DrivingHash)) animator.Play(DrivingHash, 0, 0f);
            }
            SetBodyVisible(!hideBodyWhileDriving);
            if (cameraRig != null) cameraRig.EnterVehicle(v);
            if (radio != null) radio.SetPlaying(true);
            busy = false;
        }

        void TryExit()
        {
            int obstacles = ~(1 << gameObject.layer);
            if (!CurrentVehicle.TryGetExitPosition(cc.radius, cc.height, obstacles, out Vector3 position))
                return; // every side is blocked; stay in the car

            VehicleController v = CurrentVehicle;
            LeaveVehicle();

            Vector3 forward = v.transform.forward;
            forward.y = 0f;
            transform.SetPositionAndRotation(position, forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward) : Quaternion.identity);

            // ExitCar is tagged Locked, so PlayerController holds still until the clip ends.
            cc.enabled = true;
            controller.enabled = true;
        }

        /// <summary>Common cleanup for getting out and for being thrown out (ragdoll).</summary>
        void LeaveVehicle()
        {
            if (CurrentVehicle != null) CurrentVehicle.SetDriver(null);
            CurrentVehicle = null;
            if (transform.parent != null) transform.SetParent(null, true);
            if (animator != null && animator.isActiveAndEnabled) animator.SetBool(InVehicleHash, false);
            SetBodyVisible(true);
            if (cameraRig != null) cameraRig.ExitVehicle();
            if (radio != null) radio.SetPlaying(false);
        }

        void OnRagdoll()
        {
            StopAllCoroutines();
            busy = false;
            if (InVehicle) LeaveVehicle();
        }

        void SetBodyVisible(bool visible)
        {
            foreach (Renderer r in bodyRenderers) if (r != null) r.enabled = visible;
        }
    }
}
