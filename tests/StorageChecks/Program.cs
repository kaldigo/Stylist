using System.Reflection;
using System.Runtime.InteropServices;
using Stylist.Services;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

unsafe class Program
{
    static InventoryItem* Slot(InventoryType type, int i = 0) => InventoryManager.Instance()->GetInventoryContainer(type)->GetInventorySlot(i);
    static void Check(bool condition, string message) { if(!condition) throw new Exception(message); }
    static void Reset()
    {
        foreach(var p in InventoryManager.Containers.Values) { var c = (InventoryContainer*)p; NativeMemory.Free(c->Slots); NativeMemory.Free(c); }
        InventoryManager.Containers.Clear();
        foreach(var type in Enum.GetValues<InventoryType>())
        {
            var c = (InventoryContainer*)NativeMemory.AllocZeroed((nuint)sizeof(InventoryContainer));
            c->Size = 2; c->IsLoaded = type != InventoryType.ArmoryWaist;
            c->Slots = (InventoryItem*)NativeMemory.AllocZeroed(2, (nuint)sizeof(InventoryItem));
            InventoryManager.Containers[type] = (nint)c;
        }
        RaptureGearsetModule.Available = true;
        RaptureGearsetModule.Saved = new RaptureGearsetModule.GearsetEntry[100];
        Save(0, 900);
        S.ItemMover.ItemsToMove.Clear(); S.ItemMover.ItemsToUnmove.Clear();
        P.TaskManager.Work.Clear(); DuoLog.Warnings.Clear(); C.UnmoveItems = true; Player.CID = 1;
        InventoryManager.RejectMoves = false; InventoryManager.MoveCount = 0;
    }
    static void Save(int index, params uint[] ids) => RaptureGearsetModule.Saved[index] = new() { Flags = RaptureGearsetModule.GearsetFlag.Exists, Items = ids.Select(id => new RaptureGearsetModule.GearsetItem { ItemId = id }).ToArray() };
    static InventoryItem Item(uint id, bool marked = false)
    {
        if(marked) Save(99, id);
        return new() { ItemId = id, Category = (uint)EquipSlotCategoryEnum.Head };
    }
    static bool Step(GearsetStorage sorter)
    {
        typeof(GearsetStorage).GetField("nextMove", BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(sorter, 0L);
        return (bool)typeof(GearsetStorage).GetMethod("Step", BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(sorter, null)!;
    }
    static void Run() { var sorter = new GearsetStorage(); for(int i=0;i<30;i++) if(Step(sorter)) return; throw new Exception("Sort failed to finish"); }
    static void Main()
    {
        Reset(); *Slot(InventoryType.Inventory1) = Item(10, true); *Slot(InventoryType.ArmoryHead) = Item(20); *Slot(InventoryType.EquippedItems) = Item(30);
        Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 10 && Slot(InventoryType.Inventory1, 1)->ItemId == 20, "Marked in, unmarked out"); Check(Slot(InventoryType.EquippedItems)->ItemId == 30, "Equipped unchanged");
        Reset(); *Slot(InventoryType.ArmoryHead) = Item(10, true); Run(); Check(InventoryManager.MoveCount == 0, "Marked armory gear stays");
        Reset(); *Slot(InventoryType.Inventory1) = Item(10, true); *Slot(InventoryType.ArmoryHead) = Item(10, false);
        Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 10 && Slot(InventoryType.ArmoryHead, 1)->ItemId == 10, "Same Steward membership protects all copies");
        Reset(); *Slot(InventoryType.Inventory1) = Item(10, true); C.UnmoveItems = false; Run(); Check(InventoryManager.MoveCount == 0, "Setting disabled");
        Reset(); *Slot(InventoryType.Inventory1) = Item(10, true); var changed = new GearsetStorage(); Player.CID = 2; Check(Step(changed) && InventoryManager.MoveCount == 0, "Character change cancels");
        Reset(); *Slot(InventoryType.ArmoryHead) = Item(1000010, true); Slot(InventoryType.ArmoryHead)->Dye0 = 5; Slot(InventoryType.ArmoryHead)->MateriaId = 7; Slot(InventoryType.ArmoryHead)->GlamourId = 500;
        Run(); Check(InventoryManager.MoveCount == 0, "Marked item protected irrespective of HQ and appearance");
        Reset(); *Slot(InventoryType.ArmoryHead) = Item(1000010, false); Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 0, "Unreferenced HQ moves out");
        Reset(); *Slot(InventoryType.Inventory1) = Item(1000010, true); Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 1000010 && InventoryManager.MoveCount == 1, "Marked item enters and stays, with no reversal");
        Reset(); *Slot(InventoryType.ArmoryHead) = Item(20); RaptureGearsetModule.Available = false;
        Run(); Check(InventoryManager.MoveCount == 0, "Unavailable native module never treated as unmarked");
        Reset(); *Slot(InventoryType.Inventory1) = Item(10, true);
        foreach(var type in new[]{InventoryType.Inventory1,InventoryType.Inventory2,InventoryType.Inventory3,InventoryType.Inventory4}) for(int i=0;i<2;i++) if(Slot(type,i)->ItemId == 0) *Slot(type,i) = Item(99);
        *Slot(InventoryType.ArmoryHead) = Item(20); Run(); Check(Slot(InventoryType.ArmoryHead, 1)->ItemId == 10, "Inbound move frees bag capacity before outbound retry");
        Reset(); *Slot(InventoryType.ArmoryHead) = Item(20);
        foreach(var type in new[]{InventoryType.Inventory1,InventoryType.Inventory2,InventoryType.Inventory3,InventoryType.Inventory4}) for(int i=0;i<2;i++) *Slot(type,i) = Item(99);
        Run(); Check(DuoLog.Warnings.Count == 1 && InventoryManager.MoveCount == 0, "Full destination warns without overwriting");
        Reset(); GearsetStorage.Schedule(); GearsetStorage.Schedule(); Check(P.TaskManager.Work.Count == 2 && P.TaskManager.Work[0](), "Batch coalescing");
        Reset(); *Slot(InventoryType.Inventory1) = Item(10, true); InventoryManager.RejectMoves = true;
        var stuck = new GearsetStorage(); Check(!Step(stuck), "Rejected move remains pending");
        typeof(GearsetStorage).GetField("started", BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(stuck, Environment.TickCount64 - 301000);
        Check(Step(stuck) && DuoLog.Warnings.Count == 1 && Slot(InventoryType.Inventory1)->ItemId == 10, "Blocked move times out without item loss");
        Reset(); *Slot(InventoryType.Inventory1) = Item(10, true); InventoryManager.RejectMoves = true; Run();
        Check(InventoryManager.MoveCount == 10 && DuoLog.Warnings.Count == 1, "Rejected move retries are bounded");
        // No changed set, no upstream move requests: recover items from ALL saved sets.
        Reset(); Save(0, 30); Save(7, 10); Save(99, 1000011);
        *Slot(InventoryType.EquippedItems) = Item(30);
        *Slot(InventoryType.Inventory1) = Item(10);
        *Slot(InventoryType.Inventory2) = Item(11);
        *Slot(InventoryType.ArmoryHead) = Item(20);
        GearsetStorage.Schedule();
        var run = new GearsetStorage();
        for(int i=0;i<30&&!Step(run);i++) {}
        Check(Slot(InventoryType.ArmoryHead)->ItemId == 10 && Slot(InventoryType.ArmoryHead,1)->ItemId == 11, "Unchanged sets, including last entry and HQ normalized, recovered from bags");
        Check(InventoryManager.MoveCount == 3, "Recovery finishes without reversals");
        Run(); Check(InventoryManager.MoveCount == 3, "Second reconciliation is a no-op");
        Reset(); Save(8, 10); *Slot(InventoryType.ArmoryHead) = Item(10);
        Save(0, 12); *Slot(InventoryType.Inventory1) = Item(12);
        Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 10 && Slot(InventoryType.ArmoryHead,1)->ItemId == 12, "Updating one set cannot evict another set's item");
        Reset(); *Slot(InventoryType.EquippedItems) = Item(10); *Slot(InventoryType.ArmoryHead) = Item(1000010);
        Run(); Check(InventoryManager.MoveCount == 0, "Equipped membership agrees with Steward");
        Reset(); Save(8, 20); RaptureGearsetModule.Saved[8].Flags = 0; *Slot(InventoryType.ArmoryHead) = Item(20);
        Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 0, "Deleted sets do not retain items");
        Reset(); *Slot(InventoryType.ArmoryHead) = Item(20); S.ItemMover.ItemsToMove.Add(1);
        Check(!Step(new GearsetStorage()) && InventoryManager.MoveCount == 0, "Reconciliation waits for upstream moves");
        Console.WriteLine("Storage checks passed: all saved sets including unchanged sets, Steward membership, no reversal, equipped protection, cancellation, capacity and retries.");
    }
}
