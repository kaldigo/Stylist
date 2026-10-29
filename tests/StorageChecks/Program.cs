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
        if(module->Entries.Pointer != null) { NativeMemory.Free(module->Entries[0].Pointer); NativeMemory.Free(module->Entries.Pointer); }
        module->Entries.Pointer = (RaptureGearsetModule.Entry*)NativeMemory.AllocZeroed(3, (nuint)sizeof(RaptureGearsetModule.Entry));
        module->Entries.Length = 3;
        module->Entries[0].Valid = true;
        module->Entries[0].Pointer = (RaptureGearsetModule.GearsetItem*)NativeMemory.AllocZeroed(2, (nuint)sizeof(RaptureGearsetModule.GearsetItem));
        module->Entries[0].Count = 2;
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
        Reset(); Use(10); *Slot(InventoryType.Inventory1) = Item(10); C.UnmoveItems = false; Run(); Check(InventoryManager.MoveCount == 0, "Setting disabled");
        Reset(); Use(10); *Slot(InventoryType.Inventory1) = Item(10); var changed = new GearsetStorage(); Player.CID = 2; Check(Step(changed) && InventoryManager.MoveCount == 0, "Character change cancels");
        Reset(); Use(10); *Slot(InventoryType.ArmoryHead) = Item(10); Slot(InventoryType.ArmoryHead)->Dye0 = 5; Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 0, "Different dye variant is unused");
        Reset(); Use(10); *Slot(InventoryType.ArmoryHead) = Item(1000010); Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 0, "HQ distinguished");
        Reset(); Use(10); *Slot(InventoryType.ArmoryHead) = Item(10); Slot(InventoryType.ArmoryHead)->MateriaId = 7; Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 0, "Materia distinguished");
        Reset(); Use(10); *Slot(InventoryType.ArmoryHead) = Item(10); Slot(InventoryType.ArmoryHead)->GlamourId = 7; Run(); Check(Slot(InventoryType.ArmoryHead)->ItemId == 0, "Glamour distinguished");
        Reset(); Use(10); *Slot(InventoryType.Inventory1) = Item(10);
        foreach(var type in new[]{InventoryType.Inventory1,InventoryType.Inventory2,InventoryType.Inventory3,InventoryType.Inventory4}) for(int i=0;i<2;i++) if(Slot(type,i)->ItemId == 0) *Slot(type,i) = Item(99);
        *Slot(InventoryType.ArmoryHead) = Item(20); Run(); Check(Slot(InventoryType.ArmoryHead, 1)->ItemId == 10, "Inbound move frees bag capacity before outbound retry");
        Reset(); *Slot(InventoryType.ArmoryHead) = Item(20);
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
