using System;
using System.Collections.Generic;
using System.Text;
using Jotunn.Managers;
using TMPro;
using UnityEngine;

namespace Tally.Core
{
    /// <summary>
    /// Every TextMeshPro font the game has loaded, by asset name: Valheim's own
    /// (Valheim-AveriaSerifLibre, Valheim-Norse and their variants) and whatever other mods
    /// brought along. Nothing is shipped.
    ///
    /// Two lists, deliberately. Resolve() searches everything found, so a font named in the
    /// config always draws even if the judgement below is wrong about it. Names is only what
    /// the dropdown offers, and there Usable() drops the ~35 Noto fallback assets the game
    /// carries for scripts it does not otherwise render: those have empty atlases, and a
    /// window set to one draws nothing at all.
    /// </summary>
    public static class Fonts
    {
        public const string DefaultName = "Valheim-AveriaSerifLibre";

        private static readonly Dictionary<string, TMP_FontAsset> _byName = new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);
        private static List<string> _names = new List<string> { DefaultName, "Valheim-AveriaSansLibre", "Valheim-Norse" };
        private static bool _scanned;
        private static readonly Dictionary<string, Material> _outlines = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _fallbacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const string OutlineKeyword = "OUTLINE_ON";
        private const float DefaultOutlineWidth = 0.15f;

        /// <summary>Rescan. Called when the GUI is (re)built, which is when fonts are loaded.</summary>
        public static void Refresh()
        {
            _byName.Clear();
            _outlines.Clear();
            try
            {
                foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (f == null || string.IsNullOrEmpty(f.name))
                        continue;
                    // Runtime fallbacks and atlas copies share a base name with " Material" or
                    // similar suffixes; keep the first asset seen per name.
                    if (!_byName.ContainsKey(f.name))
                        _byName[f.name] = f;
                }
            }
            catch (Exception ex)
            {
                TallyPlugin.Log.LogWarning("font scan failed: " + ex.Message);
            }

            GUIManager gui = GUIManager.Instance;
            if (gui.TMP_Norse != null && !_byName.ContainsKey(gui.TMP_Norse.name))
                _byName[gui.TMP_Norse.name] = gui.TMP_Norse;
            if (gui.TMP_AveriaSansLibre != null && !_byName.ContainsKey(gui.TMP_AveriaSansLibre.name))
                _byName[gui.TMP_AveriaSansLibre.name] = gui.TMP_AveriaSansLibre;

            CollectFallbacks();

