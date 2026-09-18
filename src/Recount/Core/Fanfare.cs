using System;
using System.Collections.Generic;
using Recount.Model;
using Recount.UI;
using UnityEngine;
using UnityEngine.Audio;

namespace Recount.Core
{
    /// <summary>Banner and sound when the local player beats a personal record.</summary>
    public static class Fanfare
    {
        private const float Cooldown = 3f;

        public static RecordBanner Banner;
        private static float _lastAt = -100f;

        public static void OnRecord(RecordStore.RecordResult r, CombatEvent e)
        {
            // The first hit ever with a weapon is a "record" only in the trivial sense.
            bool weaponBeat = r.NewWeapon && r.PreviousWeapon > 0f;
            bool overallBeat = r.NewOverall && r.PreviousOverall > 0f;
            if (!weaponBeat && !overallBeat)
                return;
            if (e.Amount < PluginConfig.RecordMinDamage.Value)
                return;
            if (Time.time - _lastAt < Cooldown)
                return;
            _lastAt = Time.time;

            string title = overallBeat ? "NEW ALL-TIME RECORD" : "NEW RECORD";
            string body = Names.Exact(e.Amount) + " with " + e.Ability;
            float previous = overallBeat ? r.PreviousOverall : r.PreviousWeapon;
            string sub = "previous best " + Names.Exact(previous)
                         + (e.Has(EventFlags.Backstab) ? "  ·  sneak attack" : "")
                         + (string.IsNullOrEmpty(e.Target) ? "" : "  ·  " + e.Target);

            RecountPlugin.Log.LogInfo(title + ": " + body + " (" + sub + ")");

            if (PluginConfig.RecordBanner.Value && Banner != null)
                Banner.Show(title + " — " + body, sub, PluginConfig.BannerSeconds.Value);

            if (PluginConfig.RecordSound.Value)
                SoundPlayer.Play(PluginConfig.RecordSoundPrefab.Value, PluginConfig.RecordSoundVolume.Value);
        }
    }

    /// <summary>
    /// Plays a vanilla sound prefab locally. The prefab is looked up by name in ZNetScene, its
    /// ZSFX component supplies the clips and the mixer group (so the game's effects volume
    /// applies), and a private AudioSource plays a clip in 2D. Nothing is instantiated, so no
    /// network object is created and nobody else hears it.
    /// </summary>
    public static class SoundPlayer
    {
        public const string LevelUpAlias = "@levelup";

        public sealed class Resolved
        {
            public string Prefab = "";
            public AudioClip[] Clips;
            public AudioMixerGroup Mixer;
            public float Volume = 1f;
            public string Error;

            public bool Ok => Error == null;
        }

        private static AudioSource _source;

        public static Resolved Resolve(string name)
        {
            var r = new Resolved { Prefab = name ?? "" };
            if (string.IsNullOrEmpty(name))
            {
                r.Error = "no prefab configured";
                return r;
            }

            ZSFX sfx = null;
            if (name == LevelUpAlias)
            {
                List<GameObject> prefabs = LevelUpPrefabs();
                foreach (GameObject go in prefabs)
                {
                    sfx = go.GetComponentInChildren<ZSFX>(true);
                    if (sfx != null)
                    {
                        r.Prefab = go.name;
                        break;
                    }
                }
                if (sfx == null)
                {
                    r.Error = prefabs.Count == 0 ? "no local player, or no level-up effects on it" : "level-up effects carry no ZSFX";
                    return r;
                }
            }
            else
            {
                if (ZNetScene.instance == null)
                {
                    r.Error = "no ZNetScene yet";
                    return r;
                }
                GameObject go = ZNetScene.instance.GetPrefab(name);
                if (go == null)
                {
                    r.Error = "prefab not found";
                    return r;
                }
                sfx = go.GetComponentInChildren<ZSFX>(true);
                if (sfx == null)
                {
                    r.Error = "prefab has no ZSFX";
                    return r;
                }
            }

            var clips = new List<AudioClip>();
            if (sfx.m_audioClips != null)
                foreach (AudioClip c in sfx.m_audioClips)
                    if (c != null)
                        clips.Add(c);
            if (clips.Count == 0)
            {
                r.Error = "ZSFX has no clips";
                return r;
            }

            r.Clips = clips.ToArray();
            r.Volume = sfx.m_maxVol > 0f ? sfx.m_maxVol : 1f;
            AudioSource src = sfx.GetComponent<AudioSource>();
            if (src != null)
                r.Mixer = src.outputAudioMixerGroup;
            return r;
        }

        public static List<GameObject> LevelUpPrefabs()
        {
            var list = new List<GameObject>();
            Player p = Player.m_localPlayer;
            if (p == null || p.m_skillLevelupEffects == null || p.m_skillLevelupEffects.m_effectPrefabs == null)
                return list;
            foreach (EffectList.EffectData ed in p.m_skillLevelupEffects.m_effectPrefabs)
                if (ed != null && ed.m_prefab != null)
                    list.Add(ed.m_prefab);
            return list;
        }

        /// <summary>Returns what happened, for the console; a missing prefab is not an error worth a log line per hit.</summary>
        public static string Play(string name, float volume)
        {
            Resolved r;
            try
            {
                r = Resolve(name);
            }
            catch (Exception ex)
            {
                return "could not resolve " + name + ": " + ex.Message;
            }
            if (!r.Ok)
                return "sound skipped (" + r.Prefab + ": " + r.Error + ")";

            try
            {
                if (_source == null)
                {
                    var go = new GameObject("RecountAudio");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _source = go.AddComponent<AudioSource>();
                    _source.playOnAwake = false;
                    _source.spatialBlend = 0f;
                    _source.loop = false;
                }
                _source.outputAudioMixerGroup = r.Mixer;
                AudioClip clip = r.Clips[UnityEngine.Random.Range(0, r.Clips.Length)];
                _source.PlayOneShot(clip, Mathf.Clamp01(volume) * r.Volume);
                return "played " + clip.name + " from " + r.Prefab;
            }
            catch (Exception ex)
            {
                return "could not play " + r.Prefab + ": " + ex.Message;
            }
        }
    }
}
