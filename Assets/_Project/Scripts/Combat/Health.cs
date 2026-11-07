using System;
using UnityEngine;

namespace GTH6
{
    /// <summary>
    /// Health and armor. Armor soaks damage first (armorAbsorption = 1 means armor takes all of it
    /// until it runs out, like GTA). Fires events so UI, ragdoll and audio can react without
    /// this script knowing about them.
    /// </summary>
    public class Health : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1f)] float maxHealth = 100f;
        [SerializeField, Min(0f)] float maxArmor = 100f;
        [SerializeField, Min(0f)] float startingArmor = 0f;
        [Tooltip("Share of each hit that armor absorbs while it lasts. 1 = all, 0.5 = half.")]
        [SerializeField, Range(0f, 1f)] float armorAbsorption = 1f;
        [SerializeField] bool invulnerable;

        public float CurrentHealth { get; private set; }
        public float CurrentArmor { get; private set; }
        public float MaxHealth => maxHealth;
        public float MaxArmor => maxArmor;
        public bool IsDead { get; private set; }

        /// <summary>Health or armor changed. Use for the HUD.</summary>
        public event Action<Health> Changed;
        public event Action<DamageInfo> Damaged;
        public event Action<DamageInfo> Died;

        void Awake()
        {
            CurrentHealth = maxHealth;
            CurrentArmor = Mathf.Clamp(startingArmor, 0f, maxArmor);
        }

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead || invulnerable || info.Amount <= 0f) return;

            float remaining = info.Amount;
            if (CurrentArmor > 0f)
            {
                float absorbed = Mathf.Min(CurrentArmor, remaining * armorAbsorption);
                CurrentArmor -= absorbed;
                remaining -= absorbed;
            }

            CurrentHealth = Mathf.Max(0f, CurrentHealth - remaining);
            Damaged?.Invoke(info);
            Changed?.Invoke(this);

            if (CurrentHealth <= 0f)
            {
                IsDead = true;
                Died?.Invoke(info);
            }
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            Changed?.Invoke(this);
        }

        public void AddArmor(float amount)
        {
            if (IsDead || amount <= 0f) return;
            CurrentArmor = Mathf.Min(maxArmor, CurrentArmor + amount);
            Changed?.Invoke(this);
        }

        /// <summary>Brings the character back. Used by respawn (hospital) in phase 7.</summary>
        public void Revive(float healthFraction = 1f)
        {
            IsDead = false;
            CurrentHealth = maxHealth * Mathf.Clamp(healthFraction, 0.01f, 1f);
            Changed?.Invoke(this);
        }
    }
}
