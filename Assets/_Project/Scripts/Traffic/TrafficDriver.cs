using UnityEngine;

namespace GTH6
{
    /// <summary>
    /// AI driver for a traffic car. Follows its lane along the road graph, picks a random road at
    /// each intersection, slows for corners, stops at red lights and keeps its distance with a box
    /// sensor in front. If a person (usually you) blocks the road it stops and honks.
    /// It only ever calls VehicleController.SetInput, the same as the player.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class TrafficDriver : MonoBehaviour, IVehicleAI
    {
        [Header("Steering")]
        [SerializeField] float lookAheadMin = 5f;
        [SerializeField] float lookAheadMax = 16f;
        [Tooltip("Extra look-ahead metres per m/s of speed. Looking further ahead at speed = smoother lines.")]
        [SerializeField] float lookAheadPerSpeed = 0.7f;

        [Header("Speed")]
        [Tooltip("Planned braking in m/s per second. 4 is a calm driver.")]
        [SerializeField] float comfortableDecel = 4f;
        [Tooltip("Metres kept to the car in front when stopped.")]
        [SerializeField] float minGap = 3f;
        [SerializeField] float turnSpeedKmh = 22f;
        [Tooltip("Each driver drives at a random share of the speed limit in this range.")]
        [SerializeField] Vector2 speedFactorRange = new Vector2(0.85f, 1.05f);

        [Header("Sensing")]
        [SerializeField] LayerMask obstacleMask = ~0;
        [SerializeField] float honkAfterSeconds = 3f;

        [Header("Testing")]
        [Tooltip("Start driving from the nearest road on Play, without a TrafficManager.")]
        [SerializeField] bool autoStart;

        VehicleController vehicle;
        RoadNetwork network;
        TrafficSignals signals;
        RoadNode from, to, next;
        int lane;
        float speedFactor = 1f;
        float blockedTimer;
        float stuckTimer;
        float honkCooldown;
        Vector3 debugTarget;
        AudioSource horn;
        static AudioClip hornClip;
        readonly RaycastHit[] hits = new RaycastHit[8];

        /// <summary>True when the car has not moved for a while although it wants to. The manager recycles it.</summary>
        public bool IsStuck => stuckTimer > 12f;
        public bool IsDriving => enabled && from != null && to != null;
        public VehicleController Vehicle => vehicle;

        void Awake()
        {
            vehicle = GetComponent<VehicleController>();
        }

        void Start()
        {
            if (autoStart && from == null)
            {
                RoadNetwork net = RoadNetwork.Instance;
                RoadNode nearest = net != null ? net.FindNearestNode(transform.position) : null;
                if (nearest != null && nearest.Links.Count > 0) Begin(net, nearest, nearest.Links[Random.Range(0, nearest.Links.Count)].target, 0);
            }
            else if (from == null)
            {
                enabled = false; // parked until a TrafficManager calls Begin
            }
        }

        /// <summary>Start driving along the link a -> b in the given lane.</summary>
        public void Begin(RoadNetwork net, RoadNode a, RoadNode b, int laneIndex)
        {
            network = net;
            signals = TrafficSignals.Instance;
            from = a;
            to = b;
            RoadLink link = a.GetLinkTo(b);
            lane = link != null ? Mathf.Clamp(laneIndex, 0, link.lanes - 1) : 0;
            next = ChooseNext(a, b);
            speedFactor = Random.Range(speedFactorRange.x, speedFactorRange.y);
            blockedTimer = stuckTimer = 0f;
            enabled = true;
            vehicle.SetDriver(this);
        }

        /// <summary>The player took the car, or the manager is recycling it.</summary>
        public void Eject()
        {
            from = to = next = null;
            enabled = false;
        }

        void FixedUpdate()
        {
            if (from == null || to == null || network == null)
            {
                vehicle.SetInput(0f, 0f, 0f, true);
                return;
            }
            if (!ReferenceEquals(vehicle.Driver, this)) { Eject(); return; } // someone else is driving now

            float dt = Time.fixedDeltaTime;
            Vector3 position = transform.position;

            // Move on to the next road once we pass the end of this one.
            network.GetLane(from, to, lane, out Vector3 a, out Vector3 b);
            if (Progress(position, a, b) >= 1f || Flat(b - position).magnitude < 2f)
            {
                Advance();
                if (to == null) { vehicle.SetInput(0f, 0f, 0f, true); return; }
                network.GetLane(from, to, lane, out a, out b);
            }

            float speed = Mathf.Max(0f, vehicle.ForwardSpeed);
            float look = Mathf.Clamp(lookAheadMin + speed * lookAheadPerSpeed, lookAheadMin, lookAheadMax);
            debugTarget = PointAlongPath(position, a, b, look);

            Vector3 local = transform.InverseTransformPoint(debugTarget);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float steer = Mathf.Clamp(angle / Mathf.Max(5f, vehicle.Data.maxSteerAngle), -1f, 1f);

            float targetSpeed = TargetSpeed(position, speed, out bool personBlocking);

            // Honk at whoever is standing in the road.
            honkCooldown -= dt;
            blockedTimer = personBlocking && speed < 1f ? blockedTimer + dt : 0f;
            if (blockedTimer > honkAfterSeconds && honkCooldown <= 0f) Honk();

            stuckTimer = speed < 0.3f && targetSpeed > 1f ? stuckTimer + dt : 0f;

            float error = targetSpeed - speed;
            if (targetSpeed < 0.3f && speed < 1f)
                vehicle.SetInput(0f, 0f, steer, true); // hold still with the handbrake (brake at a standstill would mean reverse)
            else if (error > 0f)
                vehicle.SetInput(Mathf.Clamp01(error * 0.35f + 0.1f), 0f, steer, false);
            else if (speed > 1.5f)
                vehicle.SetInput(0f, Mathf.Clamp01(-error * 0.3f), steer, false);
            else
                vehicle.SetInput(0f, 0f, steer, targetSpeed < 0.3f);
        }

        float TargetSpeed(Vector3 position, float speed, out bool personBlocking)
        {
            personBlocking = false;
            RoadLink link = from.GetLinkTo(to);
            float limit = (link != null ? link.SpeedLimitMS : 50f / 3.6f) * speedFactor;
            float target = limit;
            float toNode = Flat(to.Position - position).magnitude;

            // Corner ahead: the sharper the turn, the slower we arrive.
            if (next != null)
            {
                float turn = Vector3.Angle(Flat(to.Position - from.Position), Flat(next.Position - to.Position));
                float cornerSpeed = Mathf.Lerp(limit, turnSpeedKmh / 3.6f, Mathf.InverseLerp(20f, 90f, turn));
                target = Mathf.Min(target, AllowedSpeed(toNode - 4f, cornerSpeed));
            }
            else
            {
                target = Mathf.Min(target, AllowedSpeed(toNode - 3f, 0f)); // nowhere to go: stop at the end
            }

            // Traffic light.
            if (signals != null)
            {
                SignalState state = signals.GetState(from, to);
                if (state != SignalState.Green)
                {
                    float toStopLine = toNode - signals.StopDistance(to);
                    float brakingDistance = speed * speed / (2f * comfortableDecel * 1.5f);
                    bool tooLateToStop = state == SignalState.Yellow && toStopLine < brakingDistance;
                    if (toStopLine > -1f && !tooLateToStop) target = Mathf.Min(target, AllowedSpeed(toStopLine - 0.5f, 0f));
                }
            }

            // Whatever is in front of us.
            float scan = minGap + speed * speed / (2f * comfortableDecel) + 6f;
            if (Probe(scan, out float distance, out personBlocking))
                target = Mathf.Min(target, AllowedSpeed(distance - minGap, 0f));

            return target;
        }

        /// <summary>Fastest speed from which we can still slow to endSpeed within distance.</summary>
        float AllowedSpeed(float distance, float endSpeed) =>
            Mathf.Sqrt(endSpeed * endSpeed + 2f * comfortableDecel * Mathf.Max(0f, distance));

        bool Probe(float range, out float nearest, out bool isPerson)
        {
            nearest = float.MaxValue;
            isPerson = false;
            Vector3 size = vehicle.Data.size;
            Vector3 origin = transform.position + transform.up * (size.y * 0.5f) + transform.forward * (size.z * 0.5f + 0.3f);
            Vector3 direction = Flat(debugTarget - origin);
            direction = direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
            Vector3 halfExtents = new Vector3(size.x * 0.45f, size.y * 0.3f, 0.1f);

            int count = Physics.BoxCastNonAlloc(origin, halfExtents, direction, hits, Quaternion.LookRotation(direction), range, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider.transform.IsChildOf(transform)) continue; // our own body
                if (hit.normal.y > 0.6f) continue;                          // the road rising ahead, not an obstacle
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    isPerson = hit.collider is CharacterController || hit.collider.GetComponentInParent<RagdollController>() != null;
                }
            }
            return nearest < float.MaxValue;
        }

        void Advance()
        {
            from = to;
            to = next;
            next = to != null ? ChooseNext(from, to) : null;
            if (to != null)
            {
                RoadLink link = from.GetLinkTo(to);
                lane = link != null ? Mathf.Min(lane, link.lanes - 1) : 0;
            }
        }

        /// <summary>Random road out of "node", never straight back unless it is a dead end.</summary>
        static RoadNode ChooseNext(RoadNode cameFrom, RoadNode node)
        {
            int options = 0;
            foreach (RoadLink l in node.Links) if (l?.target != null && l.target != cameFrom) options++;

            if (options == 0) return node.HasLinkTo(cameFrom) ? cameFrom : null; // dead end: U-turn

            int pick = Random.Range(0, options);
            foreach (RoadLink l in node.Links)
            {
                if (l?.target == null || l.target == cameFrom) continue;
                if (pick-- == 0) return l.target;
            }
            return null;
        }

        /// <summary>A point "distance" metres ahead along our lane, continuing into the next road's lane.</summary>
        Vector3 PointAlongPath(Vector3 position, Vector3 a, Vector3 b, float distance)
        {
            float t = Mathf.Clamp01(Progress(position, a, b));
            Vector3 p = Vector3.Lerp(a, b, t);
            float left = Vector3.Distance(p, b);
            if (distance <= left || next == null) return p + (b - a).normalized * distance;

            distance -= left;
            RoadLink nextLink = to.GetLinkTo(next);
            int nextLane = nextLink != null ? Mathf.Min(lane, nextLink.lanes - 1) : 0;
            network.GetLane(to, next, nextLane, out Vector3 c, out Vector3 d);

            float bridge = Vector3.Distance(b, c); // short hop between the two lanes across the junction
            if (distance <= bridge) return Vector3.Lerp(b, c, distance / Mathf.Max(bridge, 0.01f));
            distance -= bridge;
            return c + (d - c).normalized * Mathf.Min(distance, Vector3.Distance(c, d));
        }

        static float Progress(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = Flat(b - a);
            float lengthSq = ab.sqrMagnitude;
            return lengthSq < 0.0001f ? 1f : Vector3.Dot(Flat(p - a), ab) / lengthSq;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void Honk()
        {
            honkCooldown = 4f;
            blockedTimer = 0f;
            if (horn == null)
            {
                horn = gameObject.AddComponent<AudioSource>();
                horn.spatialBlend = 1f;
                horn.minDistance = 5f;
                horn.maxDistance = 60f;
                horn.playOnAwake = false;
                horn.clip = HornClip();
            }
            horn.Play();
        }

        /// <summary>Two-tone car horn, generated once.</summary>
        static AudioClip HornClip()
        {
            if (hornClip != null) return hornClip;
            const int rate = 44100;
            int length = (int)(rate * 0.6f);
            var samples = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / rate;
                float envelope = Mathf.Clamp01(t * 30f) * Mathf.Clamp01((0.6f - t) * 20f);
                float tone = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 400f * t)) * 0.5f + Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 500f * t)) * 0.5f;
                samples[i] = tone * 0.25f * envelope;
            }
            hornClip = AudioClip.Create("Horn", length, 1, rate, false);
            hornClip.SetData(samples, 0);
            return hornClip;
        }

        void OnDrawGizmosSelected()
        {
            if (!IsDriving) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position + Vector3.up, debugTarget + Vector3.up);
            Gizmos.DrawWireSphere(debugTarget + Vector3.up, 0.5f);
        }
    }
}
