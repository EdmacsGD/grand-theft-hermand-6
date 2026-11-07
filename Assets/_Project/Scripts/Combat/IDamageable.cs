using UnityEngine;

namespace GTH6
{
    public enum DamageType
    {
        Generic,
        Bullet,
        Melee,
        Vehicle,
        Explosion,
        Fall
    }

    /// <summary>
    /// Everything a hit needs to describe itself. Weapons, cars and explosions will all
    /// build one of these and pass it to IDamageable.TakeDamage.
    /// </summary>
    public struct DamageInfo
    {
        public float Amount;
        public DamageType Type;
        /// <summary>World position of the hit. Vector3.zero means "unknown".</summary>
        public Vector3 Point;
        /// <summary>Impulse (N*s) pushed into the ragdoll if this hit kills.</summary>
        public Vector3 Force;
        public GameObject Instigator;

        public DamageInfo(float amount, DamageType type, Vector3 point, Vector3 force, GameObject instigator)
        {
            Amount = amount;
            Type = type;
            Point = point;
            Force = force;
            Instigator = instigator;
        }
    }

    /// <summary>Anything that can be hurt: player, pedestrians, police, later vehicles.</summary>
    public interface IDamageable
    {
        void TakeDamage(DamageInfo info);
    }
}
