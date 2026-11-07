using System;
using System.Collections.Generic;
using UnityEngine;

namespace GTH6
{
    /// <summary>
    /// WheelCollider car physics driven by a VehicleData asset: automatic gearbox, torque curve,
    /// top-speed limiter, speed-sensitive steering, handbrake drifts, downforce and anti-roll bars.
    /// Whoever drives (the player, traffic AI, police AI) only calls SetInput, so every car in the
    /// game uses exactly the same physics.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        /// <summary>Every active car. The player uses it to find the nearest car to enter.</summary>
        public static readonly List<VehicleController> All = new List<VehicleController>();

        /// <summary>1 = dry. The weather system (phase 6) lowers it on wet roads; all cars feel it.</summary>
        public static float SurfaceGripMultiplier = 1f;

        [Serializable]
        public class Wheel
        {
            public WheelCollider collider;
            [Tooltip("The visible wheel mesh. Optional.")]
            public Transform visual;
            public bool front;
            [NonSerialized] public Quaternion visualOffset = Quaternion.identity;
        }

        [SerializeField] VehicleData data;
        [SerializeField] Wheel[] wheels = Array.Empty<Wheel>();

        [Header("Seats and camera")]
        [SerializeField] Transform driverSeat;
        [Tooltip("Where the driver stands after getting out (left side of the car).")]
        [SerializeField] Transform exitPoint;
        [Tooltip("What the vehicle camera looks at. Empty = the car itself.")]
        [SerializeField] Transform cameraAnchor;

        Rigidbody rb;
        float throttleInput, brakeInput, steerInput;
        bool handbrakeInput;

        float currentSteer;
        int gearIndex;
        bool reversing;
        float shiftTimer;
        float engineRpm;
        float driveAmount;
        readonly List<(WheelCollider left, WheelCollider right)> axles = new List<(WheelCollider, WheelCollider)>();

        public VehicleData Data => data;
        public Rigidbody Body => rb;
        public Transform DriverSeat => driverSeat != null ? driverSeat : transform;
        public Transform ExitPoint => exitPoint;
        public Transform CameraAnchor => cameraAnchor != null ? cameraAnchor : transform;
        public Component Driver { get; private set; }
        public bool HasDriver => Driver != null;
        /// <summary>Set by VehicleDamage when the car is wrecked. Brakes still work.</summary>
        public bool EngineDisabled { get; set; }

        public float ForwardSpeed => rb != null ? Vector3.Dot(rb.linearVelocity, transform.forward) : 0f;
        public float SpeedKmh => rb != null ? rb.linearVelocity.magnitude * 3.6f : 0f;
        public float EngineRpm => engineRpm;
        public float EngineRpm01 => data != null ? Mathf.InverseLerp(data.idleRpm, data.maxRpm, engineRpm) : 0f;
        public float DriveAmount => driveAmount;
        public bool IsReversing => reversing;
        public string GearDisplay => reversing ? "R" : (gearIndex + 1).ToString();

        /// <summary>Largest tyre slip this physics step (0 = gripping). Used for skid sounds and marks.</summary>
        public float MaxWheelSlip { get; private set; }

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (data == null)
            {
                Debug.LogError("[VehicleController] No VehicleData assigned.", this);
                enabled = false;
                return;
            }
            ApplyData();
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Pushes the VehicleData numbers into the Rigidbody and WheelColliders. Call again after changing data (tuning shop).</summary>
        public void ApplyData()
        {
            rb.mass = data.mass;
            rb.ResetCenterOfMass();
            rb.centerOfMass += data.centerOfMassOffset;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            engineRpm = data.idleRpm;

            axles.Clear();
            WheelCollider fl = null, fr = null, rl = null, rr = null;

            foreach (Wheel w in wheels)
            {
                if (w?.collider == null) continue;
                WheelCollider c = w.collider;
                c.radius = data.wheelRadius;
                c.mass = data.wheelMass;
                c.suspensionDistance = data.suspensionDistance;
                c.forceAppPointDistance = 0.1f; // lower = less body roll
                c.wheelDampingRate = 0.25f;
                c.suspensionSpring = new JointSpring { spring = data.spring, damper = data.damper, targetPosition = 0.5f };

                c.GetWorldPose(out _, out Quaternion rest);
                if (w.visual != null) w.visualOffset = Quaternion.Inverse(rest) * w.visual.rotation;

                bool left = transform.InverseTransformPoint(c.transform.position).x < 0f;
                if (w.front) { if (left) fl = c; else fr = c; }
                else { if (left) rl = c; else rr = c; }
            }

            if (fl != null && fr != null) axles.Add((fl, fr));
            if (rl != null && rr != null) axles.Add((rl, rr));

            // More physics sub-steps at low speed stop the classic WheelCollider jitter when parked.
            if (wheels.Length > 0 && wheels[0].collider != null) wheels[0].collider.ConfigureVehicleSubsteps(5f, 12, 15);
        }

