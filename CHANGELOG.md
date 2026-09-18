# Changelog

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
- `recount` console command: show, hide, reset, mode, seg, report, dump, records, forget, sfx,
  peers, stats.
