using System;
using System.Collections.Generic;
using UnityEngine;

namespace GTH6
{
    /// <summary>
    /// Car health, visible dents (mesh deformation at the impact point) and running people over.
    /// A trigger collider slightly bigger than the body ("CharacterHitTrigger") catches pedestrians
    /// and the player first, so they turn into a ragdoll instead of acting like a solid pole that
    /// stops the car dead.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class VehicleDamage : MonoBehaviour, IDamageable
    {
        [Tooltip("Body meshes that dent. Empty = every mesh on the car except the wheels.")]
        [SerializeField] MeshFilter[] deformableMeshes;

        [Header("Crashes")]
        [Tooltip("Impacts slower than this (m/s) do nothing. 3 m/s is about 11 km/h.")]
        [SerializeField] float minImpactSpeed = 3f;
        [Tooltip("Health lost per m/s above the minimum.")]
        [SerializeField] float damagePerSpeed = 25f;
        [SerializeField] float dentRadius = 0.7f;
        [Tooltip("Dent depth in metres per m/s of impact.")]
        [SerializeField] float dentDepthPerSpeed = 0.012f;
        [Tooltip("No vertex moves further than this from its original position.")]
        [SerializeField] float maxDent = 0.3f;
        [Tooltip("Optional smoke object, switched on below 30% health.")]
        [SerializeField] GameObject smokeEffect;

        [Header("Hitting people")]
        [Tooltip("Slower than this (m/s) a person just gets pushed aside by the body collider.")]
        [SerializeField] float minRagdollSpeed = 2.5f;
        [Tooltip("Ragdoll push in N*s per m/s of car speed.")]
        [SerializeField] float impulsePerSpeed = 20f;
        [Tooltip("Health a person loses per m/s of car speed.")]
        [SerializeField] float personDamagePerSpeed = 6f;

        VehicleController vehicle;
        readonly List<(MeshFilter filter, Vector3[] original, Vector3[] current)> meshes = new List<(MeshFilter, Vector3[], Vector3[])>();

        public float Health { get; private set; }
        public float MaxHealth => vehicle != null && vehicle.Data != null ? vehicle.Data.maxHealth : 1000f;
        public bool IsWrecked { get; private set; }

        public event Action<float> Damaged;
        public event Action Wrecked;

        void Awake()
        {
            vehicle = GetComponent<VehicleController>();
            Health = MaxHealth;

            if (deformableMeshes == null || deformableMeshes.Length == 0)
                deformableMeshes = GetComponentsInChildren<MeshFilter>(true);

            foreach (MeshFilter filter in deformableMeshes)
            {
                if (filter == null || filter.sharedMesh == null || vehicle.IsWheelVisual(filter.transform)) continue;
                Mesh mesh = filter.mesh; // per-car copy, so denting one car does not dent every car using that model
                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"[VehicleDamage] {filter.name}: enable Read/Write in the model's import settings to allow dents.", filter);
                    continue;
                }
                mesh.MarkDynamic();
                Vector3[] original = mesh.vertices;
                meshes.Add((filter, original, (Vector3[])original.Clone()));
            }

            if (smokeEffect != null) smokeEffect.SetActive(false);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.collider.isTrigger) return;
            if (collision.collider.GetComponentInParent<RagdollController>() != null) return; // bodies do not dent cars

            float speed = collision.relativeVelocity.magnitude;
            if (speed < minImpactSpeed) return;

            float multiplier = vehicle.Data != null ? vehicle.Data.damageMultiplier : 1f;
            ApplyDamage((speed - minImpactSpeed) * damagePerSpeed * multiplier);

            ContactPoint contact = collision.GetContact(0);
            Vector3 inward = (vehicle.Body.worldCenterOfMass - contact.point).normalized;
            Dent(contact.point, inward, (speed - minImpactSpeed) * dentDepthPerSpeed);
        }

        void OnTriggerEnter(Collider other)
        {
            RagdollController victim = other.GetComponentInParent<RagdollController>();
            if (victim == null || victim.IsRagdoll || victim.transform.IsChildOf(transform)) return;

            Vector3 velocity = vehicle.Body.GetPointVelocity(other.bounds.center);
            float speed = velocity.magnitude;
            if (speed < minRagdollSpeed) return;

            Vector3 point = other.ClosestPoint(transform.position);
            Vector3 impulse = (velocity + Vector3.up * (speed * 0.3f)).normalized * (speed * impulsePerSpeed);
            GameObject instigator = vehicle.Driver != null ? vehicle.Driver.gameObject : gameObject;

            // Damage first: if it kills, Health.Died already throws the ragdoll with this impulse.
            Health health = victim.GetComponent<Health>();
            if (health != null) health.TakeDamage(new DamageInfo(speed * personDamagePerSpeed, DamageType.Vehicle, point, impulse, instigator));
            if (!victim.IsRagdoll) victim.Activate(impulse, point, true);
        }

        public void TakeDamage(DamageInfo info)
        {
            float multiplier = vehicle.Data != null ? vehicle.Data.damageMultiplier : 1f;
            ApplyDamage(info.Amount * multiplier);
            if (info.Point != Vector3.zero)
                Dent(info.Point, (vehicle.Body.worldCenterOfMass - info.Point).normalized, 0.02f);
        }

        void ApplyDamage(float amount)
        {
            if (IsWrecked || amount <= 0f) return;
            Health = Mathf.Max(0f, Health - amount);
            Damaged?.Invoke(amount);

            if (smokeEffect != null && Health < MaxHealth * 0.3f) smokeEffect.SetActive(true);
            if (Health <= 0f)
            {
                IsWrecked = true;
                vehicle.EngineDisabled = true;
                Wrecked?.Invoke();
            }
        }

        /// <summary>Pushes body vertices near worldPoint along direction. Works in world space so scaled meshes dent evenly.</summary>
        void Dent(Vector3 worldPoint, Vector3 direction, float depth)
        {
            if (depth <= 0f) return;
            foreach ((MeshFilter filter, Vector3[] original, Vector3[] current) in meshes)
            {
                if (filter == null) continue;
                Transform t = filter.transform;
                bool changed = false;

                for (int i = 0; i < current.Length; i++)
                {
                    Vector3 world = t.TransformPoint(current[i]);
                    float distance = Vector3.Distance(world, worldPoint);
                    if (distance > dentRadius) continue;

                    Vector3 moved = t.InverseTransformPoint(world + direction * (depth * (1f - distance / dentRadius)));
                    Vector3 offset = moved - original[i];
                    float worldLength = t.TransformVector(offset).magnitude;
                    if (worldLength > maxDent) offset *= maxDent / worldLength;
                    current[i] = original[i] + offset;
                    changed = true;
                }

                if (!changed) continue;
                Mesh mesh = filter.mesh;
                mesh.vertices = current;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
        }

        /// <summary>Restores health and removes all dents (Pay 'n' Spray style, phase 9).</summary>
        public void Repair()
        {
            foreach ((MeshFilter filter, Vector3[] original, Vector3[] current) in meshes)
            {
                if (filter == null) continue;
                Array.Copy(original, current, original.Length);
                Mesh mesh = filter.mesh;
                mesh.vertices = current;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
            Health = MaxHealth;
            IsWrecked = false;
            vehicle.EngineDisabled = false;
            if (smokeEffect != null) smokeEffect.SetActive(false);
        }
    }
}
