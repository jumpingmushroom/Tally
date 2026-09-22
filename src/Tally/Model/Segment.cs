using System.Collections.Generic;

namespace Tally.Model
{
    /// <summary>Damage or healing attributed to one key (a weapon, an attacker, a heal source).</summary>
    public sealed class Bucket
    {
        public string Name = "";
        public float Total;
        public int Hits;
        public float Max;
        public bool Approximate;

        public void Add(float amount, bool approximate)
        {
            Total += amount;
            Hits++;
            if (amount > Max)
                Max = amount;
            if (approximate)
                Approximate = true;
        }
    }

    /// <summary>One notable hit, kept for the Max Hit drill-in.</summary>
    public sealed class HitRecord
    {
        public float Amount;
        public string Weapon = "";
        public string Target = "";
        public bool Backstab;
        public bool Approximate;
        public float Time;
    }

    /// <summary>Everything a segment knows about one player.</summary>
    public sealed class PlayerStats
    {
        public const int TopHitsKept = 10;

        public string Name = "";

        public float Damage;
        public int DamageHits;
        public float MaxHit;
        public bool Approximate;

        public float Taken;
        public int TakenHits;

        public float Healing;
        public float Overheal;
        public int Heals;

        /// <summary>
        /// Seconds this player spent actively dealing damage. Tally's definition, not Skada's:
        /// gaps between hits longer than the configured idle gap are not counted, so a player
        /// who joins a fight late is not penalised for the time before they arrived.
        /// </summary>
        public float ActiveTime;

        public float FirstDamageTime = -1f;
        public float LastDamageTime = -1f;

        public readonly Dictionary<string, Bucket> Weapons = new Dictionary<string, Bucket>();
        public readonly Dictionary<string, Bucket> TakenFrom = new Dictionary<string, Bucket>();
        public readonly Dictionary<string, Bucket> HealFrom = new Dictionary<string, Bucket>();

        /// <summary>Largest hits first.</summary>
        public readonly List<HitRecord> TopHits = new List<HitRecord>();

        public float Dps
        {
            get { return Damage / (ActiveTime < 1f ? 1f : ActiveTime); }
        }

        internal static Bucket Get(Dictionary<string, Bucket> map, string key)
        {
            Bucket b;
            if (!map.TryGetValue(key, out b))
            {
                b = new Bucket { Name = key };
                map[key] = b;
            }
            return b;
        }

        internal void AddTopHit(HitRecord r)
        {
            int i = 0;
            while (i < TopHits.Count && TopHits[i].Amount >= r.Amount)
                i++;
            if (i >= TopHitsKept)
                return;
            TopHits.Insert(i, r);
            if (TopHits.Count > TopHitsKept)
                TopHits.RemoveAt(TopHits.Count - 1);
        }
    }

    /// <summary>
    /// A window of combat: the whole session, the current fight, or a past one. Aggregates
    /// events per player; never stores the events themselves.
    /// </summary>
    public sealed class Segment
    {
        public string Label = "";
        public float StartTime = -1f;
        public float EndTime = -1f;
        public bool Closed;

        public readonly Dictionary<string, PlayerStats> Players = new Dictionary<string, PlayerStats>();

        /// <summary>Damage per target, to name a fight after what was fought.</summary>
        public readonly Dictionary<string, float> TargetDamage = new Dictionary<string, float>();

        public float Duration(float now)
        {
            if (StartTime < 0f)
                return 0f;
            return (Closed ? EndTime : now) - StartTime;
        }

        public PlayerStats Get(string player)
        {
            PlayerStats p;
            if (!Players.TryGetValue(player, out p))
            {
                p = new PlayerStats { Name = player };
                Players[player] = p;
            }
            return p;
        }

        public void Add(CombatEvent e, float now, float activeGap)
        {
            if (StartTime < 0f)
                StartTime = now;
            EndTime = now;

            PlayerStats p = Get(e.Player);
            bool approx = e.Has(EventFlags.Approximate);

            switch (e.Kind)
            {
                case EventKind.Damage:
                    p.Damage += e.Amount;
                    p.DamageHits++;
                    if (approx)
                        p.Approximate = true;
                    PlayerStats.Get(p.Weapons, e.Ability).Add(e.Amount, approx);

                    if (!e.Has(EventFlags.Dot))
                    {
                        if (e.Amount > p.MaxHit)
                            p.MaxHit = e.Amount;
                        p.AddTopHit(new HitRecord
                        {
                            Amount = e.Amount,
                            Weapon = e.Ability,
                            Target = e.Target,
                            Backstab = e.Has(EventFlags.Backstab),
                            Approximate = approx,
                            Time = now
                        });
                    }

                    if (p.FirstDamageTime < 0f)
                        p.FirstDamageTime = now;
                    else
                    {
                        float gap = now - p.LastDamageTime;
                        if (gap > 0f)
                            p.ActiveTime += gap < activeGap ? gap : activeGap;
                    }
                    p.LastDamageTime = now;

                    float t;
                    TargetDamage.TryGetValue(e.Target, out t);
                    TargetDamage[e.Target] = t + e.Amount;
                    break;

                case EventKind.Taken:
                    p.Taken += e.Amount;
                    p.TakenHits++;
                    PlayerStats.Get(p.TakenFrom, e.Ability).Add(e.Amount, approx);
                    break;

                case EventKind.Heal:
                    p.Healing += e.Amount;
                    p.Overheal += e.Extra;
                    p.Heals++;
                    PlayerStats.Get(p.HealFrom, e.Ability).Add(e.Amount, approx);
                    break;
            }
        }

        /// <summary>The target that took the most damage, for the segment label.</summary>
        public string MainTarget()
        {
            string best = null;
            float bestDamage = -1f;
            foreach (KeyValuePair<string, float> kv in TargetDamage)
            {
                if (kv.Value > bestDamage)
                {
                    bestDamage = kv.Value;
                    best = kv.Key;
                }
            }
            return best;
        }
    }
}
