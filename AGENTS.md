# Maintained Stylist fork

Preserve internal identity, command, saved configuration and every IPC endpoint.
Keep sorting contained in GearsetStorage and its scheduling hook. UnmoveItems
controls it. Use the same membership rule as Steward's InventoryView: all existing
saved gearset entries plus equipped items, normalized by item ID % 1000000.
Do not narrow membership to updated sets, the current set, filters or blacklists.
The native icon predicate resolves an actual equipped/armoury item pointer and
cannot identify references to items stranded in ordinary inventory. Do not use
it for this reconciliation. An unavailable module must never mean unreferenced.
Never move equipped items or overwrite occupied destinations. Re-read state
between moves and keep cancellation and timeout bounded.

Keep pinned submodules unchanged unless explicitly reviewing a dependency update.
Routine upstream merges and releases must use the checked policy, exact tested
commit, incremented actual DLL version and immutable archives/checksums. Storage,
IPC, native interop, lifecycle, config, dependency and publishing edits are manual.

Before release run the Release build, tests/StorageChecks, Python regression
suite and tools/check_package.py. Keep in-game acceptance limitations explicit.
