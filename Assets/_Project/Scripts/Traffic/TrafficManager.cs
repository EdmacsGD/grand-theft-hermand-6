using System.Collections.Generic;
using UnityEngine;

namespace GTH6
{
    /// <summary>
    /// Keeps up to Max Active traffic cars alive around the player using a fixed pool: all cars are
    /// created once at the start and then only switched on and off. Instantiate/Destroy during play
    /// causes stutters (garbage collection), a pool does not.
    /// Cars appear on a random road in a ring around the player, out of camera view, and vanish
    /// again when far away and out of view.
    /// </summary>
    public class TrafficManager : MonoBehaviour
    {
        [SerializeField] RoadNetwork network;
        [Tooltip("Empty = the object tagged Player.")]
        [SerializeField] Transform player;
        [Tooltip("Car prefabs with VehicleController and TrafficDriver. GTH6 > Traffic > Set Up Traffic In Scene makes these.")]
        [SerializeField] GameObject[] carPrefabs;

        [Header("Amounts")]
        [SerializeField, Min(1)] int poolSize = 25;
        [SerializeField, Min(0)] int maxActive = 25;

        [Header("Distances (m)")]
        [SerializeField] float spawnMinDistance = 60f;
        [SerializeField] float spawnMaxDistance = 150f;
        [SerializeField] float despawnDistance = 200f;

        [Header("Timing")]
        [Tooltip("Seconds between spawn/despawn passes. The checks are not needed every frame.")]
        [SerializeField] float updateInterval = 0.5f;
        [SerializeField, Min(1)] int spawnsPerPass = 2;

        readonly List<TrafficDriver> pool = new List<TrafficDriver>();
        readonly List<RoadNode> candidates = new List<RoadNode>();
        float timer;
        Camera cam;

        public int ActiveCount { get; private set; }

        void Start()
        {
            if (network == null) network = RoadNetwork.Instance;
            if (player == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) player = p.transform;
            }
            if (network == null || player == null || carPrefabs == null || carPrefabs.Length == 0)
            {
                Debug.LogWarning("[TrafficManager] Needs a RoadNetwork, a Player and at least one car prefab.", this);
                enabled = false;
                return;
            }

            var poolRoot = new GameObject("TrafficPool").transform;
            poolRoot.SetParent(transform, false);
            for (int i = 0; i < poolSize; i++)
            {
                GameObject prefab = carPrefabs[i % carPrefabs.Length];
                if (prefab == null) continue;
                GameObject car = Instantiate(prefab, poolRoot);
                car.SetActive(false); // parked in the pool until a spawn pass needs it
                car.name = prefab.name + "_" + i;

                TrafficDriver driver = car.GetComponent<TrafficDriver>();
                if (driver == null) driver = car.AddComponent<TrafficDriver>();
                pool.Add(driver);
            }
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = updateInterval;
            if (cam == null) cam = Camera.main;

            DespawnPass();
            SpawnPass();
        }

        void DespawnPass()
        {
            int active = 0;
            foreach (TrafficDriver driver in pool)
            {
                if (driver == null || !driver.gameObject.activeSelf) continue;
                VehicleController v = driver.Vehicle;

                // The player (or anyone not ours) is driving it: leave it alone.
                if (v.HasDriver && !ReferenceEquals(v.Driver, driver)) { active++; continue; }

                float distance = Vector3.Distance(v.transform.position, player.position);
                bool gone = distance > despawnDistance || driver.IsStuck || (!driver.IsDriving && distance > spawnMinDistance);
                if (gone && !IsVisible(v.transform.position)) Release(driver);
                else active++;
            }
            ActiveCount = active;
        }

        void SpawnPass()
        {
            if (ActiveCount >= maxActive) return;

            candidates.Clear();
            Vector3 p = player.position;
            foreach (RoadNode node in network.Nodes)
            {
                if (node == null || node.Links.Count == 0) continue;
                float d = Vector3.Distance(node.Position, p);
                if (d >= spawnMinDistance && d <= spawnMaxDistance && !IsVisible(node.Position)) candidates.Add(node);
            }
            if (candidates.Count == 0) return;

            for (int i = 0; i < spawnsPerPass && ActiveCount < maxActive; i++)
            {
                TrafficDriver driver = FreeCar();
                if (driver == null) return;
                RoadNode from = candidates[Random.Range(0, candidates.Count)];
                RoadLink link = from.Links[Random.Range(0, from.Links.Count)];
                if (link?.target != null && TrySpawn(driver, from, link)) ActiveCount++;
            }
        }

        TrafficDriver FreeCar()
        {
            // Random start index so the car types mix instead of always using the first prefab.
            int start = Random.Range(0, pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                TrafficDriver d = pool[(start + i) % pool.Count];
                if (d != null && !d.gameObject.activeSelf) return d;
            }
            return null;
        }

        bool TrySpawn(TrafficDriver driver, RoadNode from, RoadLink link)
        {
            float length = Vector3.Distance(from.Position, link.target.Position);
            if (length < 10f) return false;

            int lane = Random.Range(0, link.lanes);
            network.GetLane(from, link.target, lane, out Vector3 a, out Vector3 b);
            Vector3 direction = (b - a).normalized;
            Vector3 position = a + direction * Mathf.Min(6f, length * 0.3f);
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude < 0.001f) return false;
            Quaternion rotation = Quaternion.LookRotation(flat);

            // Do not spawn inside another car or a person.
            VehicleData data = driver.GetComponent<VehicleController>().Data;
            Vector3 size = data != null ? data.size : new Vector3(2f, 1.5f, 4.5f);
            Vector3 center = position + Vector3.up * (size.y * 0.5f + 0.3f);
            if (Physics.CheckBox(center, new Vector3(size.x * 0.5f + 0.5f, size.y * 0.4f, size.z * 0.5f + 1f), rotation, ~0, QueryTriggerInteraction.Ignore))
                return false;

            Transform t = driver.transform;
            t.SetPositionAndRotation(position + Vector3.up * 0.1f, rotation);
            driver.gameObject.SetActive(true);

            Rigidbody rb = driver.GetComponent<Rigidbody>();
            rb.linearVelocity = flat * (link.SpeedLimitMS * 0.5f); // arrive already rolling
            rb.angularVelocity = Vector3.zero;

            driver.Begin(network, from, link.target, lane);
            return true;
        }

        void Release(TrafficDriver driver)
        {
            driver.Eject();
            VehicleController v = driver.Vehicle;
            if (ReferenceEquals(v.Driver, driver) || v.Driver == null) v.SetDriver(null);
            VehicleDamage damage = driver.GetComponent<VehicleDamage>();
            if (damage != null) damage.Repair();
            driver.gameObject.SetActive(false);
        }

        bool IsVisible(Vector3 worldPosition)
        {
            if (cam == null) return false;
            Vector3 vp = cam.WorldToViewportPoint(worldPosition);
            return vp.z > 0f && vp.z < 400f && vp.x > -0.15f && vp.x < 1.15f && vp.y > -0.15f && vp.y < 1.15f;
        }
    }
}
