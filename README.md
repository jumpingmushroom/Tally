# Tally

A combat meter for Valheim in the mould of the classic World of Warcraft Recount and Skada
addons: a small window of coloured bars, one per player, sorted by damage, with DPS, damage
taken, healing, biggest hits and all-time personal records a click away. It shows what the
players around you are doing, not just you, and it plays a fanfare when you beat your own
record with a weapon.

Built against **Valheim 1.0.15**. Nothing to install on the server.

## Install

Requires BepInEx 5 and [Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/).
Drop `Tally.dll` into `BepInEx/plugins/` or install through a mod manager.

## The window

`F7` shows and hides it. Hold **Left Alt** to free the cursor while playing; then drag the
title bar to move it, the bottom-right corner to resize it, click a bar to drill in, and use
the mouse wheel to page. Both keys are configurable, and the cursor is free anyway while the
map or inventory is open.

The font is a setting (`Window.Font`), defaulting to Valheim-AveriaSerifLibre, the face the game
uses for item text: it keeps its lowercase and its digits at meter sizes, where the runic
Valheim-Norse does not. Any other font the game has loaded works too, offered as a dropdown in
ConfigurationManager once you are in a world. `tally fonts` lists them, `tally font <name>`
switches.

Text carries the font's own outline (`Window.TextOutline`), which is what keeps it readable
where the window is see-through; `Window.Opacity` defaults to 0.75 for the same reason.

Title bar buttons: **Reset** clears every segment. **Seg** pages through Overall, the current
fight and the previous few. **Mode** steps through the modes. **Rep** posts the current view to
chat (or to your console, by config). **x** closes.

Each bar is a player: rank and name on the left, value and share of the total on the right, the
bar's length its value against the top player's. A `~` before a value means it includes hits the
mod could only estimate (see below). Hover a bar for the numbers behind it; click it to see that
player's weapons, or what hit them, or what healed them, or their biggest hits.

## Modes

- **Damage Done** — real post-mitigation damage, as it came off the target's health.
- **DPS** — damage over the time that player spent actively fighting: gaps between their hits,
  each capped at five seconds. Tally's definition, so a player who arrives late is not
  penalised for the time before they did.
- **Damage Taken** — with a breakdown by what hit them.
- **Healing** — food regen and healing effects, effective healing only; overheal is in the tooltip.
- **Max Hit** — each player's biggest single hit; drilling in lists their top ten with weapon and
  target. Sneak attacks are marked with `*`.
- **Records** — your all-time best hit with each weapon, across sessions; click a weapon for its
  top ten with target, date, damage type and skill level at the time.

Burning, poison and spirit damage is credited to whoever applied it, tick by tick, as its own
row under that player. Damage to trees, ore and built pieces is ignored unless
`Tracking.IncludeStructures` is on.

## How it sees other players

Valheim computes real damage only on the client that owns the target, and that client sees
every hit on it with the attacker attached. So your client already knows the true damage of
every player hitting the creatures near you. What it cannot see is hits on creatures owned by
someone else, and those are what the mod shares: every client running it sends the hits it
observed to the others in a small batched packet over the game's own routed RPC, which a
vanilla dedicated server relays without needing to understand it. Events are numbered so a
duplicate never counts twice, and events further than 100 m away are ignored.

If the owner of what you hit does not run the mod, nobody will ever report the real number, so
your own client records the damage it dealt before armour and resistances and marks it with a
`~`.

## Records and the fanfare

Your biggest hit with each weapon, and overall, is kept per character and world in
`BepInEx/config/Tally/`. When a hit beats one, a banner reads e.g. *NEW RECORD — 487 with
Frostner* and a vanilla sound plays: the guardian-power activation by default. The prefab name
is in the config; `tally sfx` in the console (F5) plays the configured one and lists a few
alternatives, including `@levelup` for the skill level-up sound. Banner, sound and a minimum
damage threshold are separate settings. Estimated (`~`) hits never set a record.

## Console

`tally show|hide|reset`, `tally mode damage|dps|taken|healing|maxhit|records`,
`tally seg`, `tally report`, `tally dump` (the current view as text), `tally records`,
`tally forget` (delete this character's records for this world), `tally sfx [name]`,
`tally peers` (who else is running it), `tally stats`.

## Building

```
./build/deploy.sh                # build and copy to the r2modman profile on the rig
./build/package.sh               # Thunderstore zip in dist/
```

Reference assemblies go in `lib/` (gitignored) or point `VALHEIM_INSTALL` at a game install.
`UnityEngine.AudioModule.dll` is needed alongside the usual set.

## License

MIT.
