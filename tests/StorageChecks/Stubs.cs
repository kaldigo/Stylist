global using static Harness;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

// In-memory native-boundary substitutes; the actual production sorter is linked.
public static class Harness
{
    public static Configuration C = new();
    public static Plugin P = new();
    public static bool IsScreenReady() => true;
    public static class Svc { public static Client ClientState = new(); }
    public sealed class Client { public bool IsLoggedIn = true; }
    public sealed class Configuration { public bool UnmoveItems = true; }
    public sealed class Plugin { public Tasks TaskManager = new(); }
    public sealed class Tasks
    {
        public List<Func<bool>> Work = [];
        public void Enqueue(Func<bool> f, Options options) => Work.Add(f);
    }
    public sealed class Options { public Options(bool showDebug, int timeLimitMS) {} }
    public static class DuoLog { public static List<string> Warnings = []; public static void Warning(string s) => Warnings.Add(s); }
    public static class PluginLog { public static void Information(string s) {} }
    public static class S { public static Mover ItemMover = new(); }
    public sealed class Mover { public List<int> ItemsToMove = []; public List<int> ItemsToUnmove = []; }
}
namespace ECommons.GameHelpers
{
    public static class Player { public static ulong CID = 1; public static bool Available = true, Interactable = true; }
}
namespace ECommons.ExcelServices
{
    public enum EquipSlotCategoryEnum { WeaponTwoHand, WeaponMainHand, OffHand, Head, Body, Gloves, Legs, Feet, Ears, Neck, Wrists, Ring }
}
namespace Stylist
{
    public static class Utils
    {
        public static InventoryType[] ValidInventoriesArmory = [InventoryType.EquippedItems, InventoryType.ArmoryMainHand, InventoryType.ArmoryHead, InventoryType.ArmoryWaist];
    }
    public unsafe struct InventoryDescriptor
    {
        public DataRow Data;
        public InventoryDescriptor(InventoryType type, int slot) { Data = new() { Value = new() { EquipSlotCategory = new() { RowId = InventoryManager.Instance()->GetInventoryContainer(type)->GetInventorySlot(slot)->Category } } }; }
    }
    public struct DataRow { public ItemRow Value; }
    public struct ItemRow { public Row EquipSlotCategory; }
    public struct Row { public uint RowId; }
}
namespace FFXIVClientStructs.FFXIV.Client.Game
{
    public enum InventoryType { None, Inventory1, Inventory2, Inventory3, Inventory4, EquippedItems, ArmoryMainHand, ArmoryOffHand, ArmoryHead, ArmoryBody, ArmoryHands, ArmoryWaist, ArmoryLegs, ArmoryFeets, ArmoryEar, ArmoryNeck, ArmoryWrist, ArmoryRings }
    public unsafe struct InventoryItem
    {
        public uint ItemId, GlamourId, Category;
        public bool GearsetMarked;
        public byte Dye0, Dye1, Grade;
        public ushort MateriaId;
        public uint GetItemId() => ItemId;
        public ReadOnlySpan<byte> Stains => new byte[] { Dye0, Dye1 };
        public ReadOnlySpan<ushort> Materia => new ushort[] { MateriaId };
        public ReadOnlySpan<byte> MateriaGrades => new byte[] { Grade };
    }
    public unsafe struct InventoryContainer
    {
        public int Size;
        public bool IsLoaded;
        public InventoryItem* Slots;
        public InventoryItem* GetInventorySlot(int i) => Slots + i;
    }
    public unsafe struct InventoryManager
    {
        private static InventoryManager* instance = (InventoryManager*)NativeMemory.AllocZeroed(1);
        public static Dictionary<InventoryType, nint> Containers = [];
        public static bool RejectMoves;
        public static int MoveCount;
        public static InventoryManager* Instance() => instance;
        public InventoryContainer* GetInventoryContainer(InventoryType type) => (InventoryContainer*)Containers[type];
        public void MoveItemSlot(InventoryType source, ushort slot, InventoryType target, ushort free, bool ignored)
        {
            MoveCount++;
            if(RejectMoves) return;
            *GetInventoryContainer(target)->GetInventorySlot(free) = *GetInventoryContainer(source)->GetInventorySlot(slot);
            *GetInventoryContainer(source)->GetInventorySlot(slot) = default;
        }
    }
}
namespace FFXIVClientStructs.FFXIV.Client.UI.Misc
{
    public unsafe struct RaptureGearsetModule
    {
        private static RaptureGearsetModule* instance = (RaptureGearsetModule*)NativeMemory.AllocZeroed(1);
        public static bool Available = true;
        public static GearsetEntry[] Saved = new GearsetEntry[100];
        public Span<GearsetEntry> Entries => Saved;
        public static RaptureGearsetModule* Instance() => Available ? instance : null;
        [Flags] public enum GearsetFlag : byte { Exists = 1 }
        public struct GearsetEntry
        {
            public GearsetFlag Flags;
            public GearsetItem[] Items;
        }
        public struct GearsetItem { public uint ItemId; }
    }
}
