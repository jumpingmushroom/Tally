# Tally

A combat meter in the mould of WoW's Recount and Skada: a small window of coloured bars, one
per player, sorted by damage, with **DPS, damage taken, healing, biggest hits and all-time
personal records** a click away. It shows what the players around you are doing, not just you.

![Records mode: all-time best hit with every weapon](https://raw.githubusercontent.com/jumpingmushroom/Tally/master/docs/images/records.png)

- **Damage Done / DPS / Damage Taken / Healing / Max Hit / Records**, each in the same bar list.
  Hover a bar for the numbers, click it to drill into weapons, sources or top hits.
- **Segments**: Overall, the current fight, the previous few.
- **Multiplayer**: real post-mitigation damage for everyone nearby, shared client-to-client over
  the game's own RPC. Nothing to install on the server; players without the mod are unaffected.
  Damage the mod can only estimate is marked with a `~`.
- **Records**: your biggest hit per weapon, kept across sessions per character and world. Beat
  one and a banner reads *NEW RECORD — 487 with Frostner* while a vanilla fanfare plays.
- Burning, poison and spirit ticks credited to whoever applied them.

![Damage Done, and drilling into a player's weapons](https://raw.githubusercontent.com/jumpingmushroom/Tally/master/docs/images/meter.png)
![Drill-in](https://raw.githubusercontent.com/jumpingmushroom/Tally/master/docs/images/drill.png)

`F7` toggles the window; hold `Left Alt` to free the cursor to drag, resize and click. Report
posts the current view to chat. Console: `tally` for the rest.

Requires BepInEx and Jotunn. Source and issues: https://github.com/jumpingmushroom/Tally
