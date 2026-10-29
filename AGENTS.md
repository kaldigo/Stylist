# Maintained Stylist fork

Preserve internal identity, command, saved configuration and every IPC endpoint.
Keep sorting contained in GearsetStorage and its scheduling hook. UnmoveItems
controls it. Use RaptureGearsetModule.IsItemRegisteredToGearset on the actual
InventoryItem pointer: this is the game's check for the inventory icon's gearset
mark. Marked items belong in the armoury, unmarked items in regular inventory.
Never replace this with saved-entry scans, ID comparisons or quality/appearance
heuristics. An unavailable marker check must never be treated as unmarked.
Never move equipped items or overwrite occupied destinations. Re-read state
between moves and keep cancellation and timeout bounded.

Keep pinned submodules unchanged unless explicitly reviewing a dependency update.
Routine upstream merges and releases must use the checked policy, exact tested
commit, incremented actual DLL version and immutable archives/checksums. Storage,
IPC, native interop, lifecycle, config, dependency and publishing edits are manual.

Before release run the Release build, tests/StorageChecks, Python regression
suite and tools/check_package.py. Keep in-game acceptance limitations explicit.
