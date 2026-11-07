using UnityEngine;

namespace GTH6
{
    /// <summary>One car radio station: a name and a playlist. Right click > Create > GTH6 > Radio Station.</summary>
    [CreateAssetMenu(fileName = "NewStation", menuName = "GTH6/Radio Station")]
    public class RadioStation : ScriptableObject
    {
        public string stationName = "Fjord FM";
        [Tooltip("Royalty-free tracks. They play in this order and loop.")]
        public AudioClip[] tracks;
        [Range(0f, 1f)] public float volume = 0.8f;

        /// <summary>Length of the whole playlist in seconds.</summary>
        public float TotalLength
        {
            get
            {
                float total = 0f;
                if (tracks == null) return 0f;
                foreach (AudioClip clip in tracks) if (clip != null) total += clip.length;
                return total;
            }
        }
    }
}
