using System;
using System.Collections.Generic;
using HarmonyLib;
using Tally.Core;
using Tally.Net;
using UnityEngine;

namespace Tally.Patches
{
    /// <summary>
    /// Where the numbers come from. Every patch is a prefix or postfix that reads and never
    /// alters the hit, and every body is wrapped so a bug here can never break the game's own
    /// damage path.
    ///
    /// The owner of a creature is the only client that computes real damage: Character.Damage
    /// routes the hit to the owner's RPC_Damage, which applies resistances and armour and then
    /// calls ApplyDamage with the mitigated hit. Burning and poison ticks call ApplyDamage
    /// directly. So ApplyDamage on an owned character is the one place every real hit passes.
    /// </summary>
    internal static class Guard
    {
        private static int _errors;

        public static void Fail(string where, Exception ex)
        {
            if (_errors++ < 5)
                TallyPlugin.Log.LogWarning(where + " threw: " + ex);
        }
    }

    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Character_RPC_Damage_Patch
    {
        /// <summary>
        /// Before the game strips fire, spirit and poison from the hit and hands them to status
        /// effects that do not remember who applied them.
        /// </summary>
        private static void Prefix(Character __instance, HitData hit)
        {
            try
            {
                ZNetView nview = __instance.m_nview;
                if (hit == null || nview == null || !nview.IsValid() || !nview.IsOwner())
                    return;
                Attribution.OnEffectApplied(__instance, hit);
            }
            catch (Exception ex)
            {
                Guard.Fail("RPC_Damage prefix", ex);
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class Character_ApplyDamage_Patch
    {
        /// <summary>Mirror the method's own early returns so the postfix only counts what landed.</summary>
        private static void Prefix(Character __instance, HitData hit, out bool __state)
        {
            __state = false;
            try
            {
                ZNetView nview = __instance.m_nview;
                if (hit == null || nview == null || !nview.IsValid() || !nview.IsOwner())
                    return;
                if (__instance.IsDebugFlying() || __instance.IsDead() || __instance.IsTeleporting()
                    || __instance.InCutscene() || CinematicsManager.IsPlaying())
                    return;
                __state = true;
            }
            catch (Exception ex)
            {
                Guard.Fail("ApplyDamage prefix", ex);
            }
        }

        private static void Postfix(Character __instance, HitData hit, bool __state)
        {
            if (!__state)
                return;
            try
            {
                // The hit has had the difficulty and world-level multipliers applied in place by
                // now, so this is exactly what came off the health bar (before the clamp at 0).
                float total = hit.GetTotalDamage();
                if (total <= 0.1f)
                    return;

                // RPC_Damage stamps m_backstabTime with the current time when it applies the
                // sneak multiplier, immediately before calling ApplyDamage.
                bool backstab = hit.m_backstabBonus > 1f && __instance.m_backstabTime == Time.time;

                Attribution.OnCharacterDamaged(__instance, hit, total, backstab);
            }
            catch (Exception ex)
            {
                Guard.Fail("ApplyDamage postfix", ex);
            }
        }
    }

    [HarmonyPatch(typeof(Character), "RPC_Heal")]
    internal static class Character_RPC_Heal_Patch
    {
        private static void Prefix(Character __instance, float hp)
        {
            try
            {
                if (hp <= 0f || !__instance.IsPlayer())
                    return;
                ZNetView nview = __instance.m_nview;
                if (nview == null || !nview.IsValid() || !nview.IsOwner())
                    return;

                float health = __instance.GetHealth();
                if (health <= 0f || __instance.IsDead())
                    return;

                float max = __instance.GetMaxHealth();
                float effective = Mathf.Min(health + hp, max) - health;
                if (effective < 0f)
                    effective = 0f;
                float overheal = hp - effective;

                Attribution.OnHealed(__instance, effective, overheal);
            }
            catch (Exception ex)
            {
                Guard.Fail("RPC_Heal prefix", ex);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateFood")]
    internal static class Player_UpdateFood_Patch
    {
        private static void Prefix()
        {
            HealContext.Current = HealContext.Food;
        }

        private static void Postfix()
        {
            HealContext.Current = null;
        }
    }

    [HarmonyPatch(typeof(SE_Stats), nameof(SE_Stats.UpdateStatusEffect))]
    internal static class SE_Stats_UpdateStatusEffect_Patch
    {
        private static readonly Dictionary<int, string> _names = new Dictionary<int, string>();

        internal static void Prefix(SE_Stats __instance)
        {
            try
            {
                int hash = __instance.NameHash();
                string name;
                if (!_names.TryGetValue(hash, out name))
                {
                    name = Names.StatusEffect(__instance);
                    _names[hash] = name;
                }
                HealContext.Current = name;
            }
            catch (Exception ex)
            {
                Guard.Fail("SE_Stats prefix", ex);
            }
        }

        private static void Postfix()
        {
            HealContext.Current = null;
        }
    }

    [HarmonyPatch(typeof(SE_Stats), nameof(SE_Stats.Setup))]
    internal static class SE_Stats_Setup_Patch
    {
        /// <summary>The up-front heal of a potion happens in Setup, not in a tick.</summary>
        private static void Prefix(SE_Stats __instance)
        {
            SE_Stats_UpdateStatusEffect_Patch.Prefix(__instance);
        }

        private static void Postfix()
        {
            HealContext.Current = null;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Character_Damage_Patch
    {
        /// <summary>
        /// The attacker's side. When the local player hits a creature owned by a client that
        /// does not run the mod, no one will ever report the real damage, so the pre-mitigation
        /// number is recorded here, flagged approximate. When the owner does run the mod the
        /// hit is left alone: their report arrives over the wire.
        /// </summary>
        private static void Prefix(Character __instance, HitData hit)
        {
            try
            {
                Player local = Player.m_localPlayer;
                if (local == null || hit == null)
                    return;
                ZNetView nview = __instance.m_nview;
                if (nview == null || !nview.IsValid() || nview.IsOwner())
                    return;
                if (hit.m_attacker != local.GetZDOID())
                    return;

                // The victim's RPC_Damage discards player-on-player hits unless PvP is on.
                if (__instance.IsPlayer() && !hit.m_ignorePVP
                    && (!__instance.IsPVPEnabled() || !local.IsPVPEnabled()))
                    return;

                long owner = nview.GetZDO().GetOwner();
                if (EventSync.PeerHasMod(owner))
                    return;

                float total = hit.GetTotalDamage();
                if (total <= 0f)
                    return;

                Attribution.OnApproximateHit(__instance, hit, total);
            }
            catch (Exception ex)
            {
                Guard.Fail("Damage prefix", ex);
            }
        }
    }

    // ---- trees, rocks and pieces: off by default ---------------------------------------
    //
    // Each RPC applies resistance to the hit in place and then checks the tool tier, so a
    // postfix sees the mitigated number and can repeat the same tier check.

    internal static class ObjectHits
    {
        /// <summary>
        /// Each RPC returns before applying resistance when the object is already destroyed,
        /// so the prefix records whether it was alive and the postfix trusts the hit only then.
        /// </summary>
        public static bool Alive(ZNetView nview, float health)
        {
            return PluginConfig.IncludeStructures.Value && nview != null && nview.IsValid() && nview.IsOwner() && health > 0f;
        }

        public static void Report(Component target, ZNetView nview, HitData hit, int minToolTier, bool allowTierZero, string name)
        {
            try
            {
                if (!PluginConfig.IncludeStructures.Value || hit == null)
                    return;
                if (nview == null || !nview.IsValid() || !nview.IsOwner())
                    return;
                float total = hit.GetTotalDamage();
                if (total <= 0f || !hit.CheckToolTier(minToolTier, allowTierZero))
                    return;
                Attribution.OnObjectDamaged(target, hit, total, name);
            }
            catch (Exception ex)
            {
                Guard.Fail("object hit", ex);
            }
        }

        public static string PrefabName(Component c)
        {
            return Names.Prettify(Utils.GetPrefabName(c.gameObject));
        }
    }

    [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
    internal static class WearNTear_RPC_Damage_Patch
    {
        private static void Prefix(WearNTear __instance, out bool __state)
        {
            ZNetView nview = __instance.m_nview;
            __state = ObjectHits.Alive(nview, nview != null && nview.IsValid() ? nview.GetZDO().GetFloat(ZDOVars.s_health, __instance.m_health) : 0f);
        }

        private static void Postfix(WearNTear __instance, HitData hit, bool __state)
        {
            if (!__state)
                return;
            string name = __instance.m_piece != null && !string.IsNullOrEmpty(__instance.m_piece.m_name)
                ? Names.Localize(__instance.m_piece.m_name)
                : ObjectHits.PrefabName(__instance);
            ObjectHits.Report(__instance, __instance.m_nview, hit, __instance.m_minToolTier, true, name);
        }
    }

    [HarmonyPatch(typeof(Destructible), "RPC_Damage")]
    internal static class Destructible_RPC_Damage_Patch
    {
        private static void Prefix(Destructible __instance, out bool __state)
        {
            ZNetView nview = __instance.m_nview;
            __state = !__instance.m_destroyed
                      && ObjectHits.Alive(nview, nview != null && nview.IsValid() ? nview.GetZDO().GetFloat(ZDOVars.s_health, __instance.m_health) : 0f);
        }

        private static void Postfix(Destructible __instance, HitData hit, bool __state)
        {
            if (!__state)
                return;
            ObjectHits.Report(__instance, __instance.m_nview, hit, __instance.m_minToolTier, false, ObjectHits.PrefabName(__instance));
        }
    }

    [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
    internal static class TreeBase_RPC_Damage_Patch
    {
        private static void Prefix(TreeBase __instance, out bool __state)
        {
            ZNetView nview = __instance.m_nview;
            __state = ObjectHits.Alive(nview, nview != null && nview.IsValid() ? nview.GetZDO().GetFloat(ZDOVars.s_health, __instance.m_health) : 0f);
        }

        private static void Postfix(TreeBase __instance, HitData hit, bool __state)
        {
            if (!__state)
                return;
            ObjectHits.Report(__instance, __instance.m_nview, hit, __instance.m_minToolTier, true, ObjectHits.PrefabName(__instance));
        }
    }

    [HarmonyPatch(typeof(TreeLog), "RPC_Damage")]
    internal static class TreeLog_RPC_Damage_Patch
    {
        private static void Prefix(TreeLog __instance, out bool __state)
        {
            ZNetView nview = __instance.m_nview;
            __state = ObjectHits.Alive(nview, nview != null && nview.IsValid() ? nview.GetZDO().GetFloat(ZDOVars.s_health, 0f) : 0f);
        }

        private static void Postfix(TreeLog __instance, HitData hit, bool __state)
        {
            if (!__state)
                return;
            ObjectHits.Report(__instance, __instance.m_nview, hit, __instance.m_minToolTier, true, ObjectHits.PrefabName(__instance));
        }
    }

    [HarmonyPatch(typeof(MineRock), "RPC_Hit")]
    internal static class MineRock_RPC_Hit_Patch
    {
        private static void Prefix(MineRock __instance, int hitAreaIndex, out bool __state)
        {
            ZNetView nview = __instance.m_nview;
            float health = nview != null && nview.IsValid() ? nview.GetZDO().GetFloat("Health" + hitAreaIndex, __instance.GetHealth()) : 0f;
            __state = ObjectHits.Alive(nview, health);
        }

        private static void Postfix(MineRock __instance, HitData hit, bool __state)
        {
            if (!__state)
                return;
            ObjectHits.Report(__instance, __instance.m_nview, hit, __instance.m_minToolTier, false, ObjectHits.PrefabName(__instance));
        }
    }

    [HarmonyPatch(typeof(MineRock5), "RPC_Damage")]
    internal static class MineRock5_RPC_Damage_Patch
    {
        private static void Prefix(MineRock5 __instance, int hitAreaIndex, out bool __state)
        {
            __state = false;
            try
            {
                ZNetView nview = __instance.m_nview;
                if (nview == null || !nview.IsValid() || !nview.IsOwner() || !PluginConfig.IncludeStructures.Value)
                    return;
                __instance.LoadHealth();
                MineRock5.HitArea area = __instance.GetHitArea(hitAreaIndex);
                __state = area != null && area.m_health > 0f;
            }
            catch (Exception ex)
            {
                Guard.Fail("MineRock5 prefix", ex);
            }
        }

        private static void Postfix(MineRock5 __instance, HitData hit, bool __state)
        {
            if (!__state)
                return;
            ObjectHits.Report(__instance, __instance.m_nview, hit, __instance.m_minToolTier, false, ObjectHits.PrefabName(__instance));
        }
    }
}