            string configured = PluginConfig.FontName != null ? PluginConfig.FontName.Value : null;
            var names = new List<string>();
            var rejected = new List<string>();
            foreach (KeyValuePair<string, TMP_FontAsset> kv in _byName)
            {
                // Never hide the default or whatever is in use: the dropdown has to be able to
                // show the setting it is editing.
                bool keep = Usable(kv.Value)
                    || string.Equals(kv.Key, DefaultName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kv.Key, configured, StringComparison.OrdinalIgnoreCase);
                if (keep)
                    names.Add(kv.Key);
                else
                    rejected.Add(kv.Key);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            // Valheim's own fonts first, the default at the top.
            names.Sort((a, b) =>
            {
                int ra = Rank(a), rb = Rank(b);
                return ra != rb ? ra.CompareTo(rb) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            _names = names;
            _scanned = true;

            TallyPlugin.Log.LogInfo("fonts: " + _byName.Count + " found, " + names.Count + " offered ["
                + Describe(names) + "], " + rejected.Count + " rejected (" + _fallbacks.Count + " are TMP fallbacks)");
            if (PluginConfig.Verbose != null && PluginConfig.Verbose.Value && rejected.Count > 0)
                TallyPlugin.Log.LogInfo("fonts rejected: " + string.Join(", ", rejected.ToArray()));
        }

        /// <summary>Offered fonts with their glyph counts, which is what makes a bad cut obvious.</summary>
        private static string Describe(List<string> names)
        {
            var sb = new StringBuilder(128);
            for (int i = 0; i < names.Count && i < 12; i++)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(names[i]).Append(':').Append(Glyphs(_byName[names[i]]));
            }
            return sb.ToString();
        }

        private static int Glyphs(TMP_FontAsset f)
        {
            try
            {
                return f.characterTable != null ? f.characterTable.Count : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static int Rank(string name)
        {
            if (string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.StartsWith("Valheim", StringComparison.OrdinalIgnoreCase)) return 1;
            return 2;
        }

        /// <summary>
        /// The fonts TMP holds as fallbacks for scripts the game does not otherwise draw. They
        /// are asked for a glyph only when no real font has it, so they are never a sensible
        /// choice for the window, and their atlases fill up as the game runs: judging them by
        /// glyph count alone let NotoEmoji into the list at one glyph once something drew an
        /// emoji.
        /// </summary>
        private static void CollectFallbacks()
        {
            _fallbacks.Clear();
            try
            {
                List<TMP_FontAsset> global = TMP_Settings.fallbackFontAssets;
                if (global != null)
                {
                    foreach (TMP_FontAsset f in global)
                        if (f != null && !string.IsNullOrEmpty(f.name))
                            _fallbacks.Add(f.name);
                }
                foreach (TMP_FontAsset f in _byName.Values)
                {
                    List<TMP_FontAsset> table = f != null ? f.fallbackFontAssetTable : null;
                    if (table == null)
                        continue;
                    foreach (TMP_FontAsset fb in table)
                        if (fb != null && !string.IsNullOrEmpty(fb.name))
                            _fallbacks.Add(fb.name);
                }
            }
            catch (Exception ex)
            {
                TallyPlugin.Log.LogWarning("fallback scan failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Can this font draw a row? It must have glyphs of its own and must not be one of the
        /// fallbacks. Asking HasCharacter() is what a first draft did and it answers no for a
        /// perfectly good font whose lookup table TMP has not built yet, which hid all but two.
        /// </summary>
        private static bool Usable(TMP_FontAsset f)
        {
            if (f == null || Glyphs(f) <= 0)
                return false;
            if (_fallbacks.Contains(f.name))
                return false;
            // Valheim names its own fallback set this way; belt and braces if TMP's list is empty.
            return !f.name.StartsWith("Fallback-", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Outline a label the way the game's own HUD text is outlined: the settings come from
        /// Valheim's "- Outline" preset for that font, but they are written to the label's own
        /// material instance.
        ///
        /// Handing the preset material straight to the label is what the first cut did. It held
        /// until the font was switched mid-session and then every label went blank. These are
        /// dynamic atlas assets: TMP grows and rebuilds the atlas as new glyphs are drawn, while
        /// the preset still points at the texture it was authored against, so the label samples
        /// an atlas that no longer holds its glyphs. A material instance derives from the font's
        /// live material and follows the atlas wherever it goes.
        /// </summary>
        public static void ApplyOutline(TMP_Text label)
        {
            if (label == null || label.font == null)
                return;

            float width = DefaultOutlineWidth;
            Color color = Color.black;
            Material preset = OutlinePreset(label.font);
            if (preset != null)
            {
                try
                {
                    if (preset.HasProperty(ShaderUtilities.ID_OutlineWidth))
                    {
                        float w = preset.GetFloat(ShaderUtilities.ID_OutlineWidth);
                        if (w > 0f)
                            width = w;
                    }
                    if (preset.HasProperty(ShaderUtilities.ID_OutlineColor))
                        color = preset.GetColor(ShaderUtilities.ID_OutlineColor);
                }
                catch (Exception ex)
                {
                    TallyPlugin.Log.LogWarning("outline preset unreadable: " + ex.Message);
                }
            }

            try
            {
                Material mine = label.fontMaterial;   // an instance, tied to the live atlas
                mine.EnableKeyword(OutlineKeyword);
                mine.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
                mine.SetColor(ShaderUtilities.ID_OutlineColor, color);
            }
            catch (Exception ex)
            {
                TallyPlugin.Log.LogWarning("outline failed: " + ex.Message);
            }
        }

        private static Material OutlinePreset(TMP_FontAsset font)
        {
            if (font == null || string.IsNullOrEmpty(font.name))
                return null;

            Material cached;
            if (_outlines.TryGetValue(font.name, out cached))
                return cached;

            Material found = null;
            try
            {
                foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (m == null || string.IsNullOrEmpty(m.name))
                        continue;
                    if (m.name.StartsWith(font.name, StringComparison.OrdinalIgnoreCase)
                        && m.name.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0
                        && m.name.IndexOf("Thick", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        found = m;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                TallyPlugin.Log.LogWarning("outline lookup failed for " + font.name + ": " + ex.Message);
            }

            _outlines[font.name] = found;
            TallyPlugin.Log.LogInfo("outline for " + font.name + ": "
                + (found != null ? found.name + " (settings only)" : "none, using defaults"));
            return found;
        }

        public static bool Scanned => _scanned;

        public static IList<string> Names => _names;

        /// <summary>The named font, else the default, else whatever Jotunn hands out.</summary>
        public static TMP_FontAsset Resolve(string name)
        {
            TMP_FontAsset f;
            if (!string.IsNullOrEmpty(name) && _byName.TryGetValue(name, out f) && f != null)
                return f;
            if (!string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase)
                && _byName.TryGetValue(DefaultName, out f) && f != null)
                return f;
            return GUIManager.Instance.TMP_Norse;
        }

        /// <summary>The configured font, or the default when the name is unknown.</summary>
        public static TMP_FontAsset Current
        {
            get { return Resolve(PluginConfig.FontName.Value); }
        }
    }
}
