using UnityEngine;

namespace GTH6
{
    /// <summary>
    /// Third-person movement on a CharacterController: acceleration, slopes, stairs snapping,
    /// jump (with coyote time and jump buffer), crouch, vault, and aim-strafe mode.
    /// Sends gait values to the Animator. Root motion is not used: this script owns movement.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInputReader))]
    public class PlayerController : MonoBehaviour
    {
        const string LockedTag = "Locked";

        static readonly int MoveXHash = Animator.StringToHash("MoveX");
        static readonly int MoveYHash = Animator.StringToHash("MoveY");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int CrouchHash = Animator.StringToHash("Crouch");
        static readonly int AimingHash = Animator.StringToHash("Aiming");
        static readonly int JumpHash = Animator.StringToHash("Jump");
        static readonly int VaultHash = Animator.StringToHash("Vault");

        [Header("References (auto-found if empty)")]
        [SerializeField] PlayerCameraRig cameraRig;
        [SerializeField] Animator animator;

        [Header("Speeds (m/s)")]
        [SerializeField] float walkSpeed = 1.6f;
        [SerializeField] float jogSpeed = 3.6f;
        [SerializeField] float sprintSpeed = 6.2f;
        [SerializeField] float crouchSpeed = 1.5f;
        [SerializeField] float aimMoveSpeed = 2.2f;

        [Header("Acceleration (m/s per second)")]
        [SerializeField] float acceleration = 14f;
        [SerializeField] float deceleration = 18f;
        [SerializeField, Range(0f, 1f)] float airControl = 0.25f;

        [Header("Turning (degrees per second)")]
        [SerializeField] float turnSpeed = 720f;
        [SerializeField] float aimTurnSpeed = 1080f;

        [Header("Jump and Gravity")]
        [SerializeField] float jumpHeight = 1.1f;
        [SerializeField] float gravity = -20f;
        [SerializeField] float terminalVelocity = -50f;
        [SerializeField] float coyoteTime = 0.15f;
        [SerializeField] float jumpBufferTime = 0.15f;

        [Header("Ground and Slopes")]
        [Tooltip("Layers you can stand on. Your own layer is removed automatically.")]
        [SerializeField] LayerMask groundMask = ~0;
        [Tooltip("How far down we pull the player to stay glued to stairs and slope changes.")]
        [SerializeField] float groundSnapDistance = 0.35f;
        [SerializeField] float groundStickVelocity = 2f;
        [SerializeField] float steepSlideSpeed = 4f;

        [Header("Crouch (overrides the CharacterController height)")]
        [SerializeField] float standingHeight = 1.8f;
        [SerializeField] float crouchingHeight = 1.1f;

        [Header("Vault")]
        [Tooltip("How far ahead of the capsule an obstacle can be to start a vault.")]
        [SerializeField] float vaultReach = 0.7f;
        [SerializeField] float minVaultHeight = 0.45f;
        [SerializeField] float maxVaultHeight = 1.25f;
        [Tooltip("Landing point distance past the obstacle's front face.")]
        [SerializeField] float vaultLandingDistance = 1.3f;
        [SerializeField] float maxVaultDrop = 1.5f;
        [SerializeField] float vaultDuration = 0.6f;
        [SerializeField] float vaultClearance = 0.15f;

        CharacterController cc;
        PlayerInputReader input;
        int mask;

        Vector3 desiredVelocity;
        Vector3 horizontalVelocity;
        float verticalVelocity;

        bool isGrounded;
        bool onSteepSlope;
        Vector3 groundNormal = Vector3.up;
        float lastGroundedTime = -999f;
        float lastJumpPressedTime = -999f;
        bool jumpedThisFrame;

        bool isCrouching;
        bool wantsCrouch;

        bool isVaulting;
        float vaultTimer;
        Vector3 vaultStart, vaultApex, vaultEnd, vaultDirection;
        float vaultExitSpeed;

        public bool IsGrounded => isGrounded;
        public bool IsCrouching => isCrouching;
        public bool IsVaulting => isVaulting;
        public bool IsAiming { get; private set; }
        public bool IsSprinting { get; private set; }
        public float HorizontalSpeed => horizontalVelocity.magnitude;
        public Vector3 Velocity => horizontalVelocity + Vector3.up * (isGrounded ? 0f : verticalVelocity);