        /// <summary>
        /// throttle 0..1, brake 0..1, steer -1 (left) .. 1 (right). Holding brake at a standstill reverses,
        /// like GTA: the brake key becomes the reverse throttle.
        /// </summary>
        public void SetInput(float throttle, float brake, float steer, bool handbrake)
        {
            throttleInput = Mathf.Clamp01(throttle);
            brakeInput = Mathf.Clamp01(brake);
            steerInput = Mathf.Clamp(steer, -1f, 1f);
            handbrakeInput = handbrake;
        }

        public void SetDriver(Component driver)
        {
            Driver = driver;
            if (driver == null) SetInput(0f, 0f, 0f, true);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float forwardSpeed = ForwardSpeed;
            float speed = rb.linearVelocity.magnitude;

            bool driven = HasDriver;
            float throttle = driven && !EngineDisabled ? throttleInput : 0f;
            float brake = driven ? brakeInput : 0f;
            bool handbrake = !driven || handbrakeInput;
            float steer = driven ? steerInput : 0f;

            // Switch between forward and reverse only when nearly stopped.
            if (reversing)
            {
                if (throttle > 0.05f && forwardSpeed > -0.5f) reversing = false;
            }
            else if (brake > 0.05f && throttle < 0.05f && forwardSpeed < 0.5f && !EngineDisabled)
            {
                reversing = true;
            }

            float drive = reversing ? brake : throttle;
            float brakeAmount = reversing ? throttle : brake;
            driveAmount = drive;

            UpdateGearbox(dt, drive);

            float ratio = reversing ? -data.reverseRatio : data.gearRatios[gearIndex];
            float engineTorque = data.torqueCurve.Evaluate(EngineRpm01) * data.maxTorque * drive;
            if (shiftTimer > 0f) engineTorque = 0f;
            if (!reversing && speed * 3.6f >= data.topSpeedKmh) engineTorque = 0f;
            if (reversing && speed * 3.6f >= 35f) engineTorque = 0f;
            float wheelTorque = engineTorque * ratio * data.finalDrive;

            int drivenCount = 0;
            foreach (Wheel w in wheels) if (w?.collider != null && IsDriven(w)) drivenCount++;

            float grip = Mathf.Max(0.05f, SurfaceGripMultiplier);
            float speedFactor = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / (data.TopSpeedMS * 0.6f));
            float maxAngle = Mathf.Lerp(data.maxSteerAngle, data.highSpeedSteerAngle, speedFactor);
            currentSteer = Mathf.MoveTowards(currentSteer, steer * maxAngle, data.steerSpeed * dt);

            bool holdStill = drive < 0.05f && speed < 0.5f;
            float maxSlip = 0f;

            foreach (Wheel w in wheels)
            {
                if (w?.collider == null) continue;
                WheelCollider c = w.collider;

                c.motorTorque = IsDriven(w) && drivenCount > 0 ? wheelTorque / drivenCount : 0f;

                float brakeTorque = brakeAmount * data.brakeTorque;
                if (handbrake && !w.front) brakeTorque = Mathf.Max(brakeTorque, data.handbrakeTorque);
                if (holdStill) brakeTorque = Mathf.Max(brakeTorque, data.brakeTorque * 0.3f);
                else if (drive < 0.05f && brakeAmount < 0.05f) brakeTorque = Mathf.Max(brakeTorque, 40f); // rolling resistance
                c.brakeTorque = brakeTorque;

                if (w.front) c.steerAngle = currentSteer;

                WheelFrictionCurve forward = c.forwardFriction;
                forward.stiffness = data.forwardGrip * grip;
                c.forwardFriction = forward;

                WheelFrictionCurve side = c.sidewaysFriction;
                bool drifting = driven && handbrakeInput && !w.front;
                side.stiffness = data.sidewaysGrip * grip * (drifting ? data.driftFactor : 1f);
                c.sidewaysFriction = side;

                if (c.GetGroundHit(out WheelHit hit))
                    maxSlip = Mathf.Max(maxSlip, Mathf.Abs(hit.sidewaysSlip), Mathf.Abs(hit.forwardSlip));
            }
            MaxWheelSlip = maxSlip;

