using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using Tally.Core;
using Tally.Net;
using Tally.UI;
using UnityEngine;

namespace Tally
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim.x86_64")]
    public sealed class TallyPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jumpingmushroom.tally";
        public const string PluginName = "Tally";
        public const string PluginVersion = "0.1.1";

        internal static ManualLogSource Log;
        internal static MeterWindow Window;

        private readonly RecordBanner _banner = new RecordBanner();
        private Harmony _harmony;

        private bool _guiReady;
        private bool _mouseMode;
        private ZNet _lastNet;
        private bool _hadWorld;
        private bool _arrivalPending;
        private float _nextHousekeeping;

        private void Awake()
        {
            Log = Logger;

            PluginConfig.Bind(base.Config);
            Window = new MeterWindow(Recorder.Meter);
            ConsoleCommands.Register();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(TallyPlugin).Assembly);

            GUIManager.OnCustomGUIAvailable += OnGuiAvailable;
            PluginConfig.LayoutChanged += OnLayoutChanged;
            PluginConfig.FontChanged += OnFontChanged;

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            GUIManager.OnCustomGUIAvailable -= OnGuiAvailable;
            PluginConfig.LayoutChanged -= OnLayoutChanged;
            PluginConfig.FontChanged -= OnFontChanged;
            ReleaseMouse();
            Window.Destroy();
            _banner.Destroy();
            Fanfare.Banner = null;
            RecordStore.Unload();
            if (_harmony != null)
                _harmony.UnpatchSelf();
        }

        private void OnGuiAvailable()
        {
            if (GUIManager.IsHeadless())
                return;

            // CustomGUIFront is rebuilt when moving between world and menu, which destroys our
            // objects. Recreate them, but never let the window reappear at the main menu.
            if (Player.m_localPlayer == null)
                Window.SetOpen(false);

            // Fonts are loaded by now; offer the real list in the config UI.
            Fonts.Refresh();
            PluginConfig.RebindFontChoices(Fonts.Names);

            Window.Create();
            _banner.Create(GUIManager.CustomGUIFront.transform);
            Fanfare.Banner = _banner;
            _guiReady = Window.Created;

            if (PluginConfig.Verbose.Value)
                Log.LogDebug("window created: " + _guiReady);
        }

        private void OnLayoutChanged()
        {
            Window.ApplyLayout();
        }

        /// <summary>Every label carries its font, so the cheapest correct thing is a rebuild.</summary>
        private void OnFontChanged()
        {
            if (GUIManager.CustomGUIFront == null)
                return;
            Window.Destroy();
            Window.Create();
            _banner.Destroy();
            _banner.Create(GUIManager.CustomGUIFront.transform);
            Fanfare.Banner = _banner;
            _guiReady = Window.Created;
        }

        /// <summary>Logged out or returned to the menu: close up and forget the session.</summary>
        private void WorldLeft()
        {
            ReleaseMouse();
            Window.SetOpen(false);
            Recorder.Clear();
            EventSync.Clear();
            RecordStore.Unload();
            Diagnostics.Reset();
            _arrivalPending = false;
        }

        /// <summary>A new connection: a fresh session. The player object comes a little later.</summary>
        private void WorldEntered()
        {
            Recorder.Clear();
            EventSync.Clear();
            _arrivalPending = true;
        }

        /// <summary>The local player exists for the first time in this session.</summary>
        private void LocalPlayerArrived()
        {
            RecordStore.EnsureLoaded();
            Diagnostics.Reset();
            if (PluginConfig.ShowOnLogin.Value)
                Window.SetOpen(true);
        }

        private void Update()
        {
            // A session is a ZNet: one is created per connection and destroyed on logout. The
            // player object is the wrong thing to key on, because dying destroys it and a new
            // one appears after the respawn delay; the fight you died in must survive that, and
            // so must the table of peers running the mod.
            ZNet net = ZNet.instance;
            bool hasWorld = net != null;
            if (!hasWorld && _hadWorld)
                WorldLeft();
            else if (hasWorld && (!_hadWorld || !ReferenceEquals(net, _lastNet)))
                WorldEntered();
            _hadWorld = hasWorld;
            _lastNet = net;

            float now = Time.time;
            EventSync.Update(now);

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                ReleaseMouse();
                return;
            }

            if (_arrivalPending)
            {
                _arrivalPending = false;
                LocalPlayerArrived();
            }

            Recorder.Update(now);
            _banner.Update();

            if (!_guiReady)
                return;

            bool typing = Typing();
            if (!typing)
            {
                if (Keys.Down(PluginConfig.ToggleKey.Value))
                    Window.Toggle();
                if (Keys.Down(PluginConfig.ResetKey.Value))
                    Window.Reset();
                if (Keys.Down(PluginConfig.CycleModeKey.Value))
                    Window.CycleMode();
                if (Keys.Down(PluginConfig.CycleSegmentKey.Value))
                    Window.CycleSegment();
            }

            // Holding the mouse key frees the cursor and blocks the player's own input while
            // it is held, so a click on a row is a click on a row and not an attack.
            bool wantMouse = Window.IsOpen && !typing && Keys.Held(PluginConfig.MouseKey.Value);
            if (wantMouse != _mouseMode)
            {
                _mouseMode = wantMouse;
                GUIManager.BlockInput(wantMouse);
            }

            Window.SetHidden(Hud.IsUserHidden());
            Window.Render();
            Diagnostics.ReportOnce();

            if (now >= _nextHousekeeping)
            {
                _nextHousekeeping = now + 10f;
                RecordStore.EnsureLoaded();
                RecordStore.SaveIfDirty();
            }
        }

        private void ReleaseMouse()
        {
            if (!_mouseMode)
                return;
            _mouseMode = false;
            GUIManager.BlockInput(false);
        }

        /// <summary>
        /// Never react to keys while the player is typing. Jotunn's input block reports
        /// TextInput as visible while it is active, so that check is skipped in mouse mode or
        /// the mode would cancel itself.
        /// </summary>
        private bool Typing()
        {
            if (Console.IsVisible()) return true;
            if (Menu.IsVisible()) return true;
            if (!_mouseMode && TextInput.IsVisible()) return true;
            if (Minimap.InTextInput()) return true;
            if (Chat.instance != null && Chat.instance.HasFocus()) return true;
            return false;
        }
    }
}
