using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Recount.Model;

namespace Recount.Core
{
    /// <summary>
    /// The local player's biggest hits, on disk, per character and per world. Keyed by both so
    /// a fresh character starts fresh and a modded world does not pollute a vanilla one.
    /// Lives in BepInEx/config/Recount/, so it follows the profile.
    /// </summary>
    public static class RecordStore
    {
        public const int TopPerWeapon = 10;

        public sealed class Record
        {
            public float Damage;
            public string Weapon = "";
            public string Target = "";
            public string DamageType = "";
            public bool Backstab;
            public string Skill = "";
            public float SkillLevel;
            public long Timestamp;
            public string World = "";

            public string When()
            {
                return DateTimeOffset.FromUnixTimeSeconds(Timestamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
        }

        public sealed class RecordResult
        {
            public bool NewWeapon;
            public bool NewOverall;
            public float PreviousWeapon;
            public float PreviousOverall;
            public Record Record;
        }

        private static readonly Dictionary<string, List<Record>> _weapons = new Dictionary<string, List<Record>>();
        private static Record _overall;
        private static string _path;
        private static string _loadedKey;
        private static bool _dirty;

        public static bool Loaded => _path != null;
        public static string Path => _path;
        public static Record Overall => _overall;
        public static IEnumerable<KeyValuePair<string, List<Record>>> Weapons => _weapons;

        public static List<Record> ForWeapon(string weapon)
        {
            List<Record> list;
            return _weapons.TryGetValue(weapon, out list) ? list : null;
        }

        /// <summary>Load the file for the current character and world, once both are known.</summary>
        public static void EnsureLoaded()
        {
            if (!PluginConfig.RecordsEnabled.Value)
                return;
            if (Game.instance == null || ZNet.World == null)
                return;

            PlayerProfile profile = Game.instance.GetPlayerProfile();
            World world = ZNet.World;
            if (profile == null)
                return;

            long worldId = world.m_uid != 0 ? world.m_uid : world.m_seed;
            string key = profile.GetPlayerID() + "|" + worldId;
            if (_path != null && _loadedKey == key)
                return;

            Unload();
            _loadedKey = key;
            _path = PathFor(profile, world, worldId);
            Load(world.m_name);
        }

        public static void Unload()
        {
            if (_dirty)
                Save();
            _weapons.Clear();
            _overall = null;
            _path = null;
            _loadedKey = null;
            _dirty = false;
        }

        public static void Forget()
        {
            _weapons.Clear();
            _overall = null;
            _dirty = true;
            Save();
        }

        public static void SaveIfDirty()
        {
            if (_dirty)
                Save();
        }

        /// <summary>Consider a hit for the records. Returns null when it is not one.</summary>
        public static RecordResult Offer(CombatEvent e)
        {
            EnsureLoaded();
            if (_path == null || e.Amount <= 0f)
                return null;

            string weapon = string.IsNullOrEmpty(e.Ability) ? Names.Unknown : e.Ability;
            List<Record> list;
            if (!_weapons.TryGetValue(weapon, out list))
            {
                list = new List<Record>();
                _weapons[weapon] = list;
            }

            float prevWeapon = list.Count > 0 ? list[0].Damage : 0f;
            bool topTen = list.Count < TopPerWeapon || e.Amount > list[list.Count - 1].Damage;
            bool newWeapon = e.Amount > prevWeapon;
            float prevOverall = _overall != null ? _overall.Damage : 0f;
            bool newOverall = e.Amount > prevOverall;

            if (!topTen && !newOverall)
                return null;

            var r = new Record
            {
                Damage = e.Amount,
                Weapon = weapon,
                Target = e.Target ?? "",
                DamageType = Names.DamageType(e.DamageType),
                Backstab = e.Has(EventFlags.Backstab),
                Skill = e.Skill == Skills.SkillType.None ? "" : e.Skill.ToString(),
                SkillLevel = e.SkillLevel,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                World = ZNet.World != null ? ZNet.World.m_name : ""
            };

            if (topTen)
            {
                int i = 0;
                while (i < list.Count && list[i].Damage >= r.Damage)
                    i++;
                list.Insert(i, r);
                while (list.Count > TopPerWeapon)
                    list.RemoveAt(list.Count - 1);
            }
            if (newOverall)
                _overall = r;

            _dirty = true;

            return new RecordResult
            {
                NewWeapon = newWeapon,
                NewOverall = newOverall,
                PreviousWeapon = prevWeapon,
                PreviousOverall = prevOverall,
                Record = r
            };
        }

        // ---- disk ----------------------------------------------------------------------

        private static string PathFor(PlayerProfile profile, World world, long worldId)
        {
            string dir = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "Recount");
            string file = "records-" + Safe(profile.GetName()) + "-" + profile.GetPlayerID().ToString(CultureInfo.InvariantCulture)
                          + "-" + Safe(world.m_name) + "-" + worldId.ToString(CultureInfo.InvariantCulture) + ".json";
            return System.IO.Path.Combine(dir, file);
        }

