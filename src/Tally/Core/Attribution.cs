using Tally.Model;
using Tally.Net;
using UnityEngine;

namespace Tally.Core
{
    /// <summary>
    /// Turns what the game reports into attributed events. Called from the patches with the
    /// post-mitigation numbers; everything about "who" and "with what" is decided here.
    /// </summary>
    public static class Attribution
    {
        /// <summary>
        /// A hit landed on a character this client owns. Called after Character.ApplyDamage with
        /// the damage that actually came off the health bar.
        /// </summary>
        public static void OnCharacterDamaged(Character victim, HitData hit, float applied, bool backstab)
        {
            float now = Time.time;
            Vector3 pos = victim.transform.position;
            Character attacker = hit.GetAttacker();

            string attackerName = null;
            bool attackerIsPlayer = false;
            string ability;
            EventFlags flags = EventFlags.None;

            if (backstab)
                flags |= EventFlags.Backstab;
            if (hit.m_ranged)
                flags |= EventFlags.Ranged;

            if (attacker != null)
            {
                attackerIsPlayer = attacker.IsPlayer();
                attackerName = Names.Character(attacker);
                ability = attackerIsPlayer ? WeaponName(attacker, hit) : attackerName;
            }
            else if (hit.m_hitType == HitData.HitType.Burning || hit.m_hitType == HitData.HitType.Poisoned)
            {
                DotKind kind = hit.m_hitType == HitData.HitType.Poisoned
                    ? DotKind.Poison
                    : (hit.m_damage.m_spirit > 0f && hit.m_damage.m_fire <= 0f ? DotKind.Spirit : DotKind.Fire);
                ability = DotTracker.AbilityName(kind);
                flags |= EventFlags.Dot;

                DotTracker.Source src;
                if (DotTracker.TryGet(victim.GetZDOID(), kind, now, out src))
                {
                    attackerName = src.Name;
                    attackerIsPlayer = src.IsPlayer;
                }
            }
            else
            {
                ability = Names.HitType(hit.m_hitType);
            }

            string victimName = Names.Character(victim);
            HitData.DamageType type = hit.m_damage.GetMajorityDamageType();

            if (attackerIsPlayer)
            {
                Recorder.Record(new CombatEvent
                {
                    Kind = EventKind.Damage,
                    Flags = flags,
                    Player = attackerName,
                    Ability = ability,
                    Target = victimName,
                    Amount = applied,
                    DamageType = type,
                    Position = pos,
                    Skill = hit.m_skill,
                    SkillLevel = hit.m_skillLevel
                });
            }

            if (victim.IsPlayer())
            {
                Recorder.Record(new CombatEvent
                {
                    Kind = EventKind.Taken,
                    Flags = flags & (EventFlags.Dot | EventFlags.Ranged),
                    Player = victimName,
                    Ability = attackerIsPlayer ? attackerName + " (" + ability + ")" : (attackerName ?? ability),
                    Target = attackerName ?? ability,
                    Amount = applied,
                    DamageType = type,
                    Position = pos
                });
            }
        }

        /// <summary>
        /// The local player hit a creature owned by a client without the mod. Nobody will ever
        /// tell us the real number, so record the attacker's pre-mitigation damage, flagged.
        /// </summary>
        public static void OnApproximateHit(Character victim, HitData hit, float total)
        {
            Player local = Player.m_localPlayer;
            if (local == null)
                return;

            EventFlags flags = EventFlags.Approximate;
            if (hit.m_ranged)
                flags |= EventFlags.Ranged;

            Recorder.Record(new CombatEvent
            {
                Kind = EventKind.Damage,
                Flags = flags,
                Player = Names.Character(local),
                Ability = WeaponName(local, hit),
                Target = Names.Character(victim),
                Amount = total,
                DamageType = hit.m_damage.GetMajorityDamageType(),
                Position = victim.transform.position,
                Skill = hit.m_skill,
                SkillLevel = hit.m_skillLevel
            });
        }

