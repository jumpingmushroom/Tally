using System;
using System.Collections.Generic;
using System.Text;
using Tally.Model;
using Tally.Net;
using Tally.UI;
using UnityEngine;

namespace Tally.Core
{
    /// <summary>
    /// "tally" console command. What the meter shows is also readable here, which is how a
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

            new Terminal.ConsoleCommand("tally",
                "Tally combat meter: show | hide | reset | mode <name> | seg | report | dump | records | forget | sfx [name] | fonts | peers | stats",
                delegate (Terminal.ConsoleEventArgs args)
                {
                    string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "help";
                    switch (sub)
                    {
                        case "show": TallyPlugin.Window.SetOpen(true); break;
                        case "hide": TallyPlugin.Window.SetOpen(false); break;
                        case "toggle": TallyPlugin.Window.Toggle(); break;
                        case "reset": TallyPlugin.Window.Reset(); Say(args.Context, "Tally: reset."); break;
                        case "mode": Mode(args); break;
                        case "seg": case "segment": TallyPlugin.Window.CycleSegment(); Say(args.Context, "Tally: " + MeterView.SegmentName(TallyPlugin.Window.CurrentSegment(), Recorder.Meter)); break;
                        case "report": TallyPlugin.Window.Report(); break;
                        case "dump": Dump(args.Context); break;
                        case "records": Records(args.Context); break;
                        case "forget": RecordStore.Forget(); Say(args.Context, "Tally: forgot every record for this character and world."); break;
                        case "sfx": Sfx(args); break;
                        case "peers": Peers(args.Context); break;
                        case "fonts": FontList(args.Context); break;
                        case "font": FontSet(args); break;
                        case "stats": Say(args.Context, Diagnostics.Report()); Say(args.Context, string.Format(
                            "Tally: sent {0} packet(s)/{1} event(s), received {2}/{3}, dropped {4}",
                            EventSync.PacketsSent, EventSync.EventsSent, EventSync.PacketsReceived, EventSync.EventsReceived, EventSync.EventsDropped)); break;
                        default:
                            Say(args.Context, "tally show|hide|toggle - the window");
                            Say(args.Context, "tally reset           - clear every segment");
                            Say(args.Context, "tally mode <name>     - damage | dps | taken | healing | maxhit | records");
                            Say(args.Context, "tally seg             - next segment");
                            Say(args.Context, "tally report          - post the current view (Report button)");
                            Say(args.Context, "tally dump            - print the current view here");
                            Say(args.Context, "tally records         - print every stored record");
                            Say(args.Context, "tally forget          - delete the records for this character and world");
                            Say(args.Context, "tally sfx [name]      - play the configured (or named) fanfare; no name lists candidates");
                            Say(args.Context, "tally fonts           - every font the window can use (Window.Font)");
                            Say(args.Context, "tally font <name>     - switch the window to that font");
                            Say(args.Context, "tally peers           - other players seen running Tally");
                            Say(args.Context, "tally stats           - diagnostics and packet counters");
                            break;
                    }
                });
        }

        /// <summary>Console output also goes to the BepInEx log, so it can be read back from a file.</summary>
        private static void Say(Terminal ctx, string line)
        {
            ctx.AddString(line);
            TallyPlugin.Log.LogInfo(line);
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
                    TallyPlugin.Window.CycleMode();
                    Say(args.Context, "Tally: " + MeterView.ModeName(TallyPlugin.Window.Mode));
                    return;
            }
            TallyPlugin.Window.SetMode(mode);
            Say(args.Context, "Tally: " + MeterView.ModeName(mode));
        }

        private static void Dump(Terminal ctx)
        {
            MeterWindow w = TallyPlugin.Window;
            Segment seg = w.CurrentSegment();
            List<RowData> rows = MeterView.Rows(w.Mode, seg, w.Drill, Time.time);
            Say(ctx, string.Format("Tally: {0} · {1}{2} · {3} row(s), {4}",
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
                Say(ctx, "Tally: records are disabled or not loaded yet.");
                return;
            }
            Say(ctx, "Tally: records in " + RecordStore.Path);
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
                Say(args.Context, "Tally: " + SoundPlayer.Play(args[2], PluginConfig.RecordSoundVolume.Value));
                return;
            }

            Say(args.Context, "Tally: " + SoundPlayer.Play(PluginConfig.RecordSoundPrefab.Value, PluginConfig.RecordSoundVolume.Value));

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

        private static void FontList(Terminal ctx)
        {
            Fonts.Refresh();
            PluginConfig.RebindFontChoices(Fonts.Names);
            Say(ctx, "Tally: " + Fonts.Names.Count + " font(s), using " + PluginConfig.FontName.Value);
            foreach (string n in Fonts.Names)
                Say(ctx, "  " + n);
        }

        /// <summary>Set Window.Font by name; the window rebuilds itself on the change.</summary>
        private static void FontSet(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 3)
            {
                Say(args.Context, "Tally: usage \"tally font <name>\"; \"tally fonts\" lists them.");
                return;
            }

            // Names have spaces ("NotoSansJP-Regular SDF"): take everything after the subcommand.
            var parts = new List<string>();
            for (int i = 2; i < args.Length; i++)
                parts.Add(args[i]);
            string want = string.Join(" ", parts.ToArray()).Trim();
            Fonts.Refresh();
            foreach (string n in Fonts.Names)
            {
                if (string.Equals(n, want, StringComparison.OrdinalIgnoreCase))
                {
                    PluginConfig.FontName.Value = n;
                    Say(args.Context, "Tally: font is now " + n);
                    return;
                }
            }
            Say(args.Context, "Tally: no font named \"" + want + "\"; try \"tally fonts\".");
        }

        private static void Peers(Terminal ctx)
        {
            List<EventSync.PeerState> peers = EventSync.Peers();
            Say(ctx, "Tally: " + peers.Count + " other player(s) running Tally" + (EventSync.Registered ? "" : " (RPC not registered)"));
            foreach (EventSync.PeerState p in peers)
                Say(ctx, string.Format("  {0}  uid {1}  v{2}  seen {3:0}s ago  last seq {4}", p.Name, p.Uid, p.Version, Time.time - p.LastSeen, p.LastSeq));
        }
    }
}