        Vector3 FeetPosition => transform.TransformPoint(cc.center) - Vector3.up * (cc.height * 0.5f);

        // False when there is no model or no Animator Controller yet (blockout testing with a capsule).
        bool HasAnimator => animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null;

        bool MovementLocked => HasAnimator && animator.GetCurrentAnimatorStateInfo(0).IsTag(LockedTag);

        void Reset()
        {
            groundMask = ~LayerMask.GetMask("Player");
        }

        void OnValidate()
        {
            walkSpeed = Mathf.Max(0.1f, walkSpeed);
            jogSpeed = Mathf.Max(walkSpeed + 0.1f, jogSpeed);
            sprintSpeed = Mathf.Max(jogSpeed + 0.1f, sprintSpeed);
            crouchSpeed = Mathf.Max(0.1f, crouchSpeed);
            gravity = Mathf.Min(-0.1f, gravity);
            vaultDuration = Mathf.Max(0.1f, vaultDuration);
            maxVaultHeight = Mathf.Max(minVaultHeight + 0.05f, maxVaultHeight);
        }

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            input = GetComponent<PlayerInputReader>();
            if (cameraRig == null) cameraRig = GetComponent<PlayerCameraRig>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            if (gameObject.layer == 0)
                Debug.LogError("[PlayerController] The Player is on the Default layer. Create a 'Player' layer and put the whole Player hierarchy on it, otherwise ground checks hit your own collider.", this);

            mask = groundMask & ~(1 << gameObject.layer);

            crouchingHeight = Mathf.Max(crouchingHeight, cc.radius * 2f + 0.05f);
            standingHeight = Mathf.Max(standingHeight, crouchingHeight);
            SetCrouch(false);
        }

        void OnEnable() => ResetMotion();

        /// <summary>Clears all momentum. Called after ragdoll recovery, respawn, leaving a car.</summary>
        public void ResetMotion()
        {
            desiredVelocity = Vector3.zero;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            lastJumpPressedTime = -999f;
            if (isVaulting && cc != null) cc.enabled = true;
            isVaulting = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            jumpedThisFrame = false;
            if (input.JumpPressed) lastJumpPressedTime = Time.time;

            if (isVaulting)
            {
                UpdateVault(dt);
                UpdateAnimator(dt);
                return;
            }

            UpdateGround();
            bool locked = MovementLocked;

            IsAiming = input.AimHeld && !locked;
            if (!locked) HandleCrouch();
            UpdateDesiredVelocity(locked);
            UpdateHorizontalVelocity(dt);

            if (!locked)
            {
                HandleJump();
                if (isVaulting)
                {
                    UpdateAnimator(dt);
                    return;
                }
            }

            UpdateRotation(dt);
            ApplyGravity(dt);
            Move(dt);
            UpdateAnimator(dt);
        }

        // ---------------- Ground ----------------

        bool ProbeGround(float probeDistance, out RaycastHit hit, out float distance)
        {
            float r = cc.radius;
            float castRadius = r * 0.9f;
            float lift = r + 0.05f;
            float startGap = lift - castRadius;
            Vector3 origin = FeetPosition + Vector3.up * lift;

            if (Physics.SphereCast(origin, castRadius, Vector3.down, out hit, startGap + probeDistance, mask, QueryTriggerInteraction.Ignore))
            {
                distance = hit.distance - startGap;
                return true;
            }

            distance = 0f;
            return false;
        }

        void UpdateGround()
        {
            bool wasGrounded = isGrounded;
            isGrounded = false;
            onSteepSlope = false;
            groundNormal = Vector3.up;

            // Moving up (jump): never grounded.
            if (verticalVelocity > 0.01f) return;

            float probe = wasGrounded ? groundSnapDistance : 0.08f;
            if (ProbeGround(probe, out RaycastHit hit, out _))
            {
                Vector3 normal = hit.normal;

                // A sphere touching a stair edge reports a tilted normal. A straight ray down
                // gives the real floor normal, so take whichever is flatter.
                if (Physics.Raycast(FeetPosition + Vector3.up * 0.1f, Vector3.down, out RaycastHit rayHit,
                        0.4f + probe, mask, QueryTriggerInteraction.Ignore) &&
                    Vector3.Angle(Vector3.up, rayHit.normal) < Vector3.Angle(Vector3.up, normal))
                {
                    normal = rayHit.normal;
                }

                groundNormal = normal;
                if (Vector3.Angle(Vector3.up, normal) <= cc.slopeLimit) isGrounded = true;
                else onSteepSlope = true;
            }
            else if (cc.isGrounded)
            {
                isGrounded = true;
            }

            if (isGrounded) lastGroundedTime = Time.time;
        }