        /// <summary>A tree, rock or piece this client owns took damage from a player.</summary>
        public static void OnObjectDamaged(Component target, HitData hit, float total, string targetName)
        {
            Character attacker = hit.GetAttacker();
            if (attacker == null || !attacker.IsPlayer())
                return;

            EventFlags flags = EventFlags.NonCharacter;
            if (hit.m_ranged)
                flags |= EventFlags.Ranged;

            Recorder.Record(new CombatEvent
            {
                Kind = EventKind.Damage,
                Flags = flags,
                Player = Names.Character(attacker),
                Ability = WeaponName(attacker, hit),
                Target = targetName,
                Amount = total,
                DamageType = hit.m_damage.GetMajorityDamageType(),
                Position = target.transform.position,
                Skill = hit.m_skill,
                SkillLevel = hit.m_skillLevel
            });
        }

        /// <summary>Note who applied burning, spirit or poison, before RPC_Damage strips them from the hit.</summary>
        public static void OnEffectApplied(Character victim, HitData hit)
        {
            if (hit.m_damage.m_fire <= 0f && hit.m_damage.m_spirit <= 0f && hit.m_damage.m_poison <= 0f)
                return;

            Character attacker = hit.GetAttacker();
            if (attacker == null)
                return;

            float now = Time.time;
            ZDOID id = victim.GetZDOID();
            string name = Names.Character(attacker);
            bool isPlayer = attacker.IsPlayer();

            if (hit.m_damage.m_fire > 0f)
                DotTracker.Remember(id, DotKind.Fire, name, isPlayer, now);
            if (hit.m_damage.m_spirit > 0f)
                DotTracker.Remember(id, DotKind.Spirit, name, isPlayer, now);
            if (hit.m_damage.m_poison > 0f)
                DotTracker.Remember(id, DotKind.Poison, name, isPlayer, now);
        }

        /// <summary>The local player was healed.</summary>
        public static void OnHealed(Character who, float effective, float overheal)
        {
            Recorder.Record(new CombatEvent
            {
                Kind = EventKind.Heal,
                Player = Names.Character(who),
                Ability = HealContext.Current ?? HealContext.Other,
                Amount = effective,
                Extra = overheal,
                Position = who.transform.position
            });
        }

        /// <summary>
        /// The weapon behind a hit. The local player's equipped weapon is read directly; other
        /// players' from what their character is visibly holding, which is what the game syncs.
        /// For projectiles and magic the equipped item may have changed before the hit landed,
        /// so the weapon is only trusted if its skill matches the hit's; otherwise the skill
        /// itself ("Bows") names the row.
        /// </summary>
        public static string WeaponName(Character attacker, HitData hit)
        {
            var player = attacker as Player;
            ItemDrop.ItemData weapon = null;

            if (player != null)
            {
                if (ReferenceEquals(player, Player.m_localPlayer))
                    weapon = player.GetCurrentWeapon();
                else
                    weapon = VisibleWeapon(player);
            }

            string name = weapon != null ? Names.Item(weapon) : null;

            if (name != null && hit.m_ranged && hit.m_skill != Skills.SkillType.None
                && weapon.m_shared.m_skillType != hit.m_skill)
                name = null;

            if (name == null && hit.m_skill != Skills.SkillType.None)
                name = Names.Skill(hit.m_skill);

            return name ?? Names.Unknown;
        }

        private static ItemDrop.ItemData VisibleWeapon(Player player)
        {
            VisEquipment vis = player.m_visEquipment;
            if (vis == null || ObjectDB.instance == null)
                return null;

            ItemDrop.ItemData right = ItemByHash(vis.m_currentRightItemHash);
            if (right != null && right.IsWeapon())
                return right;

            ItemDrop.ItemData left = ItemByHash(vis.m_currentLeftItemHash);
            if (left != null && left.IsWeapon() && left.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch)
                return left;

            return null;
        }

        private static ItemDrop.ItemData ItemByHash(int hash)
        {
            if (hash == 0)
                return null;
            GameObject prefab = ObjectDB.instance.GetItemPrefab(hash);
            if (prefab == null)
                return null;
            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            return drop != null ? drop.m_itemData : null;
        }
    }
}
