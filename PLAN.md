# Tally — Technical Plan

**Goal:** a client-side Valheim combat meter in the mould of the WoW Recount/Skada addons:
damage done, DPS, damage taken, healing, max hit and all-time personal records, for every player
nearby, in a draggable window of coloured bars.

**Target build:** Valheim **1.0.14** (`Version.CurrentVersion = new GameVersion(1, 0, 14)`,
assembly dated 2026-09-17 on the rig). Analysis source: `assembly_valheim.dll` copied from the
rig's `valheim_Data/Managed` on 2026-09-18 and decompiled with ILSpy 9.1. The copy in
`../Comfortaudit/lib` was 1.0.12; the damage, heal, status-effect and routed-RPC code cited
below is byte-for-byte identical between the two, but `lib/` now holds the 1.0.14 set. Every
type and member cited is present in that assembly.

---

## 1. What the game actually does

### 1.1 Damage is computed on the target's owner, and only there

`Character.Damage(HitData)` does nothing but `m_nview.InvokeRPC("RPC_Damage", hit)`. Routed
RPCs to the owner run synchronously on the owner and travel over the network otherwise, so:

- `Character.RPC_Damage(long sender, HitData hit)` returns at once unless `m_nview.IsOwner()`.
  On the owner it applies, in order: difficulty scaling for creature attackers, the sneak
  multiplier (`hit.m_backstabBonus`, stamping `m_backstabTime = Time.time`), the ×2 stagger
  crit, blocking, then `hit.ApplyResistance` and `hit.ApplyArmor`, then **strips fire, spirit and
  poison out of the hit** and calls `ApplyDamage(hit, ...)` with what is left, and finally hands
  the stripped amounts to `AddFireDamage` / `AddSpiritDamage` / `AddPoisonDamage`.
- `Character.ApplyDamage(HitData, bool, bool, DamageModifier)` applies the last multipliers
  (`GetDifficultyDamageScaleEnemy`, `Game.m_playerDamageRate` / `m_localDamgeTakenRate`) **in
  place on the hit**, returns if the result is ≤ 0.1, and subtracts `hit.GetTotalDamage()` from
  health. `SetHealth` clamps at zero, so the health delta hides overkill; the hit itself does not.
- `SE_Burning.UpdateStatusEffect` and `SE_Poison.UpdateStatusEffect` call `ApplyDamage`
  **directly**, each tick, with a fresh `HitData` whose `m_attacker` is `ZDOID.None` and whose
  `m_hitType` is `Burning` / `Poisoned`. Spirit ticks ride inside `SE_Burning` with
  `m_damage.m_spirit` set.
- **Neither effect remembers who applied it.** `StatusEffect.SetAttacker` is a no-op; only
  `SE_Harpooned` overrides it. `RPC_Damage` does call `statusEffect.SetAttacker(attacker)`, but
  for burning and poison that call discards the attacker.

So `ApplyDamage` on an owned character is the single choke point for every real hit, direct or
tick, and the mod's DoT attribution must be its own memory, filled from `RPC_Damage` before the
elemental damage is stripped (§2.2).

`HitData` carries what attribution needs: `m_attacker` (ZDOID; `GetAttacker()` resolves it via
`ZNetScene.FindInstance`), `m_skill`, `m_skillLevel` (the attacker's level at attack time, set
by `Attack` and `Projectile`), `m_backstabBonus`, `m_ranged`, `m_hitType`, `m_itemLevel`, and
`m_damage.GetMajorityDamageType()`.

The number shown as floating damage text is `totalDamage` *before* the final difficulty
multiplier in `ApplyDamage`; the meter records the amount after it, which is what came off the
health bar. They differ only with several players nearby or a world level set.

### 1.2 Healing

`Character.Heal(hp)` on the owner calls `RPC_Heal(0, hp, showText)` directly; on the owner it
clamps to max health, so effective healing is `min(health + hp, max) − health` and the rest is
overheal. Callers: `Player.UpdateFood` (every 10 s, the sum of food regen times
`ModifyHealthRegen`), `SE_Stats.UpdateStatusEffect` (per-tick and over-time health), attack and
projectile lifesteal. Players are owned by their own client, so healing is only ever observed
for the local player; other players' healing arrives over the wire.

### 1.3 Things that are not characters

`WearNTear`, `Destructible`, `TreeBase`, `TreeLog`, `MineRock` (`RPC_Hit`) and `MineRock5` each
have an owner-only `RPC_Damage`-style method that applies resistance to the hit in place and
then checks `hit.CheckToolTier(m_minToolTier, ...)`. A postfix sees the mitigated number and
can repeat the tier check. Off by default (`Tracking.IncludeStructures`).

### 1.4 The routed RPC relays anything

`ZRoutedRpc.RPC_RoutedRPC` (the handler every peer registers for `"RoutedRPC"`):

