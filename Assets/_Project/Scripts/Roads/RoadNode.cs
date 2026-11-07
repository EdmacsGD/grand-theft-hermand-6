using System;
using System.Collections.Generic;
using UnityEngine;

namespace GTH6
{
    /// <summary>
    /// One-directional connection from a node to another node. A normal two-way street is two
    /// links: A to B and B to A. A one-way street is a single link.
    /// </summary>
    [Serializable]
    public class RoadLink
    {
        public RoadNode target;
        [Tooltip("Lanes in THIS direction.")]
        [Min(1)] public int lanes = 1;
        [Tooltip("km/h. Traffic drives at this speed, the GPS uses it for the fastest route.")]
        [Min(5f)] public float speedLimit = 50f;

        public RoadLink(RoadNode target, int lanes, float speedLimit)
        {
            this.target = target;
            this.lanes = Mathf.Max(1, lanes);
            this.speedLimit = Mathf.Max(5f, speedLimit);
        }

        /// <summary>Metres per second, for physics and AI code.</summary>
        public float SpeedLimitMS => speedLimit / 3.6f;
    }

    /// <summary>
    /// A point in the road graph: a bend, an intersection or a dead end. Lives as a child of a
    /// RoadNetwork. Traffic AI (phase 4) drives between nodes, the GPS (phase 8) runs A* over them.
    /// </summary>
    public class RoadNode : MonoBehaviour
    {
        [SerializeField] List<RoadLink> links = new List<RoadLink>();

        [Tooltip("Phase 4: cars stop here on red. Set it on intersections.")]
        public bool hasTrafficLight;

        public IReadOnlyList<RoadLink> Links => links;
        public Vector3 Position => transform.position;

        public RoadLink GetLinkTo(RoadNode other)
        {
            foreach (RoadLink link in links)
                if (link != null && link.target == other) return link;
            return null;
        }

        public bool HasLinkTo(RoadNode other) => GetLinkTo(other) != null;

        /// <summary>Adds a link, or updates it if it already exists.</summary>
        public RoadLink SetLink(RoadNode other, int lanes, float speedLimit)
        {
            if (other == null || other == this) return null;
            RoadLink existing = GetLinkTo(other);
            if (existing != null)
            {
                existing.lanes = Mathf.Max(1, lanes);
                existing.speedLimit = Mathf.Max(5f, speedLimit);
                return existing;
            }

            var link = new RoadLink(other, lanes, speedLimit);
            links.Add(link);
            return link;
        }

        public bool RemoveLink(RoadNode other) => links.RemoveAll(l => l != null && l.target == other) > 0;

        /// <summary>Removes links whose target was deleted. Returns how many were removed.</summary>
        public int RemoveBrokenLinks() => links.RemoveAll(l => l == null || l.target == null || l.target == this);

        void OnValidate() => RemoveBrokenLinks();

        // Drawn here (not by RoadNetwork) because Unity selects whichever object drew the gizmo you click.
        void OnDrawGizmos()
        {
            RoadNetwork network = GetComponentInParent<RoadNetwork>();
            if (network != null && !network.drawGizmos) return;

            int degree = network != null ? network.Degree(this) : links.Count;
            if (hasTrafficLight) Gizmos.color = Color.cyan;
            else if (degree <= 1) Gizmos.color = Color.red;
            else if (degree >= 3) Gizmos.color = Color.yellow;
            else Gizmos.color = Color.white;
            Gizmos.DrawSphere(Position + Vector3.up * 0.5f, degree >= 3 ? 1.2f : 0.8f);
        }
    }
}
