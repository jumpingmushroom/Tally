using System;
using UnityEngine;

namespace Tally.Model
{
    public enum EventKind : byte
    {
        /// <summary>A player dealt damage. Player = attacker, Ability = weapon, Target = victim.</summary>
        Damage = 0,

        /// <summary>A player took damage. Player = victim, Ability = what hit them, Target = same.</summary>
        Taken = 1,

        /// <summary>A player was healed. Player = who, Ability = the source (food, an effect).</summary>
        Heal = 2
    }

    [Flags]
    public enum EventFlags : byte
    {
        None = 0,

        /// <summary>
        /// Pre-mitigation damage recorded by the attacker because the target's owner does not
        /// run the mod. Shown with a "~" prefix.
        /// </summary>
        Approximate = 1,

        /// <summary>The sneak-attack multiplier was applied to this hit.</summary>
        Backstab = 2,

        /// <summary>A burning, poison or spirit tick, credited to whoever applied it.</summary>
        Dot = 4,

        /// <summary>Projectile or thrown.</summary>
        Ranged = 8,

        /// <summary>The target was a tree, rock or built piece rather than a creature.</summary>
        NonCharacter = 16,

        /// <summary>Arrived from another client rather than being observed here.</summary>
        Remote = 32
    }

    /// <summary>
    /// One thing that happened in combat, already attributed to a player. This is the unit that
    /// segments aggregate and that goes over the wire.
    /// </summary>
    public sealed class CombatEvent
    {
        public EventKind Kind;
        public EventFlags Flags;

        /// <summary>The player this event belongs to: the row in the meter.</summary>
        public string Player = "";

        /// <summary>The drill-in key: weapon, damaging creature, or heal source.</summary>
        public string Ability = "";

        /// <summary>What was hit. Empty for heals.</summary>
        public string Target = "";

        /// <summary>Damage or effective healing.</summary>
        public float Amount;

        /// <summary>Overheal for heals; unused otherwise.</summary>
        public float Extra;

        /// <summary>Majority damage type, as HitData.DamageType, for records and tooltips.</summary>
        public HitData.DamageType DamageType;

        /// <summary>Where it happened, so a receiver can apply the range limit.</summary>
        public Vector3 Position;

        /// <summary>Skill and level behind a local hit, for the records file. Not sent.</summary>
        public Skills.SkillType Skill;
        public float SkillLevel;

        public bool Has(EventFlags f)
        {
            return (Flags & f) != 0;
        }
    }
}
