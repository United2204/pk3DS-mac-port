using pk3DS.Core;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>Per-item stats: price, held effect, fling power and consumption effects.</summary>
public static class ItemEditor
{
    public static ItemEntryResponse GetEntry(ItemEntryRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        var garc = config.GetGARCData("item");
        var item = new Item(garc.Files[RequireItem(garc, request.ItemIndex)]);
        return new ItemEntryResponse(request.ItemIndex, item.BuyPrice, item.HeldEffect, item.HeldArgument,
            item.FlingPower, item.EffectField, item.EffectBattle, item.HealValue);
    }

    public static ExportResult Export(ItemExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "item", ["item"], config =>
            {
                var garc = config.GetGARCData("item");
                var index = RequireItem(garc, request.ItemIndex);
                var item = new Item(garc.Files[index])
                {
                    // The buy price is stored divided by ten, so the raw field tops out at 655350.
                    BuyPrice = Math.Clamp(request.BuyPrice, 0, 655350),
                    HeldEffect = (byte)Math.Clamp(request.HeldEffect, 0, byte.MaxValue),
                    HeldArgument = (byte)Math.Clamp(request.HeldArgument, 0, byte.MaxValue),
                    FlingPower = (byte)Math.Clamp(request.FlingPower, 0, byte.MaxValue),
                    EffectField = (byte)Math.Clamp(request.EffectField, 0, byte.MaxValue),
                    EffectBattle = (byte)Math.Clamp(request.EffectBattle, 0, byte.MaxValue),
                    HealValue = Math.Clamp(request.HealValue, 0, byte.MaxValue),
                };
                garc.Files[index] = item.Write();
                garc.Save();
                return [config.GetGARCFileName("item")];
            });

    // Index 0 is the empty item slot.
    private static int RequireItem(GARCFile garc, int itemIndex) =>
        itemIndex >= 1 && itemIndex < garc.Files.Length
            ? itemIndex
            : throw new WorkspaceException("El objeto indicado no existe.");
}
