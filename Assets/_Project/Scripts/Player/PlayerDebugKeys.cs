using UnityEngine;
using UnityEngine.InputSystem;

namespace GTH6
{
    /// <summary>
    /// Test-only keys and a text overlay for phase 1. Delete this component when the real HUD
    /// arrives in phase 8.
    /// H = take damage, J = add armor, K = knockdown (gets up by itself), R = revive / get up.
    /// </summary>
    public class PlayerDebugKeys : MonoBehaviour
    {
        [SerializeField] Health health;
        [SerializeField] RagdollController ragdoll;
        [SerializeField] PlayerController controller;
        [SerializeField] float damagePerPress = 25f;
        [SerializeField] float armorPerPress = 50f;
        [Tooltip("Impulse in N*s. About 40-80 is a shove, 300+ is a car.")]
        [SerializeField] float knockdownImpulse = 60f;
        [SerializeField] bool showOverlay = true;

        GUIStyle style;

        void Awake()
        {
            if (health == null) health = GetComponent<Health>();
            if (ragdoll == null) ragdoll = GetComponent<RagdollController>();
            if (controller == null) controller = GetComponent<PlayerController>();
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.hKey.wasPressedThisFrame && health != null)
            {
                Vector3 push = (-transform.forward + Vector3.up * 0.2f) * 30f;
                health.TakeDamage(new DamageInfo(damagePerPress, DamageType.Generic, Vector3.zero, push, gameObject));
            }

            if (kb.jKey.wasPressedThisFrame && health != null) health.AddArmor(armorPerPress);

            if (kb.kKey.wasPressedThisFrame && ragdoll != null && !ragdoll.IsRagdoll)
            {
                Vector3 push = (-transform.forward + Vector3.up * 0.3f).normalized * knockdownImpulse;
                ragdoll.Activate(push, true);
            }

            if (kb.rKey.wasPressedThisFrame)
            {
                if (health != null && health.IsDead) health.Revive();
                if (ragdoll != null && ragdoll.IsRagdoll) ragdoll.Recover();
            }
        }

        void OnGUI()
        {
            if (!showOverlay) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 16 };

            string text = "";
            if (health != null)
                text += $"Health {health.CurrentHealth:0}/{health.MaxHealth:0}   Armor {health.CurrentArmor:0}/{health.MaxArmor:0}{(health.IsDead ? "   DEAD" : "")}\n";
            if (controller != null)
                text += $"Speed {controller.HorizontalSpeed:0.0} m/s   Grounded {controller.IsGrounded}   Crouch {controller.IsCrouching}   Aim {controller.IsAiming}   Vault {controller.IsVaulting}\n";
            text += "[H] damage   [J] armor   [K] knockdown   [R] revive   [Esc] free mouse";

            GUI.Label(new Rect(12, 12, 900, 90), text, style);
        }
    }
}