```csharp
if (data.m_targetPeerID == m_id || data.m_targetPeerID == 0L) HandleRoutedRPC(data);
if (m_server && data.m_targetPeerID != m_id) RouteRPC(data);
```

`RouteRPC` on the server forwards a call addressed to `Everybody` (0) to every ready peer
except the sender, by re-serialising the `RoutedRPCData`; it never looks at `m_methodHash`.
`HandleRoutedRPC` looks the hash up in `m_functions` and does nothing if it is missing. **A
vanilla dedicated server therefore relays a mod-specific method name, and a vanilla client
ignores it.** `InvokeRoutedRPC(Everybody, ...)` also invokes the local handler with
`sender == ZNet.GetUID()`, which the mod must skip.

Peer identity: `ZRoutedRpc.m_id` is `ZDOMan.GetSessionID()`, which is also `ZNet.GetUID()`,
also `ZDO.GetOwner()` for anything that client owns, and also `ZDOID.UserID` of anything it
created (a player's own character included). One number ties the sender of a packet to the
owner of a creature. A new `ZRoutedRpc` is constructed per connection, so handlers must be
re-registered when `ZRoutedRpc.instance` changes.

`ZNet.GetPlayerList()` carries positions only for players who share theirs on the map, so the
range limit must be applied by the receiver from the event's own position, not by the sender.

### 1.5 Weapons

`Humanoid.GetCurrentWeapon()` reads the owner's inventory, which is not synced. For other
players, `VisEquipment.m_currentRightItemHash` / `m_currentLeftItemHash` are the item hashes
the game synced for drawing the character; `ObjectDB.GetItemPrefab(hash)` gives the `ItemDrop`
and its `m_shared.m_name` token. Projectiles carry `m_skill` from the weapon that fired them.

### 1.6 Sounds

`ZNetScene.GetPrefab(string)` resolves any registered prefab. A sound prefab has a `ZSFX`
component (on itself or a child) with `m_audioClips`, `m_minVol`/`m_maxVol`, and an
`AudioSource` whose `outputAudioMixerGroup` is the game's effects bus. Playing a clip through a
private `AudioSource` set to that group makes the game's SFX slider apply, creates no network
object, and is heard only locally. The skill level-up sound is not a named ZNetScene prefab in
the Jotunn list; it is whatever `Player.m_skillLevelupEffects` references, readable at runtime.

---

## 2. Design

### 2.1 Events (`Model/CombatEvent`)

One record per thing that happened, already attributed: `Kind` (Damage / Taken / Heal),
`Player` (the row), `Ability` (the drill-in key: weapon, attacker, heal source), `Target`,
`Amount`, `Extra` (overheal), majority `DamageType`, `Position`, and flags: `Approximate`,
`Backstab`, `Dot`, `Ranged`, `NonCharacter`, `Remote`. Segments aggregate events and never keep
them.

### 2.2 Observation (`Patches/CombatPatches`, `Core/Attribution`)

| Hook | Why |
|---|---|
| `Character.RPC_Damage` prefix, owner only | Remember the attacker of any hit carrying fire, spirit or poison, per victim ZDOID and kind (`DotTracker`), before the elements are stripped. |
| `Character.ApplyDamage` prefix + postfix, owner only | Prefix mirrors the method's early returns; postfix reads `hit.GetTotalDamage()` (now mitigated and scaled) and `m_backstabTime == Time.time` for the sneak flag. A hit with an attacker that is a player becomes a **Damage** event; a hit on a player becomes a **Taken** event; a DoT tick with no attacker is credited from `DotTracker` under the ability "Burning" / "Spirit" / "Poison". |
| `Character.RPC_Heal` prefix, owner and player only | Effective heal and overheal, with the source from `HealContext`, which prefix/postfix pairs on `Player.UpdateFood` ("Food") and `SE_Stats.UpdateStatusEffect` (the effect's name) set around their calls. |
| `Character.Damage` prefix | The attacker's side. If the target is not owned here, the attacker is the local player and the owner is **not** known to run the mod (§2.3), record `hit.GetTotalDamage()` pre-mitigation, flagged `Approximate`. |
| The six destructible RPCs, postfix | Damage to pieces, trees, rocks; gated by config at runtime. |

Every patch body is wrapped; a fault logs at most five times and never reaches the game.

### 2.3 Sharing (`Net/EventSync`)

Two routed methods, registered on every new `ZRoutedRpc`:

- `Tally_Hello` `{protocol, nonce, version, name}` — sent to Everybody on entering a world and
  every 30 s, and directly to any peer whose first hello arrives, so a late joiner is known
  within a round trip. A peer is "running the mod" for 90 s after its last hello. This is what
  lets `Character.Damage` decide whether the owner will report a hit or the attacker must guess.
- `Tally_Events` — a batch, flushed every `FlushInterval` (0.5 s) only when at least one peer
  runs the mod: `protocol, nonce, firstSeq, count, stringTable[], events[]`, each event
  `kind, flags, playerIdx, abilityIdx, targetIdx, amount(, overheal), damageType, position`.
  Strings are interned per packet; at most 80 events per packet keep indices in a byte.

Receivers key on `(sender uid, nonce)` and keep the highest sequence number seen, so a
duplicated or replayed packet counts nothing, and a sender that reconnects (new nonce) starts
clean. Events further than `Sharing.Range` from the local player are dropped on receipt. Own
broadcasts (`sender == ZNet.GetUID()`) are ignored.

Each hit is computed on exactly one client (the target's owner), so as long as every hit is
reported once by that client there is no double counting between sources. The one window is a
peer whose hello has not arrived yet: the attacker may record an approximate hit that the owner
then also reports. It closes within a round trip of the peer's first hello.

### 2.4 Segments (`Model/Segment`, `Model/Meter`)

*Overall* since login or reset; *Current* fight; the last `HistorySize` fights. A fight starts
on the first Damage or Taken event and closes after `FightTimeout` seconds without one; heals
outside a fight go to Overall only. A closed fight is labelled by the target that took the most
damage and its duration. DPS is Recount's, not Skada's: per player, the gaps between that
player's hits, each capped at `ActiveGap`, divided into their damage.

### 2.5 Window (`UI/MeterWindow`, `UI/MeterView`)

Flat uGUI under Jotunn's `CustomGUIFront`: a translucent dark plate with a one-pixel border,
a title bar (mode · segment, back arrow when drilled in, Reset / Seg / Mode / Rep / x), and a
list of bars. Each bar's width is its value over the top value; colour is a stable hash of the
name. Rank and name left, value and share right. Hover shows a tooltip; click drills into a
player (weapons, sources, top hits) or a weapon (its top ten records). Mouse wheel pages.
Dragging the title bar and the corner grip write position and size back to the config once the
drag settles. `MouseKey` (Left Alt) held frees the cursor through `GUIManager.BlockInput`,
which also stops attacks and camera input while held; the map and inventory free it anyway.

### 2.6 Records (`Core/RecordStore`, `Core/Fanfare`)

`BepInEx/config/Tally/records-<character>-<id>-<world>-<uid>.json`, hand-written JSON
(`Core/Json`): the all-time best hit and a top ten per weapon, each with damage, weapon, target,
damage type, sneak flag, skill and level, timestamp and world. Only real (non-approximate,
non-DoT, non-structure) hits by the local player qualify. Beating a weapon's best or the
all-time best above `MinDamage` shows the banner and plays the sound, with a 3 s cooldown and
no fanfare for a weapon's very first hit.

**Sound choice.** The vanilla prefab list has no dedicated skill level-up sound; the level-up
effect is an `EffectList` on the player, reachable at runtime as `@levelup` (the mod reads
`Player.m_skillLevelupEffects` and uses the first prefab with a `ZSFX`; `tally sfx` logs the
real names). Verified names from the list that read as celebratory: **`fx_GP_Activation`** (the
guardian power activation, chosen as the default: unmistakably "something good happened" and
short), `sfx_secretfound` (the discovery chime), `sfx_lootspawn`. The boss-defeat sting is
music, not a prefab, and every `sfx_*_death` is a creature's own death cry. A prefab that is
missing or has no clip skips the sound and logs why in `tally stats`.

---

### 2.7 Session boundaries

A session is a `ZNet` instance, created per connection and destroyed on logout. The player
object is the wrong thing to key on: `Game._RequestRespawn` destroys it on death and a new one
appears after the respawn delay, and the fight you died in, the window state and the table of
peers running the mod must all survive that. Records are loaded when the first player object of
a session appears.

## 3. Verify on first deploy

1. `tally stats`: RPC registered, sound resolves, `levelUpEffects` names; whether
   `fx_GP_Activation` carries its `ZSFX` on a child (the resolver searches children).
2. Hits on a creature owned by another client with the mod arrive once (theirs) and not twice.
3. Damage from another player's arrows names the bow, not "Bows", while it is still equipped.
4. Left Alt frees the cursor and rows are clickable; releasing restores the camera.

## 4. Decisions (2026-09-18)

1. **Name: Tally**, `com.jumpingmushroom.tally`, in the `Tally` directory as created.
2. **Real damage means the health delta before the zero clamp**, i.e. `hit.GetTotalDamage()`
   after `ApplyDamage`, overkill included, as Recount counts it.
3. **DoT ticks are their own abilities** ("Burning", "Poison", "Spirit") credited to the most
   recent applier, because the game keeps one damage pool per effect.
4. **Records need real hits.** Approximate damage never sets a record.
5. **No JSON library.** The game ships Newtonsoft, but a 200-line reader/writer avoids another
   assembly in `lib/`.
