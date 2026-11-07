using System.Collections.Generic;
using UnityEngine;

namespace GTH6
{
    /// <summary>
    /// Test tool for phase 2: drag two empty objects around the Scene view and watch the fastest
    /// route between them draw in magenta. This is the exact search the GPS will use in phase 8.
    /// Works in edit mode, no Play needed.
    /// </summary>
    public class RoadPathTester : MonoBehaviour
    {
        [SerializeField] RoadNetwork network;
        [SerializeField] Transform start;
        [SerializeField] Transform goal;
        [SerializeField] Color pathColor = Color.magenta;

        readonly List<RoadNode> path = new List<RoadNode>();

        public float LastRouteLength { get; private set; }

        void OnDrawGizmos()
        {
            if (network == null || start == null || goal == null) return;
            if (!Application.isPlaying) network.Rebuild();

            Gizmos.color = pathColor;
            if (!network.FindPath(start.position, goal.position, path))
            {
                // No route: draw a thin red line between the two markers.
                Gizmos.color = Color.red;
                Gizmos.DrawLine(start.position, goal.position);
                LastRouteLength = 0f;
                return;
            }

            LastRouteLength = 0f;
            Vector3 lift = Vector3.up * 1.5f;
            Gizmos.DrawLine(start.position, path[0].Position + lift);
            for (int i = 0; i < path.Count - 1; i++)
            {
                Gizmos.DrawLine(path[i].Position + lift, path[i + 1].Position + lift);
                LastRouteLength += Vector3.Distance(path[i].Position, path[i + 1].Position);
            }
            Gizmos.DrawLine(path[path.Count - 1].Position + lift, goal.position);
            Gizmos.DrawWireSphere(start.position, 2f);
            Gizmos.DrawWireSphere(goal.position, 2f);
        }
    }
}
