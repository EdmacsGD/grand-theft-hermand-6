using UnityEngine;

namespace GTH6
{
    public enum VehicleCategory { Hatchback, Sedan, Sports, Supercar, SUV, Van, Truck, Taxi, Police }
    public enum Drivetrain { FrontWheelDrive, RearWheelDrive, AllWheelDrive }

    /// <summary>
    /// Everything that makes one car model feel different. Create one per model with
    /// right click > Create > GTH6 > Vehicle Data, or GTH6 > Vehicles > Create Vehicle Data Set.
    /// The same VehicleController code drives all of them; only these numbers change.
    /// </summary>
    [CreateAssetMenu(fileName = "NewVehicle", menuName = "GTH6/Vehicle Data")]
    public class VehicleData : ScriptableObject
    {
        [Header("Identity (made-up brands only)")]
        public string brand = "Nordvik";
        public string modelName = "Pip";
        public VehicleCategory category = VehicleCategory.Hatchback;
        [Tooltip("Dealership price in kroner (phase 9).")]
        public int price = 15000;

        [Header("Body")]
        [Min(100f)] public float mass = 1150f;
        [Tooltip("Lower than the body centre = harder to roll over.")]
        public Vector3 centerOfMassOffset = new Vector3(0f, -0.45f, 0.1f);
        [Tooltip("Body size in metres (x width, y height, z length). Used by the test-car builder and traffic spacing.")]
        public Vector3 size = new Vector3(1.75f, 1.45f, 4.0f);

        [Header("Engine")]
        [Tooltip("x = RPM from 0 (idle) to 1 (redline), y = share of Max Torque. Peaks in the middle like a real engine.")]
        public AnimationCurve torqueCurve = new AnimationCurve(
            new Keyframe(0f, 0.55f), new Keyframe(0.45f, 1f), new Keyframe(0.8f, 0.92f), new Keyframe(1f, 0.7f));
        [Tooltip("Newton metres at the crank at the peak of the curve.")]
        [Min(10f)] public float maxTorque = 180f;
        [Min(500f)] public float idleRpm = 850f;
        [Min(1000f)] public float maxRpm = 6500f;
        [Min(20f)] public float topSpeedKmh = 170f;

        [Header("Gearbox (automatic)")]
        public float[] gearRatios = { 3.5f, 2.0f, 1.35f, 1.0f, 0.8f };
        public float reverseRatio = 3.2f;
        public float finalDrive = 3.9f;
        [Range(0.5f, 1f)] public float shiftUpAt = 0.88f;
        [Range(0.1f, 0.6f)] public float shiftDownAt = 0.35f;
        [Tooltip("Seconds with no drive while changing gear.")]
        [Range(0f, 1f)] public float shiftTime = 0.2f;
        public Drivetrain drivetrain = Drivetrain.FrontWheelDrive;

        [Header("Brakes")]
        [Min(0f)] public float brakeTorque = 2200f;
        [Min(0f)] public float handbrakeTorque = 4000f;

        [Header("Steering")]
        [Range(5f, 50f)] public float maxSteerAngle = 34f;
        [Tooltip("Steering angle left at top speed, so the car is not twitchy on the bridge.")]
        [Range(2f, 30f)] public float highSpeedSteerAngle = 9f;
        [Tooltip("Degrees per second the front wheels turn.")]
        [Min(10f)] public float steerSpeed = 140f;

        [Header("Grip")]
        [Range(0.3f, 3f)] public float forwardGrip = 1.2f;
        [Range(0.3f, 3f)] public float sidewaysGrip = 1.3f;
        [Tooltip("Rear sideways grip multiplier while the handbrake is held. Lower = longer drifts.")]
        [Range(0.1f, 1f)] public float driftFactor = 0.45f;
        [Tooltip("Push-down force per (m/s) squared. Keeps fast cars planted.")]
        [Min(0f)] public float downforce = 1.5f;
        [Tooltip("Resists body roll in corners (N per metre of suspension difference).")]
        [Min(0f)] public float antiRoll = 6000f;

        [Header("Suspension and wheels")]
        [Min(0.05f)] public float suspensionDistance = 0.2f;
        [Min(1000f)] public float spring = 35000f;
        [Min(100f)] public float damper = 4000f;
        [Min(0.15f)] public float wheelRadius = 0.33f;
        [Min(1f)] public float wheelMass = 20f;

        [Header("Damage")]
        [Min(1f)] public float maxHealth = 1000f;
        [Tooltip("1 = normal. Trucks take less damage per hit, supercars more.")]
        [Min(0f)] public float damageMultiplier = 1f;

        [Header("Audio (empty = built-in synthetic sound)")]
        public AudioClip engineLoop;
        public AudioClip skidLoop;
        [Range(0.3f, 1.5f)] public float engineMinPitch = 0.6f;
        [Range(1f, 4f)] public float engineMaxPitch = 2.1f;

        public string DisplayName => brand + " " + modelName;
        public float TopSpeedMS => topSpeedKmh / 3.6f;

        void OnValidate()
        {
            maxRpm = Mathf.Max(maxRpm, idleRpm + 500f);
            if (gearRatios == null || gearRatios.Length == 0) gearRatios = new[] { 1f };
        }
    }
}
