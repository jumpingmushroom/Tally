using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Tally.Core;
using UnityEngine;

namespace Tally
{
    public enum ReportTarget
    {
        /// <summary>Post the lines to in-game chat, where everyone nearby reads them.</summary>
        Chat,

        /// <summary>Print the lines to the console and the BepInEx log only.</summary>
        Console
    }

    public static class PluginConfig
    {
        // General
        public static ConfigEntry<KeyboardShortcut> ToggleKey;
        public static ConfigEntry<KeyboardShortcut> ResetKey;
        public static ConfigEntry<KeyboardShortcut> CycleModeKey;
        public static ConfigEntry<KeyboardShortcut> CycleSegmentKey;
        public static ConfigEntry<KeyboardShortcut> MouseKey;
        public static ConfigEntry<bool> ShowOnLogin;

        // Window
        public static ConfigEntry<Vector2> WindowPosition;
        public static ConfigEntry<float> WindowWidth;
        public static ConfigEntry<float> WindowHeight;
        public static ConfigEntry<float> Opacity;
        public static ConfigEntry<float> BarOpacity;
        public static ConfigEntry<int> FontSize;
        public static ConfigEntry<string> FontName;
        public static ConfigEntry<bool> TextOutline;
        public static ConfigEntry<float> RowHeight;

        // Tracking
        public static ConfigEntry<float> FightTimeout;
        public static ConfigEntry<float> ActiveGap;
        public static ConfigEntry<int> HistorySize;
        public static ConfigEntry<bool> IncludeStructures;

        // Sharing
        public static ConfigEntry<bool> ShareEnabled;
        public static ConfigEntry<float> ShareRange;
        public static ConfigEntry<float> FlushInterval;

        // Records
        public static ConfigEntry<bool> RecordsEnabled;
        public static ConfigEntry<bool> RecordBanner;
        public static ConfigEntry<bool> RecordSound;
        public static ConfigEntry<string> RecordSoundPrefab;
        public static ConfigEntry<float> RecordSoundVolume;
        public static ConfigEntry<float> RecordMinDamage;
        public static ConfigEntry<float> BannerSeconds;

        // Report
        public static ConfigEntry<ReportTarget> ReportTo;
        public static ConfigEntry<int> ReportLines;

        // Logging
        public static ConfigEntry<bool> Verbose;

        /// <summary>Vector2.zero means "not positioned yet": the window picks a default spot.</summary>
        public static readonly Vector2 DefaultPosition = Vector2.zero;

        /// <summary>Raised when something that changes the window's geometry or style is edited.</summary>
        public static event Action LayoutChanged;

        /// <summary>Raised when the font is changed; the window is rebuilt with the new face.</summary>
        public static event Action FontChanged;

        private static ConfigFile _cfg;
        private const string FontDescription =
            "Font for the window, banner and tooltip, by TextMeshPro asset name. The list holds every " +
            "font the game has loaded that can draw Latin text, once you are in a world; \"tally fonts\" " +
            "in the console prints it and \"tally font <name>\" switches. Valheim-AveriaSerifLibre is the " +
            "game's item-text face, Valheim-AveriaSansLibre its plainer sans, Valheim-Norse the runic " +
            "display face the title bars use.";

        // ConfigurationManagerAttributes is supplied by Jotunn (global namespace). Config
        // managers match it by type name via reflection, so no dependency on any particular
        // ConfigurationManager build is implied and nothing breaks if none is installed.
        private static ConfigurationManagerAttributes Attr(int order, bool advanced = false)
        {
            return new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced };
        }