        void SnapToGround()
        {
            if (ProbeGround(groundSnapDistance, out RaycastHit hit, out float distance) &&
                distance > 0.001f &&
                Vector3.Angle(Vector3.up, hit.normal) <= cc.slopeLimit)
            {
                cc.Move(Vector3.down * (distance + cc.skinWidth));
            }
        }

        // ---------------- Input to velocity ----------------

        void HandleCrouch()
        {
            if (input.CrouchPressed) wantsCrouch = !wantsCrouch;
            if (wantsCrouch && input.SprintHeld && input.Move.sqrMagnitude > 0.01f) wantsCrouch = false;

            bool target = wantsCrouch;
            if (!target && isCrouching && !HasHeadroom()) target = true;
            if (target != isCrouching) SetCrouch(target);
        }

        void SetCrouch(bool crouch)
        {
            isCrouching = crouch;
            float h = crouch ? crouchingHeight : standingHeight;
            cc.height = h;
            cc.center = new Vector3(0f, h * 0.5f, 0f);
        }

        bool HasHeadroom()
        {
            float r = cc.radius;
            Vector3 feet = FeetPosition;
            Vector3 bottom = feet + Vector3.up * (r + 0.05f);
            Vector3 top = feet + Vector3.up * (standingHeight - r);
            return !Physics.CheckCapsule(bottom, top, r * 0.95f, mask, QueryTriggerInteraction.Ignore);
        }

        void UpdateDesiredVelocity(bool locked)
        {
            Vector2 raw = locked ? Vector2.zero : input.Move;
            float magnitude = Mathf.Clamp01(raw.magnitude);
            if (magnitude < 0.05f)
            {
                desiredVelocity = Vector3.zero;
                IsSprinting = false;
                return;
            }

            float yaw = CameraYaw();
            Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * new Vector3(raw.x, 0f, raw.y);
            direction.Normalize();

            IsSprinting = input.SprintHeld && !IsAiming && !isCrouching;

            float maxSpeed;
            if (isCrouching) maxSpeed = crouchSpeed;
            else if (IsAiming) maxSpeed = aimMoveSpeed;
            else if (IsSprinting) maxSpeed = sprintSpeed;
            else if (input.WalkHeld) maxSpeed = walkSpeed;
            else maxSpeed = jogSpeed;

            desiredVelocity = direction * (maxSpeed * magnitude);
        }

        float CameraYaw()
        {
            if (cameraRig != null) return cameraRig.Yaw;
            Camera cam = Camera.main;
            return cam != null ? cam.transform.eulerAngles.y : transform.eulerAngles.y;
        }

