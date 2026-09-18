using System.Collections.Generic;
using UnityEngine;

namespace Recount.Core
{
    public enum DotKind
    {
        Fire = 0,
        Spirit = 1,
        Poison = 2
    }

    /// <summary>
    /// Who applied the burning, spirit and poison on each creature this client owns.
    ///
    /// SE_Burning and SE_Poison tick by calling Character.ApplyDamage with a fresh HitData that
    /// carries no attacker, and neither effect stores one (StatusEffect.SetAttacker is a no-op
    /// for both; only SE_Harpooned overrides it). The only place the attacker is known is the
    /// hit that applied the effect, so it is remembered here, per victim and per kind, and the
    /// ticks are credited to the most recent applier. Several players stacking fire on one
    /// target share a single damage pool in the game, so "most recent wins" is the best the
    /// data allows.
    /// </summary>
    public static class DotTracker
    {
        public sealed class Source
        {
            public string Name = "";
            public bool IsPlayer;
            public float Time;
        }

        private const float Ttl = 120f;

        private static readonly Dictionary<ZDOID, Source[]> _sources = new Dictionary<ZDOID, Source[]>();
        private static float _nextPurge;

        public static void Remember(ZDOID victim, DotKind kind, string name, bool isPlayer, float now)
        {
            Source[] arr;
            if (!_sources.TryGetValue(victim, out arr))
            {
                arr = new Source[3];
                _sources[victim] = arr;
            }
            arr[(int)kind] = new Source { Name = name, IsPlayer = isPlayer, Time = now };
        }

        public static bool TryGet(ZDOID victim, DotKind kind, float now, out Source source)
        {
            source = null;
            Source[] arr;
            if (!_sources.TryGetValue(victim, out arr))
                return false;
            Source s = arr[(int)kind];
            if (s == null || now - s.Time > Ttl)
                return false;
            source = s;
            return true;
        }

        public static void Update(float now)
        {
            if (now < _nextPurge)
                return;
            _nextPurge = now + 30f;

            List<ZDOID> dead = null;
            foreach (KeyValuePair<ZDOID, Source[]> kv in _sources)
            {
                bool alive = false;
                for (int i = 0; i < 3; i++)
                {
                    Source s = kv.Value[i];
                    if (s != null && now - s.Time <= Ttl)
                        alive = true;
                }
                if (!alive)
                    (dead ?? (dead = new List<ZDOID>())).Add(kv.Key);
            }
            if (dead != null)
                foreach (ZDOID id in dead)
                    _sources.Remove(id);
        }

        public static void Clear()
        {
            _sources.Clear();
        }

        public static string AbilityName(DotKind kind)
        {
            switch (kind)
            {
                case DotKind.Fire: return "Burning";
                case DotKind.Spirit: return "Spirit";
                default: return "Poison";
            }
        }
    }

    /// <summary>
    /// What is healing right now. Character.Heal does not say where the health came from; the
    /// callers do. Patches on the food tick and on status effects set this around their calls.
    /// </summary>
    public static class HealContext
    {
        public static string Current;

        public const string Food = "Food";
        public const string Other = "Other";
    }
}
