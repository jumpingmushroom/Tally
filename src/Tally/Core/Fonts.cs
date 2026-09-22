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
                + Describe(names) + "], " + rejected.Count + " with no glyphs");
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
        /// Can this font draw a row? Only the serialized character table is consulted. Asking
        /// HasCharacter() is what a first draft did and it answers no for a perfectly good font
        /// whose lookup table TMP has not built yet, which quietly hid most of the list.
        /// </summary>
        private static bool Usable(TMP_FontAsset f)
        {
            return Glyphs(f) > 0;
        }

        /// <summary>
        /// Valheim ships an "- Outline" material preset beside each of its fonts, which is how
        /// its own HUD text stays legible over bright scenery. Read-only: the shared asset is
        /// handed straight to the label, never written to. Null when a font has no preset, and
        /// then the text simply goes without rather than paying for a material instance each.
        /// </summary>
        public static Material Outline(TMP_FontAsset font)
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
                + (found != null ? found.name : "none, drawing plain"));
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
