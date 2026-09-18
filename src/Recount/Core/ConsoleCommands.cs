using System.Collections.Generic;
using System.Text;
using Recount.Model;
using Recount.Net;
using Recount.UI;
using UnityEngine;

namespace Recount.Core
{
    /// <summary>
    /// "recount" console command. What the meter shows is also readable here, which is how a
    /// misattributed hit gets diagnosed from a log file rather than a screenshot.
    /// </summary>
    public static class ConsoleCommands
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered)
                return;
            _registered = true;

            new Terminal.ConsoleCommand("recount",
                "Recount combat meter: show | hide | reset | mode <name> | seg | report | dump | records | forget | sfx [name] | peers | stats",
                delegate (Terminal.ConsoleEventArgs args)
                {
                    string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "help";
                    switch (sub)
                    {
                        case "show": RecountPlugin.Window.SetOpen(true); break;
                        case "hide": RecountPlugin.Window.SetOpen(false); break;
                        case "toggle": RecountPlugin.Window.Toggle(); break;
                        case "reset": RecountPlugin.Window.Reset(); Say(args.Context, "Recount: reset."); break;
                        case "mode": Mode(args); break;
                        case "seg": case "segment": RecountPlugin.Window.CycleSegment(); Say(args.Context, "Recount: " + MeterView.SegmentName(RecountPlugin.Window.CurrentSegment(), Recorder.Meter)); break;
                        case "report": RecountPlugin.Window.Report(); break;
                        case "dump": Dump(args.Context); break;
                        case "records": Records(args.Context); break;
                        case "forget": RecordStore.Forget(); Say(args.Context, "Recount: forgot every record for this character and world."); break;
                        case "sfx": Sfx(args); break;
                        case "peers": Peers(args.Context); break;
                        case "stats": Say(args.Context, Diagnostics.Report()); Say(args.Context, string.Format(
                            "Recount: sent {0} packet(s)/{1} event(s), received {2}/{3}, dropped {4}",
                            EventSync.PacketsSent, EventSync.EventsSent, EventSync.PacketsReceived, EventSync.EventsReceived, EventSync.EventsDropped)); break;
                        default:
                            Say(args.Context, "recount show|hide|toggle - the window");
                            Say(args.Context, "recount reset           - clear every segment");
                            Say(args.Context, "recount mode <name>     - damage | dps | taken | healing | maxhit | records");
                            Say(args.Context, "recount seg             - next segment");
                            Say(args.Context, "recount report          - post the current view (Report button)");
                            Say(args.Context, "recount dump            - print the current view here");
                            Say(args.Context, "recount records         - print every stored record");
                            Say(args.Context, "recount forget          - delete the records for this character and world");
                            Say(args.Context, "recount sfx [name]      - play the configured (or named) fanfare; no name lists candidates");
                            Say(args.Context, "recount peers           - other players seen running Recount");
                            Say(args.Context, "recount stats           - diagnostics and packet counters");
                            break;
                    }
                });
        }

        /// <summary>Console output also goes to the BepInEx log, so it can be read back from a file.</summary>
        private static void Say(Terminal ctx, string line)
        {
            ctx.AddString(line);
            RecountPlugin.Log.LogInfo(line);
        }

        private static void Mode(Terminal.ConsoleEventArgs args)
        {
            string name = args.Length > 2 ? args[2].ToLowerInvariant() : "";
            MeterMode mode;
            switch (name)
            {
                case "damage": case "done": mode = MeterMode.DamageDone; break;
                case "dps": mode = MeterMode.Dps; break;
                case "taken": mode = MeterMode.DamageTaken; break;
                case "healing": case "heal": mode = MeterMode.Healing; break;
                case "maxhit": case "max": mode = MeterMode.MaxHit; break;
                case "records": case "rec": mode = MeterMode.Records; break;
                default:
                    RecountPlugin.Window.CycleMode();
                    Say(args.Context, "Recount: " + MeterView.ModeName(RecountPlugin.Window.Mode));
                    return;
            }
            RecountPlugin.Window.SetMode(mode);
            Say(args.Context, "Recount: " + MeterView.ModeName(mode));
        }

        private static void Dump(Terminal ctx)
        {
            MeterWindow w = RecountPlugin.Window;
            Segment seg = w.CurrentSegment();
            List<RowData> rows = MeterView.Rows(w.Mode, seg, w.Drill, Time.time);
            Say(ctx, string.Format("Recount: {0} · {1}{2} · {3} row(s), {4}",
                MeterView.ModeName(w.Mode), MeterView.SegmentName(seg, Recorder.Meter),
                w.Drill != null ? " · " + w.Drill : "", rows.Count,
                Recorder.Meter.InFight ? "in combat" : "out of combat"));
            for (int i = 0; i < rows.Count; i++)
            {
                RowData r = rows[i];
                Say(ctx, string.Format("  {0}. {1}  {2}{3}", i + 1, r.Label, r.Approximate ? "~" : "", r.ValueText));
            }
        }

        private static void Records(Terminal ctx)
        {
            RecordStore.EnsureLoaded();
            if (!RecordStore.Loaded)
            {
                Say(ctx, "Recount: records are disabled or not loaded yet.");
                return;
            }
            Say(ctx, "Recount: records in " + RecordStore.Path);
            RecordStore.Record o = RecordStore.Overall;
            if (o != null)
                Say(ctx, string.Format("  all-time: {0} with {1} on {2} ({3}{4}{5})", Names.Exact(o.Damage), o.Weapon, o.Target,
                    o.DamageType, o.Backstab ? ", sneak attack" : "", o.Skill != "" ? ", " + o.Skill + " " + (int)o.SkillLevel : ""));
            foreach (KeyValuePair<string, List<RecordStore.Record>> kv in RecordStore.Weapons)
            {
                var sb = new StringBuilder();
                sb.Append("  ").Append(kv.Key).Append(": ");
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Names.Exact(kv.Value[i].Damage));
                }
                Say(ctx, sb.ToString());
            }
        }

        private static void Sfx(Terminal.ConsoleEventArgs args)
        {
            if (args.Length > 2)
            {
                Say(args.Context, "Recount: " + SoundPlayer.Play(args[2], PluginConfig.RecordSoundVolume.Value));
                return;
            }

            Say(args.Context, "Recount: " + SoundPlayer.Play(PluginConfig.RecordSoundPrefab.Value, PluginConfig.RecordSoundVolume.Value));

            List<GameObject> levelUp = SoundPlayer.LevelUpPrefabs();
            var sb = new StringBuilder("  level-up effect prefabs: ");
            for (int i = 0; i < levelUp.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(levelUp[i].name).Append(levelUp[i].GetComponentInChildren<ZSFX>(true) != null ? " (has sfx)" : "");
            }
            Say(args.Context, levelUp.Count > 0 ? sb.ToString() : "  no level-up effect prefabs found on the player");

            foreach (string name in new[] { "fx_GP_Activation", "sfx_secretfound", "sfx_lootspawn", "sfx_perfectblock", "sfx_offering", SoundPlayer.LevelUpAlias })
            {
                SoundPlayer.Resolved r = SoundPlayer.Resolve(name);
                Say(args.Context, "  " + name + ": " + (r.Ok ? "ok, " + r.Clips.Length + " clip(s)" : r.Error));
            }
        }

        private static void Peers(Terminal ctx)
        {
            List<EventSync.PeerState> peers = EventSync.Peers();
            Say(ctx, "Recount: " + peers.Count + " other player(s) running Recount" + (EventSync.Registered ? "" : " (RPC not registered)"));
            foreach (EventSync.PeerState p in peers)
                Say(ctx, string.Format("  {0}  uid {1}  v{2}  seen {3:0}s ago  last seq {4}", p.Name, p.Uid, p.Version, Time.time - p.LastSeen, p.LastSeq));
        }
    }
}
