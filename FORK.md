# Stylist fork

Source: https://github.com/NightmareXIV/Stylist

This fork preserves `Stylist`, `/stylist`, configuration and every IPC endpoint.
Use it as a replacement for upstream Stylist; do not enable both copies.

The existing **Move replaced items from armory chest to regular inventory**
setting now sorts storage after updates, even when no upgrade was needed or
re-equipping is disabled. All valid saved gearsets, including blacklisted sets,
protect their base item IDs, for both NQ and HQ copies. Every existing entry is considered even
if it cannot currently be equipped. Dye, materia and glamour differences do
not invalidate membership: saved details can be stale. Matching duplicate items
are retained. Equipped items remain equipped; soul
crystals remain in their dedicated storage. Only the four ordinary inventory
pages and supported armoury equipment containers participate.

Sorting re-reads live containers and gearsets after every move, evacuates unused
armoury gear first, and can bring used bag items in when that frees bag space.
Full destinations, blocked moves or timeout produce a warning without deleting
or replacing items. No spare capacity in either direction requires freeing a
slot manually. Logout, character changes or disabling the setting cancel sorting.
If no saved item IDs are available, sorting stops without moving anything.
If gearset membership changes during a pass, sorting stops instead of reversing
earlier moves. Each requested move logs the item ID, membership decision and slots.

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
