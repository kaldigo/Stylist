# Stylist fork

Source: https://github.com/NightmareXIV/Stylist

This fork preserves `Stylist`, `/stylist`, configuration and every IPC endpoint.
Use it as a replacement for upstream Stylist; do not enable both copies.

The existing **Move replaced items from armory chest to regular inventory**
setting now sorts storage after updates, even when no upgrade was needed or
re-equipping is disabled. Membership matches Steward's InventoryView: collect
item IDs from every existing saved gearset and equipped slot, normalized with
`% 1000000`. All copies of a referenced item belong in the armoury, regardless
of HQ, dye, materia or glamour. This includes unchanged and blacklisted sets.
Equipped items remain equipped; soul crystals remain in dedicated storage.

The final reconciliation reads all saved sets and live containers after upstream
moves finish, then re-reads between moves. Referenced bag items go into the
armoury and unreferenced armoury items go into ordinary inventory. Full or blocked
destinations warn without overwriting items. Logout, character changes or disabling
the setting cancel sorting. Missing gearset data does not mean unreferenced.

Native trace: `IsItemRegisteredToGearset` enumerates all sets, but resolves each
reference to an actual equipped/armoury pointer; ordinary bags are not candidates.
It therefore cannot recover gearset items already in bags. The previous tests
incorrectly modeled that predicate as a location-independent per-item flag.
The regression suite now supplies saved sets and exercises unchanged-set recovery,
one-set updates preserving other sets, highest-index sets and a second no-op pass.

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
