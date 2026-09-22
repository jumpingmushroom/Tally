using System;
using System.Collections.Generic;
using System.Text;
using Tally.Core;
using Tally.Model;
using UnityEngine;

namespace Tally.UI
{
    /// <summary>One bar's worth of data, independent of how it is drawn.</summary>
    public sealed class RowData
    {
        public string Key = "";
        public string Label = "";
        public float Value;
        public string ValueText = "";
        public Color Color;
        public bool Approximate;
        public bool Clickable;
        public Func<string> Tooltip;
    }

    /// <summary>
    /// Turns a segment into bars for the current mode and drill-in. Pure: no Unity objects,
    /// so it is testable by eye in the console (tally dump) as well as on screen.
    /// </summary>
    public static class MeterView
    {
        public static readonly MeterMode[] Modes =
        {
            MeterMode.DamageDone, MeterMode.Dps, MeterMode.DamageTaken, MeterMode.Healing, MeterMode.MaxHit, MeterMode.Records
        };

        public static string ModeName(MeterMode m)
        {
            switch (m)
            {
                case MeterMode.DamageDone: return "Damage Done";
                case MeterMode.Dps: return "DPS";
                case MeterMode.DamageTaken: return "Damage Taken";
                case MeterMode.Healing: return "Healing";
                case MeterMode.MaxHit: return "Max Hit";
                case MeterMode.Records: return "Records";
                default: return m.ToString();
            }
        }

        public static string SegmentName(Segment s, Meter meter)
        {
            if (ReferenceEquals(s, meter.Overall))
                return "Overall";
            if (ReferenceEquals(s, meter.Current))
                return meter.InFight ? "Current Fight" : "Last Fight";
            return s.Label;
        }

        public static List<RowData> Rows(MeterMode mode, Segment seg, string drill, float now)
        {
            var rows = new List<RowData>();
            if (mode == MeterMode.Records)
            {
                if (drill == null) RecordRows(rows);
                else RecordDrill(rows, drill);
            }
            else if (drill == null)
            {
                PlayerRows(rows, mode, seg);
            }
            else
            {
                PlayerStats p;
                if (seg.Players.TryGetValue(drill, out p))
                    DrillRows(rows, mode, p, seg);
            }

            rows.Sort((a, b) => b.Value.CompareTo(a.Value));
            return rows;
        }

        // ---- player list -------------------------------------------------------------------

        private static void PlayerRows(List<RowData> rows, MeterMode mode, Segment seg)
        {
            float total = 0f;
            foreach (PlayerStats p in seg.Players.Values)
                total += Value(mode, p);

            foreach (PlayerStats p in seg.Players.Values)
            {
                float v = Value(mode, p);
                if (v <= 0f)
                    continue;
                PlayerStats stats = p;
                Segment segment = seg;
                rows.Add(new RowData
                {
                    Key = p.Name,
                    Label = p.Name,
                    Value = v,
                    ValueText = mode == MeterMode.MaxHit ? Names.Amount(v) : Names.Amount(v) + " (" + Names.Percent(total > 0f ? v / total : 0f) + ")",
                    Color = PlayerColors.For(p.Name),
                    Approximate = p.Approximate && (mode == MeterMode.DamageDone || mode == MeterMode.Dps || mode == MeterMode.MaxHit),
                    Clickable = true,
                    Tooltip = () => PlayerTooltip(mode, stats, segment, total)
                });
            }
        }

        private static float Value(MeterMode mode, PlayerStats p)
        {
            switch (mode)
            {
                case MeterMode.DamageDone: return p.Damage;
                case MeterMode.Dps: return p.Damage > 0f ? p.Dps : 0f;
                case MeterMode.DamageTaken: return p.Taken;
                case MeterMode.Healing: return p.Healing;
                case MeterMode.MaxHit: return p.MaxHit;
                default: return 0f;
            }
        }

