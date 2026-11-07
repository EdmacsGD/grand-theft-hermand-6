using UnityEngine;

namespace GTH6
{
    /// <summary>
    /// Engine sound pitched by RPM and louder on throttle, plus tyre squeal from wheel slip.
    /// Uses the clips in VehicleData; without them it builds simple synthetic sounds at startup,
    /// so every car makes noise before you have real audio files.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class VehicleAudio : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float engineVolume = 0.55f;
        [SerializeField, Range(0f, 1f)] float skidVolume = 0.5f;
        [Tooltip("Wheel slip where squealing starts and where it is at full volume.")]
        [SerializeField] float skidSlipStart = 0.35f;
        [SerializeField] float skidSlipFull = 0.9f;

        static AudioClip syntheticEngine;
        static AudioClip syntheticSkid;

        VehicleController vehicle;
        AudioSource engine;
        AudioSource skid;

        void Start()
        {
            vehicle = GetComponent<VehicleController>();
            VehicleData data = vehicle.Data;
            AudioClip engineClip = data != null && data.engineLoop != null ? data.engineLoop : SyntheticEngine();
            AudioClip skidClip = data != null && data.skidLoop != null ? data.skidLoop : SyntheticSkid();

            engine = MakeSource(engineClip);
            skid = MakeSource(skidClip);
            engine.Play();
        }

        AudioSource MakeSource(AudioClip clip)
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.minDistance = 4f;
            s.maxDistance = 90f;
            s.dopplerLevel = 0.3f;
            s.volume = 0f;
            return s;
        }

        void Update()
        {
            if (vehicle == null || vehicle.Data == null) return;
            VehicleData d = vehicle.Data;

            engine.pitch = Mathf.Lerp(d.engineMinPitch, d.engineMaxPitch, vehicle.EngineRpm01);
            engine.volume = vehicle.EngineDisabled ? 0f : engineVolume * Mathf.Lerp(0.45f, 1f, vehicle.DriveAmount);

            float squeal = vehicle.SpeedKmh > 7f ? Mathf.InverseLerp(skidSlipStart, skidSlipFull, vehicle.MaxWheelSlip) : 0f;
            skid.volume = skidVolume * squeal;
            // A silent looping source still uses a voice, so pause it when quiet.
            if (squeal > 0.01f && !skid.isPlaying) skid.Play();
            else if (squeal <= 0.01f && skid.isPlaying) skid.Pause();
        }

        /// <summary>One second of a buzzy four-cylinder hum at 55 Hz. Whole cycles only, so it loops without a click.</summary>
        static AudioClip SyntheticEngine()
        {
            if (syntheticEngine != null) return syntheticEngine;
            const int rate = 44100;
            var samples = new float[rate];
            var random = new System.Random(7);
            for (int i = 0; i < rate; i++)
            {
                float t = (float)i / rate;
                float tone = 0f;
                for (int k = 1; k <= 6; k++) tone += Mathf.Sin(2f * Mathf.PI * 55f * k * t) / k;
                float pulse = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 11f * t); // uneven firing
                float noise = (float)(random.NextDouble() * 2.0 - 1.0) * 0.05f;
                samples[i] = (tone * 0.35f * pulse) + noise;
            }
            syntheticEngine = AudioClip.Create("SyntheticEngine", rate, 1, rate, false);
            syntheticEngine.SetData(samples, 0);
            return syntheticEngine;
        }

        /// <summary>Filtered noise that sounds roughly like a tyre squeal.</summary>
        static AudioClip SyntheticSkid()
        {
            if (syntheticSkid != null) return syntheticSkid;
            const int rate = 44100;
            var samples = new float[rate];
            var random = new System.Random(11);
            float low = 0f, previous = 0f;
            for (int i = 0; i < rate; i++)
            {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                low += 0.15f * (white - low);            // remove the hiss
                float band = low - previous * 0.97f;     // remove the rumble
                previous = low;
                float whine = Mathf.Sin(2f * Mathf.PI * 880f * i / rate) * 0.15f;
                samples[i] = Mathf.Clamp(band * 2.2f + whine, -1f, 1f);
            }
            syntheticSkid = AudioClip.Create("SyntheticSkid", rate, 1, rate, false);
            syntheticSkid.SetData(samples, 0);
            return syntheticSkid;
        }
    }
}