        public static void Bind(ConfigFile cfg)
        {
            _cfg = cfg;
            ToggleKey = cfg.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F7),
                new ConfigDescription("Show or hide the meter window.", null, Attr(100)));

            ResetKey = cfg.Bind("General", "ResetKey", KeyboardShortcut.Empty,
                new ConfigDescription("Clear every segment. Same as the Reset button. Unbound by default.", null, Attr(95)));

            CycleModeKey = cfg.Bind("General", "CycleModeKey", KeyboardShortcut.Empty,
                new ConfigDescription("Step to the next mode (Damage Done, DPS, Damage Taken, Healing, Max Hit, Records). Unbound by default.",
                    null, Attr(90)));

            CycleSegmentKey = cfg.Bind("General", "CycleSegmentKey", KeyboardShortcut.Empty,
                new ConfigDescription("Step to the next segment (Overall, Current, previous fights). Unbound by default.",
                    null, Attr(85)));

            MouseKey = cfg.Bind("General", "MouseKey", new KeyboardShortcut(KeyCode.LeftAlt),
                new ConfigDescription(
                    "Hold this to free the cursor so you can click rows and buttons, drag and resize " +
                    "the window while playing. Attacks and camera are blocked while it is held. Not " +
                    "needed while the map or inventory is open, when the cursor is already free.",
                    null, Attr(80)));

            ShowOnLogin = cfg.Bind("General", "ShowOnLogin", true,
                new ConfigDescription("Open the window when you enter a world.", null, Attr(75)));

            WindowPosition = cfg.Bind("Window", "Position", DefaultPosition,
                new ConfigDescription(
                    "Top-left corner in canvas pixels, measured from the top-left of the screen (y is " +
                    "negative going down). Written back when you drag the window. 0,0 means \"not placed " +
                    "yet\": the window goes to the right edge, a third of the way down.",
                    null, Attr(70)));

            WindowWidth = cfg.Bind("Window", "Width", 300f,
                new ConfigDescription("Window width. Written back when you resize with the corner handle.",
                    new AcceptableValueRange<float>(180f, 900f), Attr(65)));

            WindowHeight = cfg.Bind("Window", "Height", 220f,
                new ConfigDescription("Window height. Written back when you resize with the corner handle.",
                    new AcceptableValueRange<float>(80f, 1200f), Attr(60)));

            Opacity = cfg.Bind("Window", "Opacity", 0.75f,
                new ConfigDescription("Opacity of the window background.",
                    new AcceptableValueRange<float>(0f, 1f), Attr(55)));

            BarOpacity = cfg.Bind("Window", "BarOpacity", 0.8f,
                new ConfigDescription("Opacity of the coloured bars.",
                    new AcceptableValueRange<float>(0.1f, 1f), Attr(54)));

            FontSize = cfg.Bind("Window", "FontSize", 14,
                new ConfigDescription("Text size in the bars.", new AcceptableValueRange<int>(9, 24), Attr(50)));

            FontName = cfg.Bind("Window", "Font", Fonts.DefaultName,
                new ConfigDescription(FontDescription, new AcceptableValueList<string>(new List<string>(Fonts.Names).ToArray()), Attr(48)));

            TextOutline = cfg.Bind("Window", "TextOutline", true,
                new ConfigDescription("Outline the text, the way the game's own HUD does, so it stays readable where the window is see-through. Uses the font's own outline material; fonts without one draw plain.", null, Attr(46)));

            RowHeight = cfg.Bind("Window", "RowHeight", 19f,
                new ConfigDescription("Height of each bar.", new AcceptableValueRange<float>(12f, 40f), Attr(45)));

            FightTimeout = cfg.Bind("Tracking", "FightTimeout", 5f,
                new ConfigDescription("A fight ends after this many seconds without any damage dealt or taken.",
                    new AcceptableValueRange<float>(1f, 60f), Attr(40)));

            ActiveGap = cfg.Bind("Tracking", "ActiveGap", 5f,
                new ConfigDescription(
                    "DPS divides damage by the time a player spent actively fighting: the gaps between " +
                    "their hits, each capped at this many seconds. A player who joins late is not " +
                    "penalised for the time before they arrived.",
                    new AcceptableValueRange<float>(1f, 30f), Attr(38)));

            HistorySize = cfg.Bind("Tracking", "HistorySize", 5,
                new ConfigDescription("How many previous fights the Segments button can page back through.",
                    new AcceptableValueRange<int>(1, 20), Attr(36)));

            IncludeStructures = cfg.Bind("Tracking", "IncludeStructures", false,
                new ConfigDescription(
                    "Also count damage to trees, logs, rocks, ore and built pieces. Off by default: " +
                    "chopping wood is not combat.",
                    null, Attr(34)));

            ShareEnabled = cfg.Bind("Sharing", "Enabled", true,
                new ConfigDescription(
                    "Send what this client sees to other players running the mod, and accept what " +
                    "they send. Nothing is installed on the server; players without the mod are " +
                    "unaffected.",
                    null, Attr(30)));

            ShareRange = cfg.Bind("Sharing", "Range", 100f,
                new ConfigDescription("Ignore events from other players that happened further than this many metres from you.",
                    new AcceptableValueRange<float>(10f, 1000f), Attr(28)));

            FlushInterval = cfg.Bind("Sharing", "FlushInterval", 0.5f,
                new ConfigDescription("Seconds between batched packets to other players.",
                    new AcceptableValueRange<float>(0.1f, 5f), Attr(26, advanced: true)));

            RecordsEnabled = cfg.Bind("Records", "Enabled", true,
                new ConfigDescription(
                    "Remember your biggest single hit per weapon and overall across sessions, per " +
                    "character and world. Stored in BepInEx/config/Tally/.",
                    null, Attr(24)));

            RecordBanner = cfg.Bind("Records", "Banner", true,
                new ConfigDescription("Show a centred banner when a record is beaten.", null, Attr(22)));

            RecordSound = cfg.Bind("Records", "Sound", true,
                new ConfigDescription("Play a fanfare when a record is beaten.", null, Attr(20)));

            RecordSoundPrefab = cfg.Bind("Records", "SoundPrefab", "fx_GP_Activation",
                new ConfigDescription(
                    "Name of a vanilla sound prefab to play, resolved through ZNetScene. Candidates that " +
                    "read as celebratory: fx_GP_Activation (guardian power), sfx_secretfound (discovery " +
                    "chime), sfx_lootspawn. The special value @levelup plays the skill level-up sound. " +
                    "\"tally sfx <name>\" in the console previews one; a missing prefab just skips the sound.",
                    null, Attr(18)));

            RecordSoundVolume = cfg.Bind("Records", "SoundVolume", 0.8f,
                new ConfigDescription("Fanfare volume, on top of the game's effects volume.",
                    new AcceptableValueRange<float>(0f, 1f), Attr(16)));

            RecordMinDamage = cfg.Bind("Records", "MinDamage", 50f,
                new ConfigDescription("No banner or sound for records below this much damage, so a fresh character is not serenaded for every punch.",
                    new AcceptableValueRange<float>(0f, 5000f), Attr(14)));

            BannerSeconds = cfg.Bind("Records", "BannerSeconds", 4f,
                new ConfigDescription("How long the banner stays before fading.",
                    new AcceptableValueRange<float>(1f, 15f), Attr(12)));

            ReportTo = cfg.Bind("Report", "Target", ReportTarget.Chat,
                new ConfigDescription("Where the Report button sends the current view: in-game chat (everyone nearby sees it) or your own console.",
                    null, Attr(10)));

            ReportLines = cfg.Bind("Report", "Lines", 5,
                new ConfigDescription("How many rows a report includes.", new AcceptableValueRange<int>(1, 15), Attr(8)));

            Verbose = cfg.Bind("Logging", "Verbose", false,
                new ConfigDescription("Log every recorded event and packet to the BepInEx log. Off by default.",
                    null, Attr(5, advanced: true)));

            WindowPosition.SettingChanged += (s, e) => Raise(LayoutChanged);
            WindowWidth.SettingChanged += (s, e) => Raise(LayoutChanged);
            WindowHeight.SettingChanged += (s, e) => Raise(LayoutChanged);
            Opacity.SettingChanged += (s, e) => Raise(LayoutChanged);
            BarOpacity.SettingChanged += (s, e) => Raise(LayoutChanged);
            FontSize.SettingChanged += (s, e) => Raise(LayoutChanged);
            RowHeight.SettingChanged += (s, e) => Raise(LayoutChanged);
            FontName.SettingChanged += (s, e) => Raise(FontChanged);
            TextOutline.SettingChanged += (s, e) => Raise(FontChanged);
        }

        /// <summary>
        /// The fonts are only known once the game has loaded them, long after Bind. Re-bind the
        /// entry with the real list so ConfigurationManager offers a dropdown of every font
        /// present, keeping the current choice when it is still available.
        /// </summary>
        public static void RebindFontChoices(IList<string> names)
        {
            if (_cfg == null || FontName == null || names == null || names.Count == 0)
                return;

            string current = FontName.Value;
            _cfg.Remove(FontName.Definition);
            FontName = _cfg.Bind("Window", "Font", Fonts.DefaultName,
                new ConfigDescription(FontDescription, new AcceptableValueList<string>(new List<string>(names).ToArray()), Attr(48)));
            FontName.SettingChanged += (s, e) => Raise(FontChanged);
            TextOutline.SettingChanged += (s, e) => Raise(FontChanged);

            foreach (string n in names)
            {
                if (string.Equals(n, current, StringComparison.OrdinalIgnoreCase))
                {
                    if (FontName.Value != n)
                        FontName.Value = n;
                    return;
                }
            }
            if (!string.IsNullOrEmpty(current) && current != FontName.Value)
                TallyPlugin.Log.LogWarning("font \"" + current + "\" is not loaded; using " + FontName.Value);
        }

        private static void Raise(Action a)
        {
            if (a == null)
                return;
            try
            {
                a();
            }
            catch (Exception ex)
            {
                TallyPlugin.Log.LogWarning("config listener threw: " + ex);
            }
        }
    }
}
