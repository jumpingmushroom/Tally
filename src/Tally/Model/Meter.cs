using System.Collections.Generic;

namespace Tally.Model
{
    public enum MeterMode
    {
        DamageDone,
        Dps,
        DamageTaken,
        Healing,
        MaxHit,
        Records
    }

    /// <summary>
    /// Owns the segments: Overall since the last reset, the fight in progress (or the last one),
    /// and the previous few fights. A fight starts on the first damage event and ends after the
    /// configured seconds without one.
    /// </summary>
    public sealed class Meter
    {
        public Segment Overall { get; private set; }
        public Segment Current { get; private set; }
        public readonly List<Segment> History = new List<Segment>();

        public bool InFight { get; private set; }
        public float LastCombatTime { get; private set; }

        /// <summary>Bumped on every change, so the UI can rebuild only when something happened.</summary>
        public int Revision { get; private set; }

        public Meter()
        {
            Reset();
        }

        public void Reset()
        {
            Overall = new Segment { Label = "Overall" };
            Current = new Segment { Label = "Current" };
            History.Clear();
            InFight = false;
            Revision++;
        }

        public void Add(CombatEvent e, float now, float activeGap, int historySize)
        {
            bool combat = e.Kind != EventKind.Heal;

            if (combat && !InFight)
            {
                InFight = true;
                Current = new Segment();
                LastCombatTime = now;
            }

            if (combat)
                LastCombatTime = now;

            Overall.Add(e, now, activeGap);

            // Heals outside a fight belong to Overall only: food ticking over at full health
            // between fights is not part of any fight.
            if (InFight)
                Current.Add(e, now, activeGap);

            Revision++;
        }

        /// <summary>Close the fight when nothing has happened for the timeout.</summary>
        public void Update(float now, float fightTimeout, int historySize)
        {
            if (!InFight || now - LastCombatTime < fightTimeout)
                return;

            InFight = false;
            Current.Closed = true;
            Current.EndTime = LastCombatTime;
            Current.Label = LabelFor(Current);

            History.Insert(0, Current);
            while (History.Count > historySize)
                History.RemoveAt(History.Count - 1);
            Revision++;
        }

        private static string LabelFor(Segment s)
        {
            string target = s.MainTarget() ?? "Fight";
            float d = s.EndTime - s.StartTime;
            int m = (int)(d / 60f);
            int sec = (int)(d % 60f);
            return string.Format("{0} ({1}:{2:00})", target, m, sec);
        }

        /// <summary>
        /// The segments a user can page through: Overall, Current, then earlier fights. The
        /// current segment is skipped in the history so it does not appear twice.
        /// </summary>
        public List<Segment> Segments()
        {
            var list = new List<Segment>(History.Count + 2) { Overall, Current };
            for (int i = 0; i < History.Count; i++)
            {
                if (!ReferenceEquals(History[i], Current))
                    list.Add(History[i]);
            }
            return list;
        }
    }
}
