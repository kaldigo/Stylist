# Stylist fork

Source: https://github.com/NightmareXIV/Stylist

This fork preserves `Stylist`, `/stylist`, configuration and every IPC endpoint.
Use it as a replacement for upstream Stylist; do not enable both copies.

The existing **Move replaced items from armory chest to regular inventory**
setting now sorts storage after updates, even when no upgrade was needed or
re-equipping is disabled. Sorting calls the game's own
`RaptureGearsetModule.IsItemRegisteredToGearset(item)` function, documented in
FFXIVClientStructs as the check used for the gearset mark on inventory icons.
Marked items go to the armoury chest; unmarked armoury items go to regular
inventory. There is no saved-set scan, ID matching, quality normalization or
appearance heuristic. Equipped items stay equipped and soul crystals stay in
their dedicated storage. Only the four ordinary inventory pages and supported
armoury equipment containers participate.

Sorting re-reads live containers and queries the native marker after every move.
It evacuates unmarked armoury gear first and can bring marked bag items in when
that frees bag space. Full destinations, blocked moves or timeout produce a
warning without deleting or replacing items. No spare capacity in either
direction requires freeing a slot manually. Logout, character changes or
turning off the setting cancel sorting. An unavailable gearset module stops
sorting without treating items as unmarked. Each requested move logs its native
marker result and source/destination slots.

The implementation is contained in `GearsetStorage.cs`, with one scheduling
hook replacing upstream's old replaced-item task. No dependency pins changed.
`Directory.Build.targets` adds an ExcelPage alias to the pinned ECommons build
for current API-15 compatibility and supports `DALAMUD_HOME` in CI.

Daily upstream sync uses the existing checked merge and release infrastructure.
Only clean, tested routine updates merge automatically. Storage, IPC, lifecycle,
dependency, configuration, build and publishing changes require manual review.
Routine merges explicitly dispatch a versioned preview release; missed dispatches
and source-feed publication are recoverable. Archives and checksums are immutable.

Validation: Release build; storage simulation against the production sorting
class; upstream policy tests; package and checksum checks. Native in-game move
acknowledgements, UI locks and inventory-full cases still need in-game testing.

Upstream authorship remains NightmareXIV. Fork changes were made with AI
assistance and are not intended for submission to upstream under its contribution
policy. Dependency license notices ship in `licenses/`. Upstream Stylist does
not currently include a separate license file; this fork does not assert a new
license over upstream code.