        private static string PlayerTooltip(MeterMode mode, PlayerStats p, Segment seg, float total)
        {
            var sb = new StringBuilder(256);
            sb.Append("<b>").Append(p.Name).Append("</b>");
            if (p.Approximate)
                sb.Append("  <color=#BCAF97>~ includes approximate hits</color>");
            sb.Append('\n');

            switch (mode)
            {
                case MeterMode.DamageDone:
                case MeterMode.Dps:
                    sb.Append("Damage ").Append(Names.Exact(p.Damage))
                      .Append("  ·  DPS ").Append(Names.Exact(p.Dps))
                      .Append("  ·  active ").Append(Names.Duration(p.ActiveTime)).Append('\n');
                    sb.Append("Hits ").Append(p.DamageHits)
                      .Append("  ·  avg ").Append(Names.Exact(p.DamageHits > 0 ? p.Damage / p.DamageHits : 0f))
                      .Append("  ·  max ").Append(Names.Exact(p.MaxHit)).Append('\n');
                    Buckets(sb, "Weapons", p.Weapons, p.Damage, mode == MeterMode.Dps ? p.ActiveTime : 0f);
                    break;

                case MeterMode.DamageTaken:
                    sb.Append("Taken ").Append(Names.Exact(p.Taken)).Append("  ·  hits ").Append(p.TakenHits)
                      .Append("  ·  avg ").Append(Names.Exact(p.TakenHits > 0 ? p.Taken / p.TakenHits : 0f)).Append('\n');
                    Buckets(sb, "From", p.TakenFrom, p.Taken, 0f);
                    break;

                case MeterMode.Healing:
                    sb.Append("Healed ").Append(Names.Exact(p.Healing))
                      .Append("  ·  overheal ").Append(Names.Exact(p.Overheal))
                      .Append("  ·  ticks ").Append(p.Heals).Append('\n');
                    Buckets(sb, "Sources", p.HealFrom, p.Healing, 0f);
                    break;

                case MeterMode.MaxHit:
                    if (p.TopHits.Count > 0)
                    {
                        HitRecord h = p.TopHits[0];
                        sb.Append("Biggest hit ").Append(Names.Exact(h.Amount)).Append(" with ").Append(h.Weapon)
                          .Append(" on ").Append(h.Target).Append(h.Backstab ? " (sneak attack)" : "").Append('\n');
                    }
                    sb.Append("<color=#BCAF97>Top hits:</color>\n");
                    for (int i = 0; i < p.TopHits.Count && i < 5; i++)
                    {
                        HitRecord h = p.TopHits[i];
                        sb.Append("  ").Append(Names.Exact(h.Amount)).Append("  ").Append(h.Weapon)
                          .Append(" → ").Append(h.Target).Append(h.Backstab ? " *" : "").Append('\n');
                    }
                    break;
            }
            sb.Append("<color=#BCAF97>Click for the breakdown</color>");
            return sb.ToString();
        }

        private static void Buckets(StringBuilder sb, string title, Dictionary<string, Bucket> map, float total, float activeTime)
        {
            var list = new List<Bucket>(map.Values);
            list.Sort((a, b) => b.Total.CompareTo(a.Total));
            sb.Append("<color=#BCAF97>").Append(title).Append(":</color>\n");
            for (int i = 0; i < list.Count && i < 6; i++)
            {
                Bucket b = list[i];
                sb.Append("  ").Append(b.Name).Append("  ").Append(Names.Amount(b.Total));
                if (activeTime > 0f)
                    sb.Append(" (").Append(Names.Exact(b.Total / Mathf.Max(1f, activeTime))).Append("/s)");
                else
                    sb.Append(" (").Append(Names.Percent(total > 0f ? b.Total / total : 0f)).Append(')');
                sb.Append('\n');
            }
            if (list.Count > 6)
                sb.Append("  <color=#BCAF97>and ").Append(list.Count - 6).Append(" more</color>\n");
        }

        // ---- drill-in --------------------------------------------------------------------

