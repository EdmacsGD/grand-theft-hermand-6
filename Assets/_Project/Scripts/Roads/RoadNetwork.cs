using System.Collections.Generic;
using UnityEngine;

namespace GTH6
{
    /// <summary>
    /// The whole road graph. Put it on an empty "RoadNetwork" object; every RoadNode is a child.
    /// Gives lane positions (driving on the right, like Norway), nearest-node lookup and A*
    /// fastest-route search. Traffic (phase 4) and the GPS (phase 8) both use this one graph,
    /// so the route on the minimap always matches roads the cars actually drive on.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class RoadNetwork : MonoBehaviour
    {
        public static RoadNetwork Instance { get; private set; }

        [Header("Lanes")]
        [Tooltip("Width of one lane in metres.")]
        [Min(1f)] public float laneWidth = 3.25f;
        [Tooltip("Extra road surface outside the outer lanes, in metres.")]
        [Min(0f)] public float shoulderWidth = 0.5f;

        [Header("Defaults for new links (editor tool)")]
        [Min(1)] public int defaultLanes = 1;
        [Min(5f)] public float defaultSpeedLimit = 50f;
        public bool defaultTwoWay = true;

        [Header("Road surface mesh")]
        public Material roadMaterial;
        [Tooltip("Lift above the node height so the road does not flicker against the ground.")]
        public float surfaceHeightOffset = 0.03f;
        [Range(8, 32)] public int intersectionSegments = 16;

        [Header("Gizmos")]
        public bool drawGizmos = true;
        public bool drawLanes = true;

        readonly List<RoadNode> nodes = new List<RoadNode>();
        readonly Dictionary<RoadNode, int> indexOf = new Dictionary<RoadNode, int>();
        readonly Dictionary<RoadNode, HashSet<RoadNode>> neighbours = new Dictionary<RoadNode, HashSet<RoadNode>>();
        float maxSpeedMS = 1f;

        // A* scratch arrays, reused so pathfinding does not allocate garbage every call.
        float[] gScore = new float[0];
        int[] cameFrom = new int[0];
        bool[] closed = new bool[0];
        readonly MinHeap open = new MinHeap();

        public IReadOnlyList<RoadNode> Nodes => nodes;

        void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning("[RoadNetwork] More than one RoadNetwork in the scene. Using the newest.", this);
            Instance = this;
            Rebuild();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Re-reads all child nodes. Call after adding or removing nodes at runtime.
        /// The editor tool calls it automatically.
        /// </summary>
        public void Rebuild()
        {
            nodes.Clear();
            indexOf.Clear();
            neighbours.Clear();
            maxSpeedMS = 1f;

            GetComponentsInChildren(true, nodes);
            for (int i = 0; i < nodes.Count; i++)
            {
                indexOf[nodes[i]] = i;
                neighbours[nodes[i]] = new HashSet<RoadNode>();
            }

            foreach (RoadNode node in nodes)
            {
                foreach (RoadLink link in node.Links)
                {
                    if (link == null || link.target == null || !neighbours.ContainsKey(link.target)) continue;
                    neighbours[node].Add(link.target);
                    neighbours[link.target].Add(node);
                    maxSpeedMS = Mathf.Max(maxSpeedMS, link.SpeedLimitMS);
                }
            }

            if (gScore.Length != nodes.Count)
            {
                gScore = new float[nodes.Count];
                cameFrom = new int[nodes.Count];
                closed = new bool[nodes.Count];
            }
        }

        // ---------------- Graph queries ----------------

        /// <summary>Number of different nodes this node touches (incoming or outgoing). 1 = dead end, 3+ = intersection.</summary>
        public int Degree(RoadNode node) => node != null && neighbours.TryGetValue(node, out var set) ? set.Count : 0;

        public IEnumerable<RoadNode> NeighboursOf(RoadNode node)
        {
            if (node != null && neighbours.TryGetValue(node, out var set)) return set;
            return System.Array.Empty<RoadNode>();
        }

        public RoadNode FindNearestNode(Vector3 position)
        {
            RoadNode best = null;
            float bestDistance = float.MaxValue;
            foreach (RoadNode node in nodes)
            {
                if (node == null) continue;
                float d = (node.Position - position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = node;
                }
            }
            return best;
        }

        // ---------------- Lanes ----------------

        /// <summary>Width of road surface on each side of the node line, for the mesh and for gizmos.</summary>
        public float HalfWidth(RoadNode a, RoadNode b)
        {
            RoadLink ab = a.GetLinkTo(b);
            RoadLink ba = b.GetLinkTo(a);
            float lanesOneSide;
            if (ab != null && ba != null) lanesOneSide = Mathf.Max(ab.lanes, ba.lanes);
            else lanesOneSide = (ab != null ? ab.lanes : ba != null ? ba.lanes : 1) * 0.5f;
            return lanesOneSide * laneWidth + shoulderWidth;
        }

        /// <summary>
        /// Sideways offset of a lane centre from the node line, for travel from "from" to "to".
        /// Lane 0 is the one nearest the centre line (the overtaking lane), the highest index is the
        /// kerb lane. Two-way roads put each direction on its right side; one-way roads are centred.
        /// </summary>
        public Vector3 LaneOffset(RoadNode from, RoadNode to, int laneIndex)
        {
            RoadLink link = from.GetLinkTo(to);
            int lanes = link != null ? link.lanes : 1;
            laneIndex = Mathf.Clamp(laneIndex, 0, lanes - 1);

            Vector3 right = RightOf(from.Position, to.Position);
            bool twoWay = to.HasLinkTo(from);
            float firstLaneEdge = twoWay ? 0f : -lanes * laneWidth * 0.5f;
            return right * (firstLaneEdge + (laneIndex + 0.5f) * laneWidth);
        }

        public void GetLane(RoadNode from, RoadNode to, int laneIndex, out Vector3 start, out Vector3 end)
        {
            Vector3 offset = LaneOffset(from, to, laneIndex);
            start = from.Position + offset;
            end = to.Position + offset;
        }

        public static Vector3 RightOf(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return Vector3.right;
            return Vector3.Cross(Vector3.up, direction.normalized);
        }

        // ---------------- A* ----------------

        /// <summary>Fastest route between the nodes nearest to two world positions.</summary>
        public bool FindPath(Vector3 from, Vector3 to, List<RoadNode> result)
        {
            return FindPath(FindNearestNode(from), FindNearestNode(to), result);
        }

        /// <summary>
        /// A* over the graph. Cost = driving time (length / speed limit), so it prefers a fast main
        /// road over a slightly shorter back street, like a real GPS. Fills result start..goal.
        /// Returns false if no route exists (for example a one-way street in the wrong direction).
        /// </summary>
        public bool FindPath(RoadNode start, RoadNode goal, List<RoadNode> result)
        {
            result.Clear();
            if (start == null || goal == null) return false;
            if (!indexOf.TryGetValue(start, out int startIndex) || !indexOf.TryGetValue(goal, out int goalIndex)) return false;

            for (int i = 0; i < nodes.Count; i++)
            {
                gScore[i] = float.MaxValue;
                cameFrom[i] = -1;
                closed[i] = false;
            }

            open.Clear();
            gScore[startIndex] = 0f;
            open.Push(startIndex, Heuristic(start, goal));

            while (open.Count > 0)
            {
                int current = open.Pop();
                if (closed[current]) continue; // stale heap entry
                if (current == goalIndex)
                {
                    for (int i = goalIndex; i != -1; i = cameFrom[i]) result.Add(nodes[i]);
                    result.Reverse();
                    return true;
                }
                closed[current] = true;

                RoadNode node = nodes[current];
                foreach (RoadLink link in node.Links)
                {
                    if (link == null || link.target == null) continue;
                    if (!indexOf.TryGetValue(link.target, out int next) || closed[next]) continue;

                    float travelTime = Vector3.Distance(node.Position, link.target.Position) / link.SpeedLimitMS;
                    float tentative = gScore[current] + travelTime;
                    if (tentative < gScore[next])
                    {
                        gScore[next] = tentative;
                        cameFrom[next] = current;
                        open.Push(next, tentative + Heuristic(link.target, goal));
                    }
                }
            }

            return false;
        }

        // Straight-line time at the fastest speed in the network: never overestimates, so A* stays exact.
        float Heuristic(RoadNode a, RoadNode b) => Vector3.Distance(a.Position, b.Position) / maxSpeedMS;

        /// <summary>Small binary min-heap. Unity's .NET profile has no PriorityQueue class.</summary>
        class MinHeap
        {
            readonly List<int> items = new List<int>();
            readonly List<float> priorities = new List<float>();

            public int Count => items.Count;

            public void Clear()
            {
                items.Clear();
                priorities.Clear();
            }

            public void Push(int item, float priority)
            {
                items.Add(item);
                priorities.Add(priority);
                int i = items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (priorities[parent] <= priorities[i]) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            public int Pop()
            {
                int top = items[0];
                int last = items.Count - 1;
                items[0] = items[last];
                priorities[0] = priorities[last];
                items.RemoveAt(last);
                priorities.RemoveAt(last);

                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1;
                    int right = left + 1;
                    int smallest = i;
                    if (left < items.Count && priorities[left] < priorities[smallest]) smallest = left;
                    if (right < items.Count && priorities[right] < priorities[smallest]) smallest = right;
                    if (smallest == i) break;
                    Swap(i, smallest);
                    i = smallest;
                }
                return top;
            }

            void Swap(int a, int b)
            {
                (items[a], items[b]) = (items[b], items[a]);
                (priorities[a], priorities[b]) = (priorities[b], priorities[a]);
            }
        }

        // ---------------- Gizmos ----------------

        void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            if (!Application.isPlaying) Rebuild();

            // Node spheres are drawn by each RoadNode itself, so clicking a sphere selects that node.
            foreach (RoadNode node in nodes)
            {
                if (node == null) continue;
                foreach (RoadLink link in node.Links)
                {
                    if (link == null || link.target == null) continue;
                    Gizmos.color = SpeedColor(link.speedLimit);

                    if (!drawLanes)
                    {
                        Gizmos.DrawLine(node.Position + Vector3.up * 0.3f, link.target.Position + Vector3.up * 0.3f);
                        continue;
                    }

                    for (int lane = 0; lane < link.lanes; lane++)
                    {
                        GetLane(node, link.target, lane, out Vector3 a, out Vector3 b);
                        a += Vector3.up * 0.3f;
                        b += Vector3.up * 0.3f;
                        Gizmos.DrawLine(a, b);
                        DrawArrow(a, b);
                    }
                }
            }
        }

        static void DrawArrow(Vector3 a, Vector3 b)
        {
            Vector3 direction = b - a;
            if (direction.sqrMagnitude < 1f) return;
            Vector3 tip = Vector3.Lerp(a, b, 0.6f);
            Vector3 back = -direction.normalized * 1.5f;
            Vector3 side = Vector3.Cross(Vector3.up, direction.normalized) * 0.8f;
            Gizmos.DrawLine(tip, tip + back + side);
            Gizmos.DrawLine(tip, tip + back - side);
        }

        // Green = slow street, yellow = 50, red = highway.
        static Color SpeedColor(float kmh)
        {
            float t = Mathf.InverseLerp(30f, 90f, kmh);
            return t < 0.5f ? Color.Lerp(Color.green, Color.yellow, t * 2f) : Color.Lerp(Color.yellow, Color.red, (t - 0.5f) * 2f);
        }
    }
}
