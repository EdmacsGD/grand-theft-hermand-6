using System.Collections.Generic;
using UnityEngine;

namespace GTH6
{
    public enum SignalState { Green, Yellow, Red }

    /// <summary>
    /// Runs every traffic light in the road graph (nodes with Has Traffic Light ticked).
    /// Roads into an intersection are split into two groups by direction: roads that line up
    /// (north-south) share a green, the crossing ones (east-west) get the other green.
    /// Cycle: group A green, yellow, all red, group B green, yellow, all red. Each intersection
    /// starts at a random point in the cycle so the city does not switch in sync.
    /// Optionally shows small coloured boxes so you can see the lights in the blockout.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class TrafficSignals : MonoBehaviour
    {
        public static TrafficSignals Instance { get; private set; }

        [SerializeField] RoadNetwork network;
        [SerializeField] float greenTime = 12f;
        [SerializeField] float yellowTime = 3f;
        [SerializeField] float allRedTime = 1.5f;
        [Tooltip("Spawn simple light boxes at each approach so you can see the signals.")]
        [SerializeField] bool showPlaceholderLights = true;

        class Intersection
        {
            public RoadNode node;
            public readonly HashSet<RoadNode> groupA = new HashSet<RoadNode>();
            public float timer;
            public readonly List<(RoadNode from, Renderer renderer)> lamps = new List<(RoadNode, Renderer)>();
        }

        readonly Dictionary<RoadNode, Intersection> intersections = new Dictionary<RoadNode, Intersection>();
        MaterialPropertyBlock block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        float CycleLength => 2f * (greenTime + yellowTime + allRedTime);

        void Awake()
        {
            Instance = this;
            block = new MaterialPropertyBlock();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (network == null) network = RoadNetwork.Instance;
            if (network == null)
            {
                Debug.LogWarning("[TrafficSignals] No RoadNetwork in the scene.", this);
                enabled = false;
                return;
            }
            Build();
        }

        /// <summary>Re-reads the graph. Call after editing roads at runtime.</summary>
        public void Build()
        {
            foreach (Intersection old in intersections.Values)
                foreach (var lamp in old.lamps) if (lamp.renderer != null) Destroy(lamp.renderer.gameObject);
            intersections.Clear();

            foreach (RoadNode node in network.Nodes)
            {
                if (node == null || !node.hasTrafficLight) continue;
                var x = new Intersection { node = node, timer = Random.Range(0f, CycleLength) };

                // Incoming approaches: neighbours that have a link into this node.
                Vector3 reference = Vector3.zero;
                foreach (RoadNode from in network.NeighboursOf(node))
                {
                    if (!from.HasLinkTo(node)) continue;
                    Vector3 dir = node.Position - from.Position;
                    dir.y = 0f;
                    dir.Normalize();
                    if (reference == Vector3.zero) reference = dir;
                    // Same or opposite direction as the first approach (within 45 degrees) = group A.
                    if (Mathf.Abs(Vector3.Dot(dir, reference)) > 0.707f) x.groupA.Add(from);
                    if (showPlaceholderLights) x.lamps.Add((from, MakeLamp(node, from)));
                }
                intersections[node] = x;
            }
        }

        Renderer MakeLamp(RoadNode node, RoadNode from)
        {
            Vector3 dir = (node.Position - from.Position);
            dir.y = 0f;
            dir.Normalize();
            float pad = RoadMeshBuilder.PadRadius(network, node);
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            Vector3 pos = node.Position - dir * (pad + 1f) + right * (network.HalfWidth(from, node) + 0.6f) + Vector3.up * 3f;

            GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lamp.name = $"Signal_{node.name}_from_{from.name}";
            Destroy(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(transform, true);
            lamp.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-dir));
            lamp.transform.localScale = new Vector3(0.4f, 1f, 0.3f);
            return lamp.GetComponent<Renderer>();
        }

        void Update()
        {
            float cycle = CycleLength;
            foreach (Intersection x in intersections.Values)
            {
                x.timer = (x.timer + Time.deltaTime) % cycle;
                foreach ((RoadNode from, Renderer r) in x.lamps)
                {
                    if (r == null) continue;
                    SignalState s = StateFor(x, from);
                    Color c = s == SignalState.Green ? new Color(0.1f, 0.9f, 0.2f) : s == SignalState.Yellow ? new Color(1f, 0.8f, 0.1f) : new Color(0.9f, 0.1f, 0.1f);
                    block.SetColor(BaseColorId, c);
                    block.SetColor(ColorId, c);
                    r.SetPropertyBlock(block);
                }
            }
        }

        SignalState StateFor(Intersection x, RoadNode from)
        {
            float half = greenTime + yellowTime + allRedTime;
            bool inA = x.groupA.Contains(from);
            float t = inA ? x.timer : (x.timer + half) % CycleLength; // group B runs half a cycle behind
            if (t < greenTime) return SignalState.Green;
            if (t < greenTime + yellowTime) return SignalState.Yellow;
            return SignalState.Red;
        }

        /// <summary>Signal for a car travelling from "from" into "intersection". Green when there is no light.</summary>
        public SignalState GetState(RoadNode from, RoadNode intersection)
        {
            if (intersection == null || !intersections.TryGetValue(intersection, out Intersection x)) return SignalState.Green;
            return StateFor(x, from);
        }

        /// <summary>Distance before the node centre where cars should stop.</summary>
        public float StopDistance(RoadNode intersection) =>
            network != null ? RoadMeshBuilder.PadRadius(network, intersection) + 1.5f : 8f;
    }
}