        private static void DrillRows(List<RowData> rows, MeterMode mode, PlayerStats p, Segment seg)
        {
            switch (mode)
            {
                case MeterMode.DamageDone:
                    BucketRows(rows, p.Weapons, p.Damage, 0f);
                    break;
                case MeterMode.Dps:
                    BucketRows(rows, p.Weapons, p.Damage, Mathf.Max(1f, p.ActiveTime));
                    break;
                case MeterMode.DamageTaken:
                    BucketRows(rows, p.TakenFrom, p.Taken, 0f);
                    break;
                case MeterMode.Healing:
                    BucketRows(rows, p.HealFrom, p.Healing, 0f);
                    break;
                case MeterMode.MaxHit:
                    for (int i = 0; i < p.TopHits.Count; i++)
                    {
                        HitRecord h = p.TopHits[i];
                        string label = h.Weapon + " → " + h.Target + (h.Backstab ? " *" : "");
                        rows.Add(new RowData
                        {
                            Key = "hit" + i,
                            Label = label,
                            Value = h.Amount,
                            ValueText = Names.Amount(h.Amount),
                            Color = PlayerColors.For(h.Weapon),
                            Approximate = h.Approximate,
                            Clickable = false,
                            Tooltip = () => Names.Exact(h.Amount) + " with " + h.Weapon + " on " + h.Target
                                            + (h.Backstab ? "\nSneak attack" : "") + (h.Approximate ? "\nApproximate (pre-mitigation)" : "")
                        });
                    }
                    break;
            }
        }

        private static void BucketRows(List<RowData> rows, Dictionary<string, Bucket> map, float total, float activeTime)
        {
            foreach (Bucket b in map.Values)
            {
                if (b.Total <= 0f)
                    continue;
                Bucket bucket = b;
                float v = activeTime > 0f ? b.Total / activeTime : b.Total;
                rows.Add(new RowData
                {
                    Key = b.Name,
                    Label = b.Name,
                    Value = v,
                    ValueText = Names.Amount(v) + " (" + Names.Percent(total > 0f ? b.Total / total : 0f) + ")",
                    Color = PlayerColors.For(b.Name),
                    Approximate = b.Approximate,
                    Clickable = false,
                    Tooltip = () => "<b>" + bucket.Name + "</b>\nTotal " + Names.Exact(bucket.Total)
                                    + "  ·  hits " + bucket.Hits
                                    + "  ·  avg " + Names.Exact(bucket.Hits > 0 ? bucket.Total / bucket.Hits : 0f)
                                    + "  ·  max " + Names.Exact(bucket.Max)
                                    + (bucket.Approximate ? "\n<color=#BCAF97>~ includes approximate hits</color>" : "")
                });
            }
        }

        // ---- records ---------------------------------------------------------------------

        private static void RecordRows(List<RowData> rows)
        {
            foreach (KeyValuePair<string, List<RecordStore.Record>> kv in RecordStore.Weapons)
            {
                if (kv.Value.Count == 0)
                    continue;
                RecordStore.Record best = kv.Value[0];
                string weapon = kv.Key;
                rows.Add(new RowData
                {
                    Key = weapon,
                    Label = weapon,
                    Value = best.Damage,
                    ValueText = Names.Amount(best.Damage),
                    Color = PlayerColors.For(weapon),
                    Clickable = true,
                    Tooltip = () => RecordTooltip(best, true)
                });
            }
        }

        private static void RecordDrill(List<RowData> rows, string weapon)
        {
            List<RecordStore.Record> list = RecordStore.ForWeapon(weapon);
            if (list == null)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                RecordStore.Record r = list[i];
                rows.Add(new RowData
                {
                    Key = "rec" + i,
                    Label = r.Target + "  " + r.When() + (r.Backstab ? " *" : ""),
                    Value = r.Damage,
                    ValueText = Names.Amount(r.Damage),
                    Color = PlayerColors.For(weapon),
                    Clickable = false,
                    Tooltip = () => RecordTooltip(r, false)
                });
            }
        }

        private static string RecordTooltip(RecordStore.Record r, bool header)
        {
            var sb = new StringBuilder(160);
            if (header)
                sb.Append("<b>").Append(r.Weapon).Append("</b>  best ");
            sb.Append(Names.Exact(r.Damage)).Append(" on ").Append(r.Target).Append('\n');
            if (!string.IsNullOrEmpty(r.DamageType))
                sb.Append(r.DamageType).Append("  ·  ");
            if (r.Backstab)
                sb.Append("sneak attack  ·  ");
            if (!string.IsNullOrEmpty(r.Skill))
                sb.Append(r.Skill).Append(" ").Append((int)r.SkillLevel).Append("  ·  ");
            sb.Append(r.When());
            if (!string.IsNullOrEmpty(r.World))
                sb.Append("  ·  ").Append(r.World);
            if (header)
                sb.Append("\n<color=#BCAF97>Click for the top 10</color>");
            return sb.ToString();
        }
    }
}