        private static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.Length == 0 ? "x" : sb.ToString();
        }

        private static void Load(string worldName)
        {
            _weapons.Clear();
            _overall = null;
            _dirty = false;

            if (!File.Exists(_path))
            {
                RecountPlugin.Log.LogInfo("no records yet at " + _path);
                return;
            }

            try
            {
                var root = Json.Parse(File.ReadAllText(_path)) as Dictionary<string, object>;
                if (root == null)
                    throw new FormatException("root is not an object");

                Dictionary<string, object> overall = Json.Obj(root, "overall");
                if (overall != null)
                    _overall = FromJson(overall);

                Dictionary<string, object> weapons = Json.Obj(root, "weapons");
                if (weapons != null)
                {
                    foreach (KeyValuePair<string, object> kv in weapons)
                    {
                        var arr = kv.Value as List<object>;
                        if (arr == null) continue;
                        var list = new List<Record>();
                        foreach (object o in arr)
                        {
                            var d = o as Dictionary<string, object>;
                            if (d != null)
                                list.Add(FromJson(d));
                        }
                        list.Sort((a, b) => b.Damage.CompareTo(a.Damage));
                        _weapons[kv.Key] = list;
                    }
                }

                RecountPlugin.Log.LogInfo(string.Format("loaded records for {0} weapon(s) from {1}", _weapons.Count, System.IO.Path.GetFileName(_path)));
            }
            catch (Exception ex)
            {
                RecountPlugin.Log.LogWarning("could not read records " + _path + ": " + ex.Message);
                _weapons.Clear();
                _overall = null;
            }
        }

        private static Record FromJson(Dictionary<string, object> d)
        {
            return new Record
            {
                Damage = (float)Json.Num(d, "damage"),
                Weapon = Json.Str(d, "weapon"),
                Target = Json.Str(d, "target"),
                DamageType = Json.Str(d, "damageType"),
                Backstab = Json.Bool(d, "backstab"),
                Skill = Json.Str(d, "skill"),
                SkillLevel = (float)Json.Num(d, "skillLevel"),
                Timestamp = (long)Json.Num(d, "timestamp"),
                World = Json.Str(d, "world")
            };
        }

        private static Dictionary<string, object> ToJson(Record r)
        {
            return new Dictionary<string, object>
            {
                { "damage", r.Damage },
                { "weapon", r.Weapon },
                { "target", r.Target },
                { "damageType", r.DamageType },
                { "backstab", r.Backstab },
                { "skill", r.Skill },
                { "skillLevel", r.SkillLevel },
                { "timestamp", r.Timestamp },
                { "world", r.World }
            };
        }

        private static void Save()
        {
            if (_path == null)
                return;
            try
            {
                var weapons = new Dictionary<string, object>();
                foreach (KeyValuePair<string, List<Record>> kv in _weapons)
                {
                    var arr = new List<object>();
                    foreach (Record r in kv.Value)
                        arr.Add(ToJson(r));
                    weapons[kv.Key] = arr;
                }

                var root = new Dictionary<string, object>
                {
                    { "version", 1 },
                    { "character", Game.instance != null ? Game.instance.GetPlayerProfile().GetName() : "" },
                    { "world", ZNet.World != null ? ZNet.World.m_name : "" },
                    { "overall", _overall != null ? ToJson(_overall) : null },
                    { "weapons", weapons }
                };

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));

                // Write beside, then rename: a crash mid-write must not lose the file.
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, Json.Write(root));
                if (File.Exists(_path))
                    File.Delete(_path);
                File.Move(tmp, _path);
                _dirty = false;

                if (PluginConfig.Verbose.Value)
                    RecountPlugin.Log.LogDebug("saved records to " + System.IO.Path.GetFileName(_path));
            }
            catch (Exception ex)
            {
                RecountPlugin.Log.LogWarning("could not write records " + _path + ": " + ex.Message);
            }
        }
    }
}
