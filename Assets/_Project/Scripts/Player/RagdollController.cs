using System;
using System.Collections.Generic;
using UnityEngine;

namespace GTH6
{
    /// <summary>
    /// Switches a character between animated and ragdoll. Needs the rigidbodies and joints that
    /// Unity's Ragdoll Wizard creates on the bones. While animated, the bone colliders are
    /// triggers, so the CharacterController walks freely and raycasts (QueryTriggerInteraction.Ignore)
    /// skip them. Works for the player now and for pedestrians/police later.
    /// </summary>
    public class RagdollController : MonoBehaviour
    {
        [Header("References (auto-found if empty)")]
        [SerializeField] Animator animator;
        [SerializeField] Health health;
        [Tooltip("Scripts turned off while ragdolled. For the player: PlayerController.")]
        [SerializeField] Behaviour[] disableWhileRagdoll;

        [Header("Death")]
        [SerializeField] float deathImpulseMultiplier = 1f;

        [Header("Getting Up")]
        [SerializeField] float minRecoverTime = 2f;
        [SerializeField] float maxRagdollTime = 8f;
        [Tooltip("Hips speed (m/s) under which the body counts as settled.")]
        [SerializeField] float settledSpeed = 0.25f;
        [SerializeField] string getUpStateName = "GetUp";
        [SerializeField] LayerMask groundMask = ~0;

        CharacterController cc;
        Rigidbody[] bodies = Array.Empty<Rigidbody>();
        Collider[] boneColliders = Array.Empty<Collider>();
        Rigidbody hipsBody;
        Transform hips;
        Transform head;
        int mask;

        Vector3 lastPosition;
        Vector3 trackedVelocity;
        float ragdollTimer;
        bool autoRecover;

        public bool IsRagdoll { get; private set; }
        public Transform Hips => hips;

        public event Action RagdollStarted;
        public event Action RagdollEnded;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (health == null) health = GetComponent<Health>();
            if ((disableWhileRagdoll == null || disableWhileRagdoll.Length == 0) && TryGetComponent(out PlayerController pc))
                disableWhileRagdoll = new Behaviour[] { pc };

            var bodyList = new List<Rigidbody>();
            var colliderList = new List<Collider>();
            foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb.gameObject == gameObject) continue;
                bodyList.Add(rb);
                colliderList.AddRange(rb.GetComponents<Collider>());
                rb.gameObject.layer = gameObject.layer;
            }
            bodies = bodyList.ToArray();
            boneColliders = colliderList.ToArray();

            if (animator != null && animator.isHuman)
            {
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                head = animator.GetBoneTransform(HumanBodyBones.Head);
            }
            if (hips != null) hipsBody = hips.GetComponent<Rigidbody>();
            if (hipsBody == null && bodies.Length > 0)
            {
                hipsBody = bodies[0];
                hips = hipsBody.transform;
            }

            if (bodies.Length == 0)
                Debug.LogWarning("[RagdollController] No bone rigidbodies found. Run GameObject > 3D Object > Ragdoll... on the model first.", this);

            mask = groundMask & ~(1 << gameObject.layer);
            SetPhysicsActive(false);
            lastPosition = transform.position;
        }

        void OnEnable()
        {
            if (health != null) health.Died += OnDied;
        }

        void OnDisable()
        {
            if (health != null) health.Died -= OnDied;
        }

        void OnDied(DamageInfo info)
        {
            Vector3 impulse = info.Force * deathImpulseMultiplier;
            if (info.Point == Vector3.zero) Activate(impulse, false);
            else Activate(impulse, info.Point, false);
        }

        /// <summary>Ragdoll with a push at the hips.</summary>
        public void Activate(Vector3 impulse, bool recoverAutomatically)
        {
            Activate(impulse, hips != null ? hips.position : transform.position, recoverAutomatically);
        }

        /// <summary>Ragdoll with a push at a world point (bullet hit, car bumper contact).</summary>
        public void Activate(Vector3 impulse, Vector3 hitPoint, bool recoverAutomatically)
        {
            if (bodies.Length == 0) return;

            if (!IsRagdoll)
            {
                IsRagdoll = true;
                ragdollTimer = 0f;

                foreach (Behaviour b in disableWhileRagdoll)
                    if (b != null) b.enabled = false;
                if (cc != null) cc.enabled = false;
                if (animator != null) animator.enabled = false;

                SetPhysicsActive(true);
                // Keep the running speed so a sprinting character tumbles forward.
                foreach (Rigidbody rb in bodies) rb.linearVelocity = trackedVelocity;

                RagdollStarted?.Invoke();
            }

            autoRecover = recoverAutomatically && !(health != null && health.IsDead);

            if (impulse.sqrMagnitude > 0f)
            {
                Rigidbody target = ClosestBody(hitPoint);
                target.AddForceAtPosition(impulse, hitPoint, ForceMode.Impulse);
            }
        }

        /// <summary>Stands the character back up. Does nothing while dead (call Health.Revive first).</summary>
        public void Recover()
        {
            if (!IsRagdoll || hips == null) return;
            if (health != null && health.IsDead) return;

            Vector3 hipsPosition = hips.position;
            Vector3 groundPoint = hipsPosition;
            if (Physics.Raycast(hipsPosition + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f, mask, QueryTriggerInteraction.Ignore))
                groundPoint = hit.point;

            // The get-up clip assumes lying on the back, feet pointing forward.
            Vector3 facing = head != null ? Vector3.ProjectOnPlane(hipsPosition - head.position, Vector3.up) : transform.forward;
            if (facing.sqrMagnitude < 0.0001f) facing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;

            SetPhysicsActive(false);
            transform.SetPositionAndRotation(groundPoint, Quaternion.LookRotation(facing.normalized, Vector3.up));

            if (animator != null)
            {
                animator.enabled = true;
                int stateHash = Animator.StringToHash(getUpStateName);
                if (animator.HasState(0, stateHash)) animator.Play(stateHash, 0, 0f);
            }

            if (cc != null) cc.enabled = true;
            foreach (Behaviour b in disableWhileRagdoll)
                if (b != null) b.enabled = true;

            IsRagdoll = false;
            lastPosition = transform.position;
            trackedVelocity = Vector3.zero;
            RagdollEnded?.Invoke();
        }

        void Update()
        {
            if (!IsRagdoll || !autoRecover) return;

            ragdollTimer += Time.deltaTime;
            bool settled = hipsBody.linearVelocity.magnitude < settledSpeed;
            if ((ragdollTimer >= minRecoverTime && settled) || ragdollTimer >= maxRagdollTime) Recover();
        }

        void LateUpdate()
        {
            if (!IsRagdoll && Time.deltaTime > 0f)
                trackedVelocity = Vector3.ClampMagnitude((transform.position - lastPosition) / Time.deltaTime, 30f);
            lastPosition = transform.position;
        }

        void SetPhysicsActive(bool active)
        {
            foreach (Rigidbody rb in bodies)
            {
                rb.isKinematic = !active;
                rb.interpolation = active ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
                if (active) rb.WakeUp();
            }
            foreach (Collider col in boneColliders) col.isTrigger = !active;
        }

        Rigidbody ClosestBody(Vector3 point)
        {
            Rigidbody best = hipsBody;
            float bestDistance = float.MaxValue;
            foreach (Rigidbody rb in bodies)
            {
                float d = (rb.worldCenterOfMass - point).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = rb;
                }
            }
            return best;
        }
    }
}
