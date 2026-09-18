using Recount.Model;
using Recount.Net;
using UnityEngine;

namespace Recount.Core
{
    /// <summary>
    /// The one door every event goes through: into the segments, past the records, and out to
    /// other players. Attribution decides what an event is; this decides what happens to it.
    /// </summary>
    public static class Recorder
    {
        public static readonly Meter Meter = new Meter();

        public static string LocalPlayerName
        {
            get
            {
                Player p = Player.m_localPlayer;
                return p != null ? p.GetPlayerName() : "";
            }
        }

        /// <summary>Record an event this client observed, and share it if sharing is on.</summary>
        public static void Record(CombatEvent e)
        {
            Add(e);

            if (PluginConfig.ShareEnabled.Value)
                EventSync.Queue(e);

            OfferRecord(e);
        }

        /// <summary>
        /// Record an event another client sent us. Never re-shared. It can still be a record:
        /// the local player's hits on creatures another client owns arrive this way, and they
        /// are the owner's real numbers.
        /// </summary>
        public static void RecordRemote(CombatEvent e)
        {
            e.Flags |= EventFlags.Remote;
            Add(e);
            OfferRecord(e);
        }

        private static void OfferRecord(CombatEvent e)
        {
            if (e.Kind != EventKind.Damage
                || e.Has(EventFlags.Approximate)
                || e.Has(EventFlags.Dot)
                || e.Has(EventFlags.NonCharacter)
                || e.Player != LocalPlayerName)
                return;

            RecordStore.RecordResult r = RecordStore.Offer(e);
            if (r != null)
                Fanfare.OnRecord(r, e);
        }

        private static void Add(CombatEvent e)
        {
            Meter.Add(e, Time.time, PluginConfig.ActiveGap.Value, PluginConfig.HistorySize.Value);

            if (PluginConfig.Verbose.Value)
            {
                RecountPlugin.Log.LogDebug(string.Format(
                    "{0}{1} {2} -> {3} [{4}] {5:0.#}{6}",
                    e.Has(EventFlags.Remote) ? "remote " : "",
                    e.Kind, e.Player, e.Target, e.Ability, e.Amount,
                    e.Flags == EventFlags.None ? "" : " " + e.Flags));
            }
        }

        public static void Update(float now)
        {
            Meter.Update(now, PluginConfig.FightTimeout.Value, PluginConfig.HistorySize.Value);
            DotTracker.Update(now);
        }

        public static void Reset()
        {
            Meter.Reset();
        }

        public static void Clear()
        {
            Meter.Reset();
            DotTracker.Clear();
            HealContext.Current = null;
        }
    }
}
