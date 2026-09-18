using System.Collections.Generic;
using UnityEngine;

namespace Recount.Core
{
    /// <summary>
    /// Recount tints bars by class. Valheim has no classes, so each player gets a stable colour
    /// seeded from their name: the same player is the same colour on every client and every
    /// session. Drill-in rows (weapons) get colours the same way.
    /// </summary>
    public static class PlayerColors
    {
        private static readonly Dictionary<string, Color> _cache = new Dictionary<string, Color>();

        public static Color For(string name)
        {
            Color c;
            if (_cache.TryGetValue(name, out c))
                return c;

            uint h = 2166136261u;
            for (int i = 0; i < name.Length; i++)
            {
                h ^= name[i];
                h *= 16777619u;
            }

            // Spread hues with the golden ratio from the hash so two similar names do not land
            // on neighbouring hues. Saturation and value are fixed: readable text on every bar.
            float hue = ((h % 360u) / 360f + 0.618034f * ((h >> 16) % 7u)) % 1f;
            c = Color.HSVToRGB(hue, 0.55f, 0.78f);
            _cache[name] = c;
            return c;
        }
    }
}