            rb.AddForce(-transform.up * (data.downforce * rb.linearVelocity.sqrMagnitude));
            foreach ((WheelCollider left, WheelCollider right) in axles) ApplyAntiRoll(left, right);
        }

        bool IsDriven(Wheel w)
        {
            switch (data.drivetrain)
            {
                case Drivetrain.FrontWheelDrive: return w.front;
                case Drivetrain.RearWheelDrive: return !w.front;
                default: return true;
            }
        }

        void UpdateGearbox(float dt, float drive)
        {
            if (shiftTimer > 0f) shiftTimer -= dt;
            gearIndex = Mathf.Clamp(gearIndex, 0, data.gearRatios.Length - 1); // in case the data was edited

            float wheelRpm = 0f;
            int count = 0;
            foreach (Wheel w in wheels)
            {
                if (w?.collider == null || !IsDriven(w)) continue;
                wheelRpm += Mathf.Abs(w.collider.rpm);
                count++;
            }
            if (count > 0) wheelRpm /= count;

            if (reversing) gearIndex = 0;
            float ratio = reversing ? data.reverseRatio : data.gearRatios[gearIndex];

            // At very low speed the clutch slips: the engine revs with the throttle instead of following the wheels.
            float fromWheels = wheelRpm * ratio * data.finalDrive;
            float slipping = data.idleRpm + (data.maxRpm * 0.45f - data.idleRpm) * drive;
            float target = Mathf.Clamp(Mathf.Max(fromWheels, slipping), data.idleRpm, data.maxRpm);
            engineRpm = Mathf.Lerp(engineRpm, target, 1f - Mathf.Exp(-12f * dt));

            if (reversing || shiftTimer > 0f) return;

            float rpm01 = Mathf.InverseLerp(data.idleRpm, data.maxRpm, fromWheels);
            if (rpm01 > data.shiftUpAt && gearIndex < data.gearRatios.Length - 1 && ForwardSpeed > 1f)
            {
                gearIndex++;
                shiftTimer = data.shiftTime;
            }
            else if (rpm01 < data.shiftDownAt && gearIndex > 0)
            {
                gearIndex--;
                shiftTimer = data.shiftTime * 0.5f;
            }
        }

        void ApplyAntiRoll(WheelCollider left, WheelCollider right)
        {
            float travelLeft = 1f, travelRight = 1f;
            bool groundedLeft = left.GetGroundHit(out WheelHit hitLeft);
            bool groundedRight = right.GetGroundHit(out WheelHit hitRight);
            if (groundedLeft) travelLeft = (-left.transform.InverseTransformPoint(hitLeft.point).y - left.radius) / left.suspensionDistance;
            if (groundedRight) travelRight = (-right.transform.InverseTransformPoint(hitRight.point).y - right.radius) / right.suspensionDistance;

            float force = (travelLeft - travelRight) * data.antiRoll;
            if (groundedLeft) rb.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            if (groundedRight) rb.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }

        void LateUpdate()
        {
            foreach (Wheel w in wheels)
            {
                if (w?.collider == null || w.visual == null) continue;
                w.collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
                w.visual.SetPositionAndRotation(position, rotation * w.visualOffset);
            }
        }

        /// <summary>True if t is one of the visible wheel meshes (or inside one). Wheels do not dent.</summary>
        public bool IsWheelVisual(Transform t)
        {
            foreach (Wheel w in wheels)
                if (w?.visual != null && (t == w.visual || t.IsChildOf(w.visual))) return true;
            return false;
        }

        /// <summary>
        /// Finds a free spot to stand after getting out: the exit point, then the other side, then the roof.
        /// </summary>
        public bool TryGetExitPosition(float radius, float height, int obstacleMask, out Vector3 position)
        {
            var candidates = new List<Vector3>();
            if (exitPoint != null)
            {
                candidates.Add(exitPoint.position);
                Vector3 local = transform.InverseTransformPoint(exitPoint.position);
                candidates.Add(transform.TransformPoint(new Vector3(-local.x, local.y, local.z)));
            }
            candidates.Add(transform.position + Vector3.up * ((data != null ? data.size.y : 1.5f) + 0.2f));

            foreach (Vector3 p in candidates)
            {
                Vector3 bottom = p + Vector3.up * (radius + 0.05f);
                Vector3 top = p + Vector3.up * (height - radius);
                if (!Physics.CheckCapsule(bottom, top, radius, obstacleMask, QueryTriggerInteraction.Ignore))
                {
                    position = p;
                    return true;
                }
            }
            position = Vector3.zero;
            return false;
        }
    }
}
