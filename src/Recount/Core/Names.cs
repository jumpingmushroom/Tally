using System.Globalization;
using System.Text;

namespace Recount.Core
{
    /// <summary>
    /// Display names and number formatting. Every string the meter shows passes through here,
    /// so an untranslated token never reaches a bar.
    /// </summary>
    public static class Names
    {
        public const string Unknown = "Unknown";

        public static string Localize(string token)
        {
            if (string.IsNullOrEmpty(token))
                return "";
            if (token[0] != '$')
                return token;

            string localized = Localization.instance != null ? Localization.instance.Localize(token) : token;
            return IsUntranslated(localized) ? Prettify(token.Substring(1)) : localized;
        }

        /// <summary>A token the game could not translate comes back wrapped in square brackets.</summary>
        private static bool IsUntranslated(string localized)
        {
            return string.IsNullOrEmpty(localized)
                   || (localized.Length > 1 && localized[0] == '[' && localized[localized.Length - 1] == ']');
        }

        public static string Item(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null)
                return Unknown;
            string name = Localize(item.m_shared.m_name);
            if (string.IsNullOrEmpty(name) && item.m_dropPrefab != null)
                name = Prettify(item.m_dropPrefab.name);
            return string.IsNullOrEmpty(name) ? Unknown : name;
        }

        public static string Skill(Skills.SkillType skill)
        {
            if (skill == Skills.SkillType.None)
                return Unknown;
            return Localize("$skill_" + skill.ToString().ToLowerInvariant());
        }

        public static string Character(Character c)
        {
            if (c == null)
                return Unknown;
            var p = c as Player;
            if (p != null)
            {
                string n = p.GetPlayerName();
                return string.IsNullOrEmpty(n) ? "Player" : n;
            }
            string hover = c.GetHoverName();
            return string.IsNullOrEmpty(hover) ? Prettify(c.m_name) : hover;
        }

        public static string StatusEffect(StatusEffect se)
        {
            if (se == null)
                return "Effect";
            string n = Localize(se.m_name);
            return string.IsNullOrEmpty(n) ? Prettify(se.name) : n;
        }

        /// <summary>What to call damage that has no attacker: falling, drowning, the environment.</summary>
        public static string HitType(HitData.HitType type)
        {
            switch (type)
            {
                case HitData.HitType.Fall: return "Falling";
                case HitData.HitType.Drowning: return "Drowning";
                case HitData.HitType.Burning: return "Burning";
                case HitData.HitType.Freezing: return "Freezing";
                case HitData.HitType.Poisoned: return "Poison";
                case HitData.HitType.Water: return "Water";
                case HitData.HitType.Smoke: return "Smoke";
                case HitData.HitType.EdgeOfWorld: return "Edge of the world";
                case HitData.HitType.Impact: return "Impact";
                case HitData.HitType.Cart: return "Cart";
                case HitData.HitType.Tree: return "Falling tree";
                case HitData.HitType.Self: return "Self";
                case HitData.HitType.Structural: return "Collapse";
                case HitData.HitType.Turret: return "Ballista";
                case HitData.HitType.Boat: return "Boat";
                case HitData.HitType.Stalagtite: return "Stalactite";
                case HitData.HitType.Catapult: return "Catapult";
                case HitData.HitType.CinderFire: return "Cinders";
                case HitData.HitType.AshlandsOcean: return "Boiling sea";
                case HitData.HitType.AshlandsLava: return "Lava";
                case HitData.HitType.Incinerator: return "Incinerator";
                default: return Unknown;
            }
        }

        public static string DamageType(HitData.DamageType t)
        {
            return t == 0 ? "" : t.ToString();
        }

        /// <summary>"12.4k", "1.23M", "487". Recount's compact style; fits inside a narrow bar.</summary>
        public static string Amount(float v)
        {
            if (v < 0f) v = 0f;
            if (v < 1000f)
                return ((int)(v + 0.5f)).ToString(CultureInfo.InvariantCulture);
            if (v < 1000000f)
                return (v / 1000f).ToString(v < 10000f ? "0.00" : "0.0", CultureInfo.InvariantCulture) + "k";
            return (v / 1000000f).ToString("0.00", CultureInfo.InvariantCulture) + "M";
        }

        /// <summary>"12,430" for tooltips, where there is room.</summary>
        public static string Exact(float v)
        {
            return ((long)(v + 0.5f)).ToString("#,0", CultureInfo.InvariantCulture);
        }

        public static string Percent(float share)
        {
            return ((int)(share * 100f + 0.5f)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        public static string Duration(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int m = (int)(seconds / 60f);
            int s = (int)(seconds % 60f);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", m, s);
        }

        /// <summary>"ArmorStand_Male" -> "Armor Stand Male"; "piece_maypole" -> "Maypole".</summary>
        public static string Prettify(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return "?";

            string s = prefabName;
            if (s.EndsWith("(Clone)"))
                s = s.Substring(0, s.Length - 7);
            if (s.StartsWith("piece_"))
                s = s.Substring(6);
            if (s.StartsWith("item_"))
                s = s.Substring(5);

            var sb = new StringBuilder(s.Length + 8);
            bool newWord = true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '_' || c == '-')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != ' ')
                        sb.Append(' ');
                    newWord = true;
                    continue;
                }
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1]) && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                {
                    sb.Append(' ');
                    newWord = true;
                }
                sb.Append(newWord ? char.ToUpperInvariant(c) : c);
                newWord = false;
            }
            return sb.ToString().Trim();
        }
    }
}