        void UpdateHorizontalVelocity(float dt)
        {
            bool hasInput = desiredVelocity.sqrMagnitude > 0.0001f;

            // In the air with no input, keep momentum (a sprint jump should carry you).
            if (!isGrounded && !hasInput) return;

            float rate = desiredVelocity.sqrMagnitude > horizontalVelocity.sqrMagnitude ? acceleration : deceleration;
            if (!isGrounded) rate *= airControl;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, desiredVelocity, rate * dt);
        }

        void UpdateRotation(float dt)
        {
            if (IsAiming)
            {
                Quaternion target = Quaternion.Euler(0f, CameraYaw(), 0f);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, aimTurnSpeed * dt);
            }
            else if (desiredVelocity.sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.LookRotation(new Vector3(desiredVelocity.x, 0f, desiredVelocity.z));
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
            }
        }

        // ---------------- Jump, gravity, move ----------------

        void HandleJump()
        {
            if (Time.time - lastJumpPressedTime > jumpBufferTime) return;

            if (isCrouching)
            {
                // Jump while crouched = stand up.
                lastJumpPressedTime = -999f;
                if (HasHeadroom())
                {
                    wantsCrouch = false;
                    SetCrouch(false);
                }
                return;
            }

            bool canJump = isGrounded || (Time.time - lastGroundedTime <= coyoteTime && verticalVelocity <= 0f);
            if (!canJump) return;

            lastJumpPressedTime = -999f;
            if (isGrounded && TryStartVault()) return;

            verticalVelocity = Mathf.Sqrt(2f * -gravity * jumpHeight);
            jumpedThisFrame = true;
            isGrounded = false;
            lastGroundedTime = -999f;
            if (HasAnimator) animator.SetTrigger(JumpHash);
        }

        void ApplyGravity(float dt)
        {
            if (jumpedThisFrame) return;

            if (isGrounded)
            {
                verticalVelocity = -groundStickVelocity;
                return;
            }

            verticalVelocity = Mathf.Max(verticalVelocity + gravity * dt, terminalVelocity);
        }

        void Move(float dt)
        {
            Vector3 planar = horizontalVelocity;

            // Walk along the slope instead of into or off it. Keeps the same speed on any angle.
            if (isGrounded && !jumpedThisFrame)
            {
                Vector3 projected = Vector3.ProjectOnPlane(planar, groundNormal);
                if (projected.sqrMagnitude > 0.000001f) planar = projected.normalized * planar.magnitude;
            }

            Vector3 velocity = planar + Vector3.up * verticalVelocity;
            if (onSteepSlope)
            {
                Vector3 slideDirection = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
                velocity += slideDirection * steepSlideSpeed;
            }

            Vector3 before = transform.position;
            CollisionFlags flags = cc.Move(velocity * dt);

            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;

            if (isGrounded && !jumpedThisFrame && (flags & CollisionFlags.Below) == 0) SnapToGround();

            // Running into a wall: stop pretending we are at full speed (fixes "jogging into walls").
            if ((flags & CollisionFlags.Sides) != 0)
            {
                Vector3 actual = (transform.position - before) / dt;
                actual.y = 0f;
                if (actual.sqrMagnitude < horizontalVelocity.sqrMagnitude) horizontalVelocity = actual;
            }
        }

        // ---------------- Vault ----------------

        bool TryStartVault()
        {
            if (desiredVelocity.sqrMagnitude < 0.25f) return false;

            Vector3 direction = new Vector3(desiredVelocity.x, 0f, desiredVelocity.z).normalized;
            Vector3 feet = FeetPosition;
            float r = cc.radius;
            const QueryTriggerInteraction q = QueryTriggerInteraction.Ignore;

            // 1. Something at knee height in front of us.
            if (!Physics.Raycast(feet + Vector3.up * 0.35f, direction, out RaycastHit front, r + vaultReach, mask, q)) return false;
            if (Mathf.Abs(front.normal.y) > 0.3f) return false;

            // 2. Nothing at head-of-obstacle height: it must be low.
            if (Physics.Raycast(feet + Vector3.up * (maxVaultHeight + 0.1f), direction, r + vaultReach + 0.2f, mask, q)) return false;

            // 3. Find the top surface.
            float topRayHeight = maxVaultHeight + 0.3f;
            Vector3 topOrigin = front.point + direction * 0.1f;
            topOrigin.y = feet.y + topRayHeight;
            if (!Physics.Raycast(topOrigin, Vector3.down, out RaycastHit top, topRayHeight, mask, q)) return false;

            float height = top.point.y - feet.y;
            if (height < minVaultHeight || height > maxVaultHeight) return false;

            // 4. Find the landing spot on the far side.
            Vector3 landXZ = front.point + direction * vaultLandingDistance;
            Vector3 landOrigin = new Vector3(landXZ.x, top.point.y + 0.5f, landXZ.z);
            if (!Physics.Raycast(landOrigin, Vector3.down, out RaycastHit land, 0.5f + height + maxVaultDrop, mask, q)) return false;
            if (land.point.y > top.point.y - 0.3f) return false; // still on top: obstacle too deep to vault
            if (Vector3.Angle(Vector3.up, land.normal) > cc.slopeLimit) return false;

            // 5. Room for the body over the top and at the landing.
            Vector3 apex = new Vector3(top.point.x, top.point.y + vaultClearance, top.point.z);
            if (Physics.CheckCapsule(apex + Vector3.up * (r + 0.05f), apex + Vector3.up * (crouchingHeight - r), r, mask, q)) return false;
            if (Physics.CheckCapsule(land.point + Vector3.up * (r + 0.05f), land.point + Vector3.up * (standingHeight - r), r, mask, q)) return false;

            Vector3 rootOffset = transform.position - feet;
            vaultStart = transform.position;
            vaultApex = apex + rootOffset;
            vaultEnd = land.point + rootOffset + Vector3.up * 0.02f;
            vaultDirection = direction;
            vaultExitSpeed = Mathf.Max(horizontalVelocity.magnitude, walkSpeed);
            vaultTimer = 0f;
            verticalVelocity = 0f;
            isVaulting = true;

            // The capsule would collide with the obstacle, so we move the transform by hand.
            cc.enabled = false;
            if (HasAnimator) animator.SetTrigger(VaultHash);
            return true;
        }

        void UpdateVault(float dt)
        {
            const float apexTime = 0.45f;
            vaultTimer += dt;
            float t = Mathf.Clamp01(vaultTimer / vaultDuration);

            Vector3 position;
            if (t < apexTime)
            {
                float k = t / apexTime;
                position = Vector3.Lerp(vaultStart, vaultApex, k);
                // Rise fast, then level out: feet clear the front edge.
                position.y = Mathf.Lerp(vaultStart.y, vaultApex.y, Mathf.Sin(k * Mathf.PI * 0.5f));
            }
            else
            {
                float k = (t - apexTime) / (1f - apexTime);
                position = Vector3.Lerp(vaultApex, vaultEnd, k);
                // Start falling slowly, then faster: looks like gravity.
                position.y = Mathf.Lerp(vaultApex.y, vaultEnd.y, 1f - Mathf.Cos(k * Mathf.PI * 0.5f));
            }

            transform.position = position;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(vaultDirection), turnSpeed * dt);
            horizontalVelocity = vaultDirection * vaultExitSpeed;

            if (t >= 1f)
            {
                isVaulting = false;
                cc.enabled = true;
                verticalVelocity = 0f;
                isGrounded = true;
                lastGroundedTime = Time.time;
            }
        }

        // ---------------- Animator ----------------

        /// <summary>
        /// Converts m/s into "gait units": 1 = walk, 2 = jog, 3 = sprint. The blend tree is
        /// built on these units, so you can change speeds in the Inspector without rebuilding it.
        /// </summary>
        float SpeedToGait(float speed)
        {
            if (isCrouching) return speed / crouchSpeed;
            if (speed <= walkSpeed) return speed / walkSpeed;
            if (speed <= jogSpeed) return 1f + (speed - walkSpeed) / (jogSpeed - walkSpeed);
            return 2f + Mathf.Min((speed - jogSpeed) / (sprintSpeed - jogSpeed), 1f);
        }

        void UpdateAnimator(float dt)
        {
            if (!HasAnimator) return;

            Vector3 local = transform.InverseTransformDirection(horizontalVelocity);
            Vector2 planarLocal = new Vector2(local.x, local.z);
            float speed = planarLocal.magnitude;
            float gait = SpeedToGait(speed);

            // Free movement: the character always faces where it runs, so only MoveY is used.
            // Aiming: the character faces the camera, so sideways/backwards movement uses strafe clips.
            Vector2 blend;
            if (IsAiming) blend = speed > 0.01f ? planarLocal / speed * gait : Vector2.zero;
            else blend = new Vector2(0f, gait);

            animator.SetFloat(MoveXHash, blend.x, 0.1f, dt);
            animator.SetFloat(MoveYHash, blend.y, 0.1f, dt);
            animator.SetBool(GroundedHash, isGrounded || isVaulting || Time.time - lastGroundedTime <= coyoteTime);
            animator.SetBool(CrouchHash, isCrouching);
            animator.SetBool(AimingHash, IsAiming);
        }

        void OnDrawGizmosSelected()
        {
            CharacterController c = GetComponent<CharacterController>();
            if (c == null) return;
            Vector3 feet = transform.TransformPoint(c.center) - Vector3.up * (c.height * 0.5f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(feet + Vector3.up * 0.35f, feet + Vector3.up * 0.35f + transform.forward * (c.radius + vaultReach));
            Gizmos.color = Color.red;
            Gizmos.DrawLine(feet + Vector3.up * (maxVaultHeight + 0.1f), feet + Vector3.up * (maxVaultHeight + 0.1f) + transform.forward * (c.radius + vaultReach + 0.2f));
        }
    }
}
