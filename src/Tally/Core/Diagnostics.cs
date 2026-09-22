using System.Collections.Generic;
using System.Text;
using Tally.Net;
using Tally.UI;
using UnityEngine;

namespace Tally.Core
{
    /// <summary>
    /// One-shot report to the BepInEx log after entering a world. The things that cannot be
    /// checked by reading the assembly: whether the configured sound resolves, what the
    /// level-up effect is actually called, whether the routed RPC is registered.
    /// </summary>
    public static class Diagnostics
    {
        private static bool _reported;
        private static float _reportAt;

        public static void Reset()
        {
            _reported = false;
            _reportAt = Time.time + 3f;   // give ZNetScene and the RPC a moment
        }

        public static void ReportOnce()
        {
            if (_reported || Time.time < _reportAt)
                return;
            _reported = true;
            TallyPlugin.Log.LogInfo(Report());
        }

        public static string Report()
        {
            var sb = new StringBuilder(512);
            sb.Append("diagnostics: game=").Append(Version.CurrentVersion)
              .Append(" uid=").Append(ZNet.instance != null ? ZNet.GetUID().ToString() : "-")
              .Append(" server=").Append(ZNet.instance != null && ZNet.instance.IsServer())
              .Append(" rpc=").Append(EventSync.Registered ? "registered" : "NOT registered")
              .Append(" nonce=").Append(EventSync.Nonce)
              .Append(" font=").Append(Widgets.Font != null ? Widgets.Font.name : "MISSING")
              .Append(" (").Append(Fonts.Names.Count).Append(" loaded)")
              .Append(" records=").Append(RecordStore.Loaded ? System.IO.Path.GetFileName(RecordStore.Path) : "not loaded")
              .Append(' ');

            SoundPlayer.Resolved r = SoundPlayer.Resolve(PluginConfig.RecordSoundPrefab.Value);
            sb.Append("sound=").Append(r.Prefab).Append(r.Ok ? " ok (" + r.Clips.Length + " clip(s), mixer " + (r.Mixer != null ? r.Mixer.name : "none") + ")" : " " + r.Error).Append(' ');

            List<GameObject> levelUp = SoundPlayer.LevelUpPrefabs();
            sb.Append("levelUpEffects=[");
            for (int i = 0; i < levelUp.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(levelUp[i].name).Append(levelUp[i].GetComponentInChildren<ZSFX>(true) != null ? "(sfx)" : "");
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
