using UnityEngine;
using UnityEngine.InputSystem;

namespace GTH6
{
    /// <summary>
    /// Car radio on the player. Stations keep "broadcasting" while you are not listening: switching
    /// back to a station jumps to where it would be now, like real radio (and like GTA).
    /// Q / E (or d-pad left/right) change station while driving. The last slot is "Radio Off".
    /// </summary>
    public class CarRadio : MonoBehaviour
    {
        [SerializeField] RadioStation[] stations;
        [Tooltip("Start on a random station when getting in a car.")]
        [SerializeField] bool randomStartStation = true;

        AudioSource source;
        InputAction next, previous;
        int stationIndex;
        bool playing;
        float stationTimer;

        public string CurrentStationName =>
            stations == null || stationIndex >= stations.Length || stations[stationIndex] == null ? "Radio Off" : stations[stationIndex].stationName;

        /// <summary>Raised when the station changes, so the HUD can show its name (phase 8).</summary>
        public event System.Action<string> StationChanged;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // radio is "inside your head", not positioned in the world
            source.loop = false;

            next = new InputAction("RadioNext", InputActionType.Button);
            next.AddBinding("<Keyboard>/e");
            next.AddBinding("<Gamepad>/dpad/right");
            previous = new InputAction("RadioPrevious", InputActionType.Button);
            previous.AddBinding("<Keyboard>/q");
            previous.AddBinding("<Gamepad>/dpad/left");

            int count = stations != null ? stations.Length : 0;
            stationIndex = randomStartStation && count > 0 ? Random.Range(0, count) : 0;
        }

        void OnEnable() { next.Enable(); previous.Enable(); }
        void OnDisable() { next.Disable(); previous.Disable(); }
        void OnDestroy() { next.Dispose(); previous.Dispose(); }

        /// <summary>Turns the radio on when entering a car and off when leaving.</summary>
        public void SetPlaying(bool on)
        {
            playing = on;
            if (on) PlayCurrent();
            else source.Stop();
        }

        void Update()
        {
            if (!playing) return;

            int slots = (stations != null ? stations.Length : 0) + 1; // + Radio Off
            if (next.WasPressedThisFrame()) ChangeStation((stationIndex + 1) % slots);
            if (previous.WasPressedThisFrame()) ChangeStation((stationIndex - 1 + slots) % slots);

            // Track finished: move on to the station's next track.
            if (!source.isPlaying && CurrentStation() != null)
            {
                stationTimer -= Time.unscaledDeltaTime;
                if (stationTimer <= 0f) PlayCurrent();
            }
        }

        void ChangeStation(int index)
        {
            stationIndex = index;
            PlayCurrent();
            StationChanged?.Invoke(CurrentStationName);
        }

        RadioStation CurrentStation() =>
            stations != null && stationIndex < stations.Length ? stations[stationIndex] : null;

        void PlayCurrent()
        {
            source.Stop();
            RadioStation station = CurrentStation();
            if (station == null || station.tracks == null || station.TotalLength <= 0f) return;

            // Where the station would be right now if it had been playing since the game started.
            // Each station gets a different offset so they do not all start their playlists together.
            float offset = (station.stationName.Length * 37 + station.TotalLength.GetHashCode() & 0xffff) * 7.31f;
            float t = (Time.unscaledTime + offset) % station.TotalLength;

            foreach (AudioClip clip in station.tracks)
            {
                if (clip == null) continue;
                if (t < clip.length)
                {
                    source.clip = clip;
                    source.volume = station.volume;
                    source.time = Mathf.Clamp(t, 0f, clip.length - 0.05f);
                    source.Play();
                    stationTimer = 0.1f;
                    return;
                }
                t -= clip.length;
            }
        }
    }
}
