# Changelog

## 1.0.0 — first release

A combat meter for Valheim: who is doing what damage, to what, and how much of it.

- **Six modes** in one window of coloured bars — Damage Done, DPS, Damage Taken, Healing,
  Max Hit and Records. Rank and name on the left, value and share on the right, the bar's
  length its value against the top player's. Hover for the numbers behind a bar; click to
  drill into that player's weapons, what hit them, what healed them, or their biggest hits.
- **Segments**: Overall, the current fight, and the previous five. A fight ends after 5 s of
  quiet and is labelled by whatever took the most damage.
- **Everyone around you, not just you.** Valheim computes real damage only on the client that
  owns the target, so clients running Tally share what they saw over the game's own routed
  RPC. Nothing is installed on the server, a vanilla dedicated server relays it untouched, and
  players without the mod are unaffected. Where nobody could report the real number, the
  attacker's own pre-mitigation figure is shown and marked with a `~`.
- **All-time personal records** per weapon and overall, kept per character and world. Beating
  one raises a banner and plays a vanilla fanfare, both configurable, with a damage floor so a
  fresh character is not serenaded for every punch.
- **DoT attribution**: burning, poison and spirit ticks are credited to whoever applied them,
  tick by tick, as their own rows.
- **Readable at a glance.** The window is drawn in Valheim-AveriaSerifLibre, chosen by
  comparing every font the game loads at the real size in-game, and the text carries that
  font's outline the way the game's own HUD text does, so it survives bright scenery without
  the window having to become a solid box. `Window.Font` changes the face — `tally fonts`
  lists what is available, `tally font <name>` switches without leaving the game.
- **Console**: `tally show|hide|reset|mode|seg|report|dump|records|forget|sfx|fonts|font|peers|stats`.

Client-side. Requires BepInEx and Jotunn. Built against Valheim 1.0.15.
