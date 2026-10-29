using ECommons.GameHelpers;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Stylist.Services;

// Fork addition: run after upstream moves/equipping; never modify equipped slots.
public unsafe sealed class GearsetStorage
{
    private static long generation;
    private readonly ulong character = Player.CID;
    private long started;
    private long nextMove;
    private int moves;
    private (InventoryType Source, int Slot, uint ItemId, InventoryType Target, int Free)? pending;
    private int attempts;
    private static readonly InventoryType[] Bags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private static readonly InventoryType[] Armory = Utils.ValidInventoriesArmory.Where(x => x != InventoryType.EquippedItems && x != InventoryType.ArmoryWaist).ToArray();

    public static void Schedule()
    {
        var ticket = ++generation;
        var storage = new GearsetStorage();
        // Multiple updates in a batch collapse into one final reconciliation.
        P.TaskManager.Enqueue(() => ticket != generation || storage.Step(), new(showDebug: true, timeLimitMS: 330000));
    }

    private bool Step()
    {
        if(!C.UnmoveItems || !Svc.ClientState.IsLoggedIn || !Player.Available || Player.CID != character) return true;
        if(started == 0) started = Environment.TickCount64;
        if(Environment.TickCount64 - started > 300000 || moves >= 1000)
            return Stop("Storage sorting stopped before completion; some items may remain in their original containers.");
        if(!Player.Interactable || !IsScreenReady() || S.ItemMover.ItemsToMove.Count != 0 || S.ItemMover.ItemsToUnmove.Count != 0) return false;
        if(Environment.TickCount64 < nextMove) return false;
        var manager = InventoryManager.Instance();
        if(manager == null) return true;
        foreach(var type in Bags.Concat(Armory))
        {
            var container = manager->GetInventoryContainer(type);
            if(container == null || !container->IsLoaded) return false;
        }
        var sets = RaptureGearsetModule.Instance();
        if(sets == null) return true;
        var blocked = false;
        // Move unused armoury gear out first to make room for gearset items.
        foreach(var source in Armory.Concat(Bags))
        {
            var container = manager->GetInventoryContainer(source);
            for(var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if(item == null || item->ItemId == 0) continue;
                // This is the game's own check for the gearset mark on item icons.
                var used = sets->IsItemRegisteredToGearset(item);
                var inArmory = Armory.Contains(source);
                if(used == inArmory) continue;
                var destinations = inArmory ? Bags : Armory;
                foreach(var target in destinations)
                {
                    // Armoury container types encode the equipment slot. Use an existing
                    // item descriptor's sheet data rather than attempting incompatible moves.
                    if(!inArmory && target != ArmoryFor(new InventoryDescriptor(source, slot))) continue;
                    var destination = manager->GetInventoryContainer(target);
                    for(var free = 0; free < destination->Size; free++)
                    {
                        if(destination->GetInventorySlot(free)->ItemId != 0) continue;
                        var request = (source, slot, item->GetItemId(), target, free);
                        if(pending == request) attempts++;
                        else { pending = request; attempts = 1; }
                        if(attempts > 10) return Stop("Storage sorting stopped: the game did not complete an item move. Try again when inventory actions are available.");
                        PluginLog.Information($"Storage sort: item {item->GetItemId()}, gearset={used}, {source}/{slot} -> {target}/{free}");
                        manager->MoveItemSlot(source, (ushort)slot, target, (ushort)free, true);
                        nextMove = Environment.TickCount64 + 500;
                        moves++;
                        return false; // Re-read gearsets and containers after each move request.
                    }
                }
                blocked = true;
            }
        }
        return blocked ? Stop("Storage sorting incomplete: destination space is unavailable or an item has no supported armoury slot.") : true;
    }

    private static bool Stop(string message)
    {
        DuoLog.Warning(message);
        return true;
    }

    private static InventoryType ArmoryFor(InventoryDescriptor item) => (EquipSlotCategoryEnum)item.Data.Value.EquipSlotCategory.RowId switch
    {
        EquipSlotCategoryEnum.WeaponTwoHand or EquipSlotCategoryEnum.WeaponMainHand => InventoryType.ArmoryMainHand,
        EquipSlotCategoryEnum.OffHand => InventoryType.ArmoryOffHand,
        EquipSlotCategoryEnum.Head => InventoryType.ArmoryHead,
        EquipSlotCategoryEnum.Body => InventoryType.ArmoryBody,
        EquipSlotCategoryEnum.Gloves => InventoryType.ArmoryHands,
        EquipSlotCategoryEnum.Legs => InventoryType.ArmoryLegs,
        EquipSlotCategoryEnum.Feet => InventoryType.ArmoryFeets,
        EquipSlotCategoryEnum.Ears => InventoryType.ArmoryEar,
        EquipSlotCategoryEnum.Neck => InventoryType.ArmoryNeck,
        EquipSlotCategoryEnum.Wrists => InventoryType.ArmoryWrist,
        EquipSlotCategoryEnum.Ring => InventoryType.ArmoryRings,
        _ => default,
    };
}
