using System;
using System.Collections.Generic;
using Recount.Core;
using Recount.Model;
using UnityEngine;

namespace Recount.Net
{
    /// <summary>
    /// Client-to-client sharing over the game's routed RPC. Nothing runs on the server: a
    /// dedicated server's ZRoutedRpc.RPC_RoutedRPC forwards any routed call addressed to
    /// Everybody to all other ready peers without looking at the method, and a client without
    /// the mod finds no handler for the hash and drops the call silently.
    ///
    /// Two methods. "Recount_Hello" announces presence, so an attacker knows whether the owner
    /// of what it hits will report real damage. "Recount_Events" carries a batch of events as a
    /// compact binary ZPackage with a per-packet string table, flushed on a timer rather than
    /// per hit. Every sender numbers its events from a per-session nonce, and receivers keep
    /// the highest number seen per sender, so a replayed or duplicated packet counts nothing.
    /// </summary>
    public static class EventSync
    {
        private const byte Protocol = 1;
        private const string EventsRpc = "Recount_Events";
        private const string HelloRpc = "Recount_Hello";
        private const float HelloInterval = 30f;
        private const float PeerTtl = 90f;
        private const int MaxEventsPerPacket = 80;

        public sealed class PeerState
        {
            public long Uid;
            public uint Nonce;
            public uint LastSeq;
            public float LastSeen;
            public string Name = "";
            public string Version = "";
        }

        private static readonly uint _nonce = (uint)new System.Random().Next(1, int.MaxValue);
        private static uint _nextSeq = 1;
        private static ZRoutedRpc _registeredOn;
        private static readonly List<CombatEvent> _outgoing = new List<CombatEvent>();
        private static readonly Dictionary<long, PeerState> _peers = new Dictionary<long, PeerState>();
        private static float _nextFlush;
        private static float _nextHello;

        public static int PacketsSent, PacketsReceived, EventsSent, EventsReceived, EventsDropped;
        public static bool Registered => _registeredOn != null;
        public static uint Nonce => _nonce;

        public static void Update(float now)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null)
            {
                _registeredOn = null;
                return;
            }
            if (!ReferenceEquals(rpc, _registeredOn))
                Register(rpc);

            if (Player.m_localPlayer == null)
                return;

            if (now >= _nextHello)
            {
                _nextHello = now + HelloInterval;
                SendHello(ZRoutedRpc.Everybody);
            }

