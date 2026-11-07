using UnityEngine;

namespace GTH6
{
    /// <summary>
    /// Moves an object along fixed points: trams on the light rail, the funicular car, cable car cabins.
    /// Uses a kinematic Rigidbody, so the player can stand inside and gets carried along.
    /// Waypoints are the children of the Path object, in order. Stations are waypoints where it waits.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PathMover : MonoBehaviour
    {
        public enum EndMode { Loop, PingPong }

        [Tooltip("Parent object whose children are the waypoints, in order.")]
        [SerializeField] Transform path;
        [SerializeField] EndMode endMode = EndMode.PingPong;
        [SerializeField] float speed = 8f;
        [Tooltip("Metres over which it speeds up and slows down near stations.")]
        [SerializeField] float easeDistance = 15f;
        [Tooltip("Indices of waypoints where it stops (for example 0 and the last one).")]
        [SerializeField] int[] stationIndices = { 0 };
        [SerializeField] float stationWaitTime = 8f;
        [Tooltip("Turn to face the direction of travel (trams yes, cable car cabins no).")]
        [SerializeField] bool faceDirection = true;

        Rigidbody rb;
        Transform[] points;
        int target = 1;
        int direction = 1;
        float waitTimer;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            if (path == null || path.childCount < 2)
            {
                Debug.LogWarning("[PathMover] Needs a Path object with at least 2 child waypoints.", this);
                enabled = false;
                return;
            }
            points = new Transform[path.childCount];
            for (int i = 0; i < points.Length; i++) points[i] = path.GetChild(i);
            rb.position = points[0].position;
            target = 1;
        }

        void FixedUpdate()
        {
            if (waitTimer > 0f)
            {
                waitTimer -= Time.fixedDeltaTime;
                return;
            }

            Vector3 goal = points[target].position;
            Vector3 toGoal = goal - rb.position;
            float distance = toGoal.magnitude;

            // Slow down approaching a station and speed up leaving one.
            float factor = 1f;
            if (IsStation(target)) factor = Mathf.Min(factor, Mathf.Clamp(distance / easeDistance, 0.15f, 1f));
            int previous = target - direction;
            if (previous >= 0 && previous < points.Length && IsStation(previous))
                factor = Mathf.Min(factor, Mathf.Clamp(Vector3.Distance(rb.position, points[previous].position) / easeDistance, 0.15f, 1f));

            float step = speed * factor * Time.fixedDeltaTime;
            if (step >= distance)
            {
                rb.MovePosition(goal);
                if (IsStation(target)) waitTimer = stationWaitTime;
                NextTarget();
            }
            else
            {
                rb.MovePosition(rb.position + toGoal / distance * step);
            }

            if (faceDirection && toGoal.sqrMagnitude > 0.01f)
                rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(toGoal), 4f * Time.fixedDeltaTime));
        }

        bool IsStation(int index)
        {
            foreach (int s in stationIndices) if (s == index) return true;
            return false;
        }

        void NextTarget()
        {
            int nextIndex = target + direction;
            if (nextIndex >= 0 && nextIndex < points.Length)
            {
                target = nextIndex;
                return;
            }
            if (endMode == EndMode.Loop)
            {
                target = 0;
            }
            else
            {
                direction = -direction;
                target += direction;
            }
        }

        void OnDrawGizmos()
        {
            if (path == null || path.childCount < 2) return;
            Gizmos.color = new Color(0.9f, 0.5f, 0.1f);
            for (int i = 0; i < path.childCount - 1; i++) Gizmos.DrawLine(path.GetChild(i).position, path.GetChild(i + 1).position);
            if (endMode == EndMode.Loop) Gizmos.DrawLine(path.GetChild(path.childCount - 1).position, path.GetChild(0).position);
        }
    }
}
