using UnityEngine;

namespace GTH6
{
    /// <summary>
    /// Humanoid foot IK. For each foot, a ray finds the ground under it. The foot is moved up or
    /// down by the difference between that ground and the character's root height, so the walk
    /// cycle's foot lift stays intact. The pelvis drops to let the lower foot reach downhill.
    /// Must sit on the same GameObject as the Animator, and the Animator layer needs "IK Pass" on.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class FootIK : MonoBehaviour
    {
        static readonly int GroundedHash = Animator.StringToHash("Grounded");

        [Tooltip("Layers feet can stand on. This object's own layer is removed automatically.")]
        [SerializeField] LayerMask groundMask = ~0;
        [Tooltip("Ray starts this far above the root.")]
        [SerializeField] float raycastHeight = 0.6f;
        [Tooltip("Ray searches this far below the root.")]
        [SerializeField] float raycastDepth = 0.6f;
        [SerializeField] float maxFootRaise = 0.45f;
        [SerializeField] float maxFootDrop = 0.45f;
        [SerializeField] float footSmoothing = 18f;
        [SerializeField] float pelvisSmoothing = 10f;
        [Tooltip("How fast IK fades in/out (weight per second).")]
        [SerializeField] float weightSpeed = 8f;
        [SerializeField] bool alignFeetToSlope = true;
        [Tooltip("Animator states with one of these tags get no foot IK (jump, vault, get up).")]
        [SerializeField] string[] disabledTags = { "NoIK", "Locked" };

        Animator animator;
        int mask;
        bool hasGroundedParameter;

        float weight;
        float leftOffset;
        float rightOffset;
        float pelvisOffset;
        Vector3 leftNormal = Vector3.up;
        Vector3 rightNormal = Vector3.up;

        void Reset()
        {
            groundMask = ~LayerMask.GetMask("Player");
        }

        void Awake()
        {
            animator = GetComponent<Animator>();
            mask = groundMask & ~(1 << gameObject.layer);
        }

        void Start()
        {
            foreach (AnimatorControllerParameter p in animator.parameters)
                if (p.nameHash == GroundedHash && p.type == AnimatorControllerParameterType.Bool)
                    hasGroundedParameter = true;
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0) return;

            float dt = Time.deltaTime;
            bool grounded = !hasGroundedParameter || animator.GetBool(GroundedHash);
            float targetWeight = grounded && !IsBlockedState() ? 1f : 0f;
            weight = Mathf.MoveTowards(weight, targetWeight, weightSpeed * dt);

            Vector3 leftPosition = animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            Vector3 rightPosition = animator.GetIKPosition(AvatarIKGoal.RightFoot);
            Quaternion leftRotation = animator.GetIKRotation(AvatarIKGoal.LeftFoot);
            Quaternion rightRotation = animator.GetIKRotation(AvatarIKGoal.RightFoot);

            float footLerp = 1f - Mathf.Exp(-footSmoothing * dt);
            leftOffset = Mathf.Lerp(leftOffset, SampleGround(leftPosition, out Vector3 nL), footLerp);
            rightOffset = Mathf.Lerp(rightOffset, SampleGround(rightPosition, out Vector3 nR), footLerp);
            leftNormal = Vector3.Slerp(leftNormal, nL, footLerp);
            rightNormal = Vector3.Slerp(rightNormal, nR, footLerp);

            // Lower the hips so the lower foot can reach. Never raise them (that would hover the body).
            float targetPelvis = Mathf.Min(leftOffset, rightOffset, 0f);
            pelvisOffset = Mathf.Lerp(pelvisOffset, targetPelvis, 1f - Mathf.Exp(-pelvisSmoothing * dt));

            if (weight <= 0.001f)
            {
                // Explicitly zero the weights so the pure animation plays (jumping, vaulting).
                animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
                animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
                return;
            }

            animator.bodyPosition += Vector3.up * (pelvisOffset * weight);
            ApplyFoot(AvatarIKGoal.LeftFoot, leftPosition, leftRotation, leftOffset, leftNormal);
            ApplyFoot(AvatarIKGoal.RightFoot, rightPosition, rightRotation, rightOffset, rightNormal);
        }

        bool IsBlockedState()
        {
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            bool inTransition = animator.IsInTransition(0);
            AnimatorStateInfo next = inTransition ? animator.GetNextAnimatorStateInfo(0) : current;

            foreach (string tag in disabledTags)
            {
                if (string.IsNullOrEmpty(tag)) continue;
                if (current.IsTag(tag) || (inTransition && next.IsTag(tag))) return true;
            }
            return false;
        }

        /// <summary>Returns ground height under the foot relative to the root, clamped.</summary>
        float SampleGround(Vector3 footPosition, out Vector3 normal)
        {
            float rootY = transform.position.y;
            Vector3 origin = new Vector3(footPosition.x, rootY + raycastHeight, footPosition.z);

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastHeight + raycastDepth, mask, QueryTriggerInteraction.Ignore))
            {
                normal = hit.normal;
                return Mathf.Clamp(hit.point.y - rootY, -maxFootDrop, maxFootRaise);
            }

            normal = Vector3.up;
            return 0f;
        }

        void ApplyFoot(AvatarIKGoal goal, Vector3 animatedPosition, Quaternion animatedRotation, float offset, Vector3 normal)
        {
            Vector3 target = animatedPosition + Vector3.up * offset;
            Quaternion rotation = alignFeetToSlope
                ? Quaternion.FromToRotation(Vector3.up, normal) * animatedRotation
                : animatedRotation;

            animator.SetIKPositionWeight(goal, weight);
            animator.SetIKRotationWeight(goal, weight);
            animator.SetIKPosition(goal, target);
            animator.SetIKRotation(goal, rotation);
        }
    }
}
