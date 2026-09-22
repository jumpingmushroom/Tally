# Changelog

## 0.1.1 — readability

- The window, banner and tooltip now use **Valheim-AveriaSerifLibre** instead of Valheim-Norse.
  Norse is a display face: forced small caps, thin uneven strokes and cramped digits, which is
  what the meter is mostly made of. Averia Serif has true lowercase and legible numbers at the
  12-14px the rows and title bar run at.
- `Window.Font` chooses that font from every TextMeshPro font the game has loaded, as a dropdown
  in ConfigurationManager once you are in a world. `tally fonts` lists the names and
  `tally font <name>` switches without leaving the game.
- Text is outlined with the font's own outline material, the way the game's own HUD text is,
  so it reads over bright scenery instead of dissolving into it. `Window.TextOutline` turns it
  off. Because the outline does the work, `Window.Opacity` only had to go from 0.6 to 0.75:
  enough to settle the background without turning the window into a solid box over the game.
- The list holds only fonts that can draw Latin text. The game keeps ~35 Noto fallback assets
  for other scripts; they are dynamic assets with empty atlases and a window set to one drew
  nothing at all.

## 0.1.0 — first cut

- Damage Done, DPS, Damage Taken, Healing, Max Hit and Records modes in a Recount-style window
  of coloured bars: rank and name left, value and share right, hover for detail, click to drill
  into a player's weapons, sources or top hits.
- Segments: Overall, the current fight, and the previous five. A fight ends after 5 s of quiet.
- Sharing between players over the game's routed RPC. Nothing on the server; players without
  the mod are unaffected. Hits on creatures owned by a client without the mod are recorded by
  the attacker pre-mitigation and shown with a "~".
- Burning, poison and spirit ticks credited to whoever applied them.
- All-time personal records per weapon and overall, per character and world, with a banner and
  a vanilla fanfare (`fx_GP_Activation` by default, swappable in the config).
- `tally` console command: show, hide, reset, mode, seg, report, dump, records, forget, sfx,
  peers, stats.
