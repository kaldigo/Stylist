# Maintained Stylist fork

Preserve internal identity, command, saved configuration and every IPC endpoint.
Keep sorting contained in GearsetStorage and its scheduling hook. UnmoveItems
controls it; protect every valid saved gearset, not only the updated subset.
Never move equipped items or overwrite occupied destinations. Re-read state
between moves and keep cancellation and timeout bounded.

Keep pinned submodules unchanged unless explicitly reviewing a dependency update.
Routine upstream merges and releases must use the checked policy, exact tested
commit, incremented actual DLL version and immutable archives/checksums. Storage,
IPC, native interop, lifecycle, config, dependency and publishing edits are manual.

Before release run the Release build, tests/StorageChecks, Python regression
suite and tools/check_package.py. Keep in-game acceptance limitations explicit.
