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
        var module = RaptureGearsetModule.Instance();
        if(module->Entries.Pointer != null) { for(var i=0;i<module->Entries.Length;i++) NativeMemory.Free(module->Entries[i].Pointer); NativeMemory.Free(module->Entries.Pointer); }
        module->Entries.Pointer = (RaptureGearsetModule.Entry*)NativeMemory.AllocZeroed(3, (nuint)sizeof(RaptureGearsetModule.Entry));
        module->Entries.Length = 3;
        module->Entries[0].Valid = true;
        module->Entries[0].Flags = RaptureGearsetModule.GearsetFlag.Exists;
        for(var i=0;i<3;i++) {
            module->Entries[i].Pointer = (RaptureGearsetModule.GearsetItem*)NativeMemory.AllocZeroed(2, (nuint)sizeof(RaptureGearsetModule.GearsetItem));
            module->Entries[i].Count = 2;
        }
        P.TaskManager.Work.Clear(); DuoLog.Warnings.Clear(); C.UnmoveItems = true; Player.CID = 1;
        InventoryManager.RejectMoves = false; InventoryManager.MoveCount = 0;
    }
    static InventoryItem Item(uint id) => new() { ItemId = id, Category = (uint)EquipSlotCategoryEnum.Head };
    static void Use(uint id) => RaptureGearsetModule.Instance()->Entries[0].Items[0].ItemId = id;
    static bool Step(GearsetStorage sorter)
    {
        typeof(GearsetStorage).GetField("nextMove", BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(sorter, 0L);
        return (bool)typeof(GearsetStorage).GetMethod("Step", BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(sorter, null)!;
    }
    static void Run() { var sorter = new GearsetStorage(); for(int i=0;i<30;i++) if(Step(sorter)) return; throw new Exception("Sort failed to finish"); }
    static void Main()
    {
        Reset(); Use(10); *Slot(InventoryType.Inventory1) = Item(10); *Slot(InventoryType.ArmoryHead) = Item(20); *Slot(InventoryType.EquippedItems) = Item(30);
        Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 10 && Slot(InventoryType.Inventory1, 1)->ItemId == 20, "Both directions"); Check(Slot(InventoryType.EquippedItems)->ItemId == 30, "Equipped unchanged");
        Reset(); Use(10); *Slot(InventoryType.ArmoryHead) = Item(10); Run(); Check(InventoryManager.MoveCount == 0, "Other saved sets protect gear");
        Reset(); Use(99); var other = RaptureGearsetModule.Instance(); other->Entries[2].Flags = RaptureGearsetModule.GearsetFlag.Exists;
        other->Entries[2].Items[0].ItemId = 10; *Slot(InventoryType.ArmoryHead) = Item(10); Slot(InventoryType.ArmoryHead)->Dye0 = 7;
        Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 10 && InventoryManager.MoveCount == 0, "Noncurrent set outside validity/update filters protects gear");
        Reset(); Use(99); other->Entries[2].Flags = RaptureGearsetModule.GearsetFlag.Exists; other->Entries[2].Items[0].ItemId = 10;
        *Slot(InventoryType.Inventory1) = Item(10); Slot(InventoryType.Inventory1)->GlamourId = 500;
        Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 10 && Slot(InventoryType.Inventory1)->ItemId == 0, "Restore displaced noncurrent-set gear despite stale saved appearance");
        Reset(); Use(10); *Slot(InventoryType.Inventory1) = Item(10); C.UnmoveItems = false; Run(); Check(InventoryManager.MoveCount == 0, "Setting disabled");
        Reset(); Use(10); *Slot(InventoryType.Inventory1) = Item(10); var changed = new GearsetStorage(); Player.CID = 2; Check(Step(changed) && InventoryManager.MoveCount == 0, "Character change cancels");
        Reset(); Use(10); *Slot(InventoryType.ArmoryHead) = Item(10); Slot(InventoryType.ArmoryHead)->Dye0 = 5; Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 10, "Stale dye must not evict saved gear");
        Reset(); Use(10); *Slot(InventoryType.ArmoryHead) = Item(1000010); Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 1000010, "HQ copy of saved item stays protected");
        Reset(); Use(1000010); *Slot(InventoryType.ArmoryHead) = Item(10); Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 10, "NQ copy of saved HQ item stays protected");
        Reset(); Use(10); *Slot(InventoryType.Inventory1) = Item(1000010); Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 1000010, "HQ gearset item enters and remains in armory");
        Reset(); Use(10); *Slot(InventoryType.Inventory1) = Item(10); var changing = new GearsetStorage();
        Check(!Step(changing) && Slot(InventoryType.ArmoryHead)->ItemId == 10, "First move enters armory");
        Use(99); Check(Step(changing) && Slot(InventoryType.ArmoryHead)->ItemId == 10 && DuoLog.Warnings.Count == 1, "Changing membership cannot reverse an earlier move");
        Reset(); Use(10); *Slot(InventoryType.ArmoryHead) = Item(10); Slot(InventoryType.ArmoryHead)->MateriaId = 7; Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 10, "Stale materia must not evict saved gear");
        Reset(); Use(10); *Slot(InventoryType.ArmoryHead) = Item(10); Slot(InventoryType.ArmoryHead)->GlamourId = 7; Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 10, "Stale glamour must not evict saved gear");
        Reset(); Use(10); *Slot(InventoryType.Inventory1) = Item(10);
        foreach(var type in new[]{InventoryType.Inventory1,InventoryType.Inventory2,InventoryType.Inventory3,InventoryType.Inventory4}) for(int i=0;i<2;i++) if(Slot(type,i)->ItemId == 0) *Slot(type,i) = Item(99);
        *Slot(InventoryType.ArmoryHead) = Item(20); Run(); Check(Slot(InventoryType.ArmoryHead, 1)->ItemId == 10, "Inbound move frees bag capacity before outbound retry");
        Reset(); Use(10); *Slot(InventoryType.ArmoryHead) = Item(20);
        foreach(var type in new[]{InventoryType.Inventory1,InventoryType.Inventory2,InventoryType.Inventory3,InventoryType.Inventory4}) for(int i=0;i<2;i++) *Slot(type,i) = Item(99);
        Run(); Check(DuoLog.Warnings.Count == 1 && InventoryManager.MoveCount == 0, "Full destination warns without overwriting");
        Reset(); GearsetStorage.Schedule(); GearsetStorage.Schedule(); Check(P.TaskManager.Work.Count == 2 && P.TaskManager.Work[0](), "Batch coalescing");
        Reset(); Use(10); *Slot(InventoryType.Inventory1) = Item(10); InventoryManager.RejectMoves = true;
        var stuck = new GearsetStorage(); Check(!Step(stuck), "Rejected move remains pending");
        typeof(GearsetStorage).GetField("started", BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(stuck, Environment.TickCount64 - 301000);
        Check(Step(stuck) && DuoLog.Warnings.Count == 1 && Slot(InventoryType.Inventory1)->ItemId == 10, "Blocked move times out without item loss");
        Reset(); Use(10); *Slot(InventoryType.Inventory1) = Item(10); InventoryManager.RejectMoves = true; Run();
        Check(InventoryManager.MoveCount == 10 && DuoLog.Warnings.Count == 1, "Rejected move retries are bounded");
        Console.WriteLine("Storage checks passed: both directions, shared gear, variants, disabled setting, character change, capacity, batching and rejected moves.");
    }
}