            if (now >= _nextFlush)
            {
                _nextFlush = now + Mathf.Max(0.1f, PluginConfig.FlushInterval.Value);
                Flush();
            }
        }

        /// <summary>A new ZRoutedRpc is built on every connection; handlers go with it.</summary>
        private static void Register(ZRoutedRpc rpc)
        {
            try
            {
                rpc.Register<ZPackage>(EventsRpc, OnEvents);
                rpc.Register<ZPackage>(HelloRpc, OnHello);
            }
            catch (ArgumentException)
            {
                // Already registered on this instance (a reload of the plugin, say).
            }
            _registeredOn = rpc;
            _peers.Clear();
            _outgoing.Clear();
            _nextHello = 0f;
            if (PluginConfig.Verbose.Value)
                RecountPlugin.Log.LogDebug("routed RPC handlers registered, nonce " + _nonce);
        }

        public static void Clear()
        {
            _outgoing.Clear();
            _peers.Clear();
            _nextHello = 0f;
        }

        // ---- presence ------------------------------------------------------------------

        /// <summary>
        /// False whenever sharing is off: with nothing accepted from peers, the attacker's own
        /// estimate is the only number there will ever be.
        /// </summary>
        public static bool PeerHasMod(long uid)
        {
            if (!PluginConfig.ShareEnabled.Value)
                return false;
            PeerState p;
            return _peers.TryGetValue(uid, out p) && Time.time - p.LastSeen < PeerTtl;
        }

        public static List<PeerState> Peers()
        {
            var list = new List<PeerState>();
            float now = Time.time;
            foreach (PeerState p in _peers.Values)
                if (now - p.LastSeen < PeerTtl)
                    list.Add(p);
            return list;
        }

        private static void SendHello(long target)
        {
            if (!PluginConfig.ShareEnabled.Value || ZRoutedRpc.instance == null)
                return;
            var pkg = new ZPackage();
            pkg.Write(Protocol);
            pkg.Write(_nonce);
            pkg.Write(RecountPlugin.PluginVersion);
            pkg.Write(Recorder.LocalPlayerName ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(target, HelloRpc, pkg);
        }

        private static void OnHello(long sender, ZPackage pkg)
        {
            try
            {
                if (sender == ZNet.GetUID())
                    return;
                byte protocol = pkg.ReadByte();
                uint nonce = pkg.ReadUInt();
                string version = pkg.ReadString();
                string name = pkg.ReadString();

                PeerState p;
                bool isNew = !_peers.TryGetValue(sender, out p);
                if (isNew)
                {
                    p = new PeerState { Uid = sender };
                    _peers[sender] = p;
                }
                if (p.Nonce != nonce)
                {
                    p.Nonce = nonce;
                    p.LastSeq = 0;
                }
                p.LastSeen = Time.time;
                p.Name = name;
                p.Version = version + (protocol != Protocol ? " (protocol " + protocol + ")" : "");

                if (isNew)
                {
                    if (PluginConfig.Verbose.Value)
                        RecountPlugin.Log.LogDebug("peer with Recount: " + name + " (" + sender + ", v" + version + ")");
                    // Answer directly so a late joiner learns about us at once rather than at
                    // the next periodic hello.
                    SendHello(sender);
                }
            }
            catch (Exception ex)
            {
                RecountPlugin.Log.LogWarning("bad hello from " + sender + ": " + ex.Message);
            }
        }

        // ---- events --------------------------------------------------------------------

        public static void Queue(CombatEvent e)
        {
            _outgoing.Add(e);
        }

        private static bool AnyPeerWithMod()
        {
            float now = Time.time;
            foreach (PeerState p in _peers.Values)
                if (now - p.LastSeen < PeerTtl)
                    return true;
            return false;
        }

        private static void Flush()
        {
            if (_outgoing.Count == 0)
                return;
            if (!PluginConfig.ShareEnabled.Value || !AnyPeerWithMod() || ZRoutedRpc.instance == null)
            {
                _outgoing.Clear();
                return;
            }

            int offset = 0;
            while (offset < _outgoing.Count)
            {
                int count = Math.Min(MaxEventsPerPacket, _outgoing.Count - offset);
                ZPackage pkg = Encode(_outgoing, offset, count);
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, EventsRpc, pkg);
                PacketsSent++;
                EventsSent += count;
                if (PluginConfig.Verbose.Value)
                    RecountPlugin.Log.LogDebug("sent " + count + " event(s), " + pkg.Size() + " bytes");
                offset += count;
            }
            _outgoing.Clear();
        }

        private static ZPackage Encode(List<CombatEvent> events, int offset, int count)
        {
            var strings = new List<string>();
            var index = new Dictionary<string, byte>();
            for (int i = 0; i < count; i++)
            {
                CombatEvent e = events[offset + i];
                Intern(strings, index, e.Player);
                Intern(strings, index, e.Ability);
                Intern(strings, index, e.Target);
            }

            var pkg = new ZPackage();
            pkg.Write(Protocol);
            pkg.Write(_nonce);
            pkg.Write(_nextSeq);
            pkg.Write((byte)count);
            pkg.Write((byte)strings.Count);
            for (int i = 0; i < strings.Count; i++)
                pkg.Write(strings[i]);

            for (int i = 0; i < count; i++)
            {
                CombatEvent e = events[offset + i];
                pkg.Write((byte)e.Kind);
                pkg.Write((byte)(e.Flags & ~EventFlags.Remote));
                pkg.Write(index[e.Player ?? ""]);
                pkg.Write(index[e.Ability ?? ""]);
                pkg.Write(index[e.Target ?? ""]);
                pkg.Write(e.Amount);
                if (e.Kind == EventKind.Heal)
                    pkg.Write(e.Extra);
                pkg.Write((ushort)e.DamageType);
                pkg.Write(e.Position);
            }
            _nextSeq += (uint)count;
            return pkg;
        }

        private static void Intern(List<string> strings, Dictionary<string, byte> index, string s)
        {
            s = s ?? "";
            if (index.ContainsKey(s))
                return;
            index[s] = (byte)strings.Count;
            strings.Add(s);
        }

        private static void OnEvents(long sender, ZPackage pkg)
        {
            try
            {
                if (sender == ZNet.GetUID() || !PluginConfig.ShareEnabled.Value)
                    return;

                byte protocol = pkg.ReadByte();
                if (protocol != Protocol)
                    return;
                uint nonce = pkg.ReadUInt();
                uint firstSeq = pkg.ReadUInt();
                int count = pkg.ReadByte();
                int stringCount = pkg.ReadByte();
                var strings = new string[stringCount];
                for (int i = 0; i < stringCount; i++)
                    strings[i] = pkg.ReadString();

                PeerState peer;
                if (!_peers.TryGetValue(sender, out peer))
                {
                    peer = new PeerState { Uid = sender };
                    _peers[sender] = peer;
                }
                if (peer.Nonce != nonce)
                {
                    peer.Nonce = nonce;
                    peer.LastSeq = 0;
                }
                peer.LastSeen = Time.time;
                PacketsReceived++;

                Player local = Player.m_localPlayer;
                float range = PluginConfig.ShareRange.Value;
                int accepted = 0;

                for (int i = 0; i < count; i++)
                {
                    var e = new CombatEvent();
                    e.Kind = (EventKind)pkg.ReadByte();
                    e.Flags = (EventFlags)pkg.ReadByte();
                    e.Player = strings[pkg.ReadByte()];
                    e.Ability = strings[pkg.ReadByte()];
                    e.Target = strings[pkg.ReadByte()];
                    e.Amount = pkg.ReadSingle();
                    if (e.Kind == EventKind.Heal)
                        e.Extra = pkg.ReadSingle();
                    e.DamageType = (HitData.DamageType)pkg.ReadUShort();
                    e.Position = pkg.ReadVector3();

                    uint seq = firstSeq + (uint)i;
                    if (seq <= peer.LastSeq)
                    {
                        EventsDropped++;
                        continue;
                    }
                    peer.LastSeq = seq;

                    if (local != null && Vector3.Distance(local.transform.position, e.Position) > range)
                    {
                        EventsDropped++;
                        continue;
                    }
                    if (e.Has(EventFlags.NonCharacter) && !PluginConfig.IncludeStructures.Value)
                    {
                        EventsDropped++;
                        continue;
                    }

                    Recorder.RecordRemote(e);
                    accepted++;
                }
                EventsReceived += accepted;
            }
            catch (Exception ex)
            {
                RecountPlugin.Log.LogWarning("bad event packet from " + sender + ": " + ex.Message);
            }
        }
    }
}
