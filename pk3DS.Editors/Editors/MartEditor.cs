using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>Gen VII regular and Battle Point inventories stored in <c>Shop.cro</c>.</summary>
public static class MartEditor
{
    private const string CroFile = "Shop.cro";
    private const int LengthOffset = 0x52D2;
    private const int UsumRegularLengthOffset = LengthOffset + 4 + 7;
    private const int UsumRegularOffset = 0x50BC;
    private const int UsumBattlePointOffset = 0x52FA;

    private static readonly byte[] SmSignature =
    [
        0x2D, 0x00, 0x00, 0x00, 0x3B, 0x00, 0x00, 0x00, 0x2F, 0x00, 0x00, 0x00, 0x3D, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00,
        0x10, 0x00, 0x00, 0x00, 0x0E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00,
    ];
    private static readonly byte[] SmBattlePointSignature =
    [
        0x09, 0x0B, 0x0D, 0x0F, 0x11, 0x13, 0x14, 0x15, 0x09, 0x04, 0x08, 0x0C, 0x05, 0x04, 0x0B, 0x03,
        0x0A, 0x06, 0x0A, 0x06, 0x04, 0x05, 0x07, 0x01,
    ];

    private static readonly int[] SmRegularLengths = [9, 11, 13, 15, 17, 19, 20, 21, 9, 4, 8, 12, 5, 4, 11, 3, 10, 6, 10, 6, 4, 5, 7];
    private static readonly int[] SmBattlePointLengths = [8, 7, 18, 12, 21, 16];
    private static readonly string[] SmRegularNames =
    [
        "No Trials", "1 Trial", "2 Trials", "3 Trials", "4 Trials", "5 Trials", "6 Trials", "7 Trials",
        "Konikoni City [Incenses]", "Konikoni City [Herbs]", "Hau'oli City [X Items]", "Route 2 [Misc]",
        "Heahea City [TMs]", "Royal Avenue [TMs]", "Route 8 [Misc]", "Paniola Town [Poké Balls]",
        "Malie City [TMs]", "Mount Hokulani [Vitamins]", "Seafolk Village [TMs]", "Konikoni City [TMs]",
        "Konikoni City [Stones]", "Thrifty Megamart, Left [Poké Balls]", "Thrifty Megamart, Middle [Misc]",
        "Thrifty Megamart, Right [Strange Souvenir]",
    ];
    private static readonly string[] SmBattlePointNames =
    [
        "Battle Royal Dome [Medicine]", "Battle Royal Dome [EV Training]", "Battle Royal Dome [Held Items]",
        "Battle Tree [Trade Evolution Items]", "Battle Tree [Held Items]", "Battle Tree [Mega Stones]",
    ];
    private static readonly string[] UsumRegularNames =
    [
        "No Trials", "1 Trial", "2 Trials", "3 Trials", "4 Trials", "5 Trials", "6 Trials", "7 Trials",
        "Konikoni City [Incenses]", "Konikoni City [Herbs]", "Hau'oli City [X Items]", "Route 2 [Misc]",
        "Heahea City [TMs]", "Royal Avenue [TMs]", "Route 8 [Misc]", "Paniola Town [Poké Balls]",
        "Malie City [TMs]", "Mount Hokulani [Vitamins]", "Seafolk Village [TMs]", "Konikoni City [TMs]",
        "Konikoni City [Stones]", "Thrifty Megamart, Left [Poké Balls]", "Thrifty Megamart, Middle [Misc]",
        "Thrifty Megamart, Right [Strange Souvenir]", "Route 3 [X Items]", "Konikoni City [X Items]",
        "Tapu Village [X Items]", "Mount Lanakila [X Items]",
    ];
    private static readonly string[] UsumBattlePointNames =
    [
        "Battle Royal Dome [Medicine]", "Battle Royal Dome [EV Training]", "Battle Royal Dome [Held Items]",
        "Battle Tree [Trade Evolution Items]", "Battle Tree [Held Items]", "Battle Tree [Mega Stones]", "Beaches [Medicine]",
    ];

    public static MartTableResponse GetTable(MartTableRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var config = EditorSession.OpenReadOnly(workspace, request.Language);
        EnsureSupported(config);
        var data = File.ReadAllBytes(RequireCro(workspace));
        var (regular, battlePoints) = Read(data, config);
        return new MartTableResponse(config.Version.ToString(), regular, battlePoints, Catalogs.Items(config),
            "Shop.cro se modifica dentro de RomFS. La salida es un parche LayeredFS.");
    }

    public static ExportResult Export(MartExportRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        RequireCro(workspace);
        return EditorSession.ExportLooseFiles(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "marts", [CroFile], (_, config, scratchRomFs) =>
            {
                EnsureSupported(config);
                var path = Path.Combine(scratchRomFs, CroFile);
                var data = File.ReadAllBytes(path);
                Validate(request.Regular, request.BattlePoints, Catalogs.ItemCount(config), config, data);
                Write(data, config, request.Regular, request.BattlePoints);
                File.WriteAllBytes(path, data);
            });
    }

    internal static (MartGroup[] Regular, MartGroup[] BattlePoints) Read(byte[] data, GameConfig config)
    {
        var layout = Layout(config, data);
        var regular = ReadGroups(data, layout.RegularOffset, layout.RegularLengths, layout.RegularNames, hasPrices: false);
        var battlePoints = ReadGroups(data, layout.BattlePointOffset, layout.BattlePointLengths, layout.BattlePointNames, hasPrices: true);
        return (regular, battlePoints);
    }

    internal static void Validate(MartGroup[]? regular, MartGroup[]? battlePoints, int itemCount, GameConfig config, byte[] data)
    {
        var layout = Layout(config, data);
        ValidateGroups(regular, layout.RegularLengths, itemCount, hasPrices: false);
        ValidateGroups(battlePoints, layout.BattlePointLengths, itemCount, hasPrices: true);
    }

    private static MartGroup[] ReadGroups(byte[] data, int offset, int[] lengths, string[] names, bool hasPrices)
    {
        var groups = new MartGroup[lengths.Length];
        for (var groupIndex = 0; groupIndex < lengths.Length; groupIndex++)
        {
            var entries = new MartEntry[lengths[groupIndex]];
            for (var entryIndex = 0; entryIndex < entries.Length; entryIndex++)
            {
                var at = offset + (entryIndex * (hasPrices ? 4 : 2));
                var item = BitConverter.ToUInt16(data, at);
                int? price = hasPrices ? BitConverter.ToUInt16(data, at + 2) : null;
                entries[entryIndex] = new MartEntry(item, price);
            }
            offset += entries.Length * (hasPrices ? 4 : 2);
            groups[groupIndex] = new MartGroup(names[groupIndex], entries);
        }
        return groups;
    }

    private static void Write(byte[] data, GameConfig config, MartGroup[] regular, MartGroup[] battlePoints)
    {
        var layout = Layout(config, data);
        WriteGroups(data, layout.RegularOffset, regular, hasPrices: false);
        WriteGroups(data, layout.BattlePointOffset, battlePoints, hasPrices: true);
    }

    private static void WriteGroups(byte[] data, int offset, MartGroup[] groups, bool hasPrices)
    {
        foreach (var group in groups)
        foreach (var entry in group.Entries)
        {
            BitConverter.GetBytes((ushort)entry.Item).CopyTo(data, offset);
            if (hasPrices)
                BitConverter.GetBytes((ushort)entry.Price!.Value).CopyTo(data, offset + 2);
            offset += hasPrices ? 4 : 2;
        }
    }

    private static void ValidateGroups(MartGroup[]? groups, int[] lengths, int itemCount, bool hasPrices)
    {
        if (groups is null || groups.Length != lengths.Length)
            throw new WorkspaceException("La tabla de tiendas no conserva la cantidad de inventarios original.");
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var entries = groups[groupIndex]?.Entries;
            if (entries is null || entries.Length != lengths[groupIndex])
                throw new WorkspaceException("La tabla de tiendas no conserva la cantidad de objetos original.");
            foreach (var entry in entries)
            {
                if (entry.Item < 0 || entry.Item >= itemCount)
                    throw new WorkspaceException("La tabla de tiendas contiene un objeto inválido.");
                if (hasPrices && (entry.Price is null || entry.Price < 0 || entry.Price > ushort.MaxValue))
                    throw new WorkspaceException("La tabla BP contiene un precio inválido.");
                if (!hasPrices && entry.Price is not null)
                    throw new WorkspaceException("Las tiendas normales no admiten precios en este formato.");
            }
        }
    }

    private static LayoutInfo Layout(GameConfig config, byte[] data)
    {
        if (data is null || data.Length < LengthOffset + 4)
            throw new WorkspaceException("Shop.cro está incompleto.");
        if (config.USUM)
        {
            var battlePointLengths = data.AsSpan(LengthOffset + 4, 7).ToArray().Select(value => (int)value).ToArray();
            var regularLengths = data.AsSpan(UsumRegularLengthOffset, Math.Min(32, data.Length - UsumRegularLengthOffset)).ToArray()
                .TakeWhile(value => value > 0).Select(value => (int)value).ToArray();
            if (regularLengths.Length != UsumRegularNames.Length || regularLengths.Sum() * 2 + UsumRegularOffset > data.Length
                || battlePointLengths.Sum() * 4 + UsumBattlePointOffset > data.Length)
                throw new WorkspaceException("Shop.cro no contiene las tiendas de US/UM completas.");
            return new LayoutInfo(UsumRegularOffset, UsumBattlePointOffset, regularLengths, battlePointLengths, UsumRegularNames, UsumBattlePointNames);
        }

        var regularFound = Find(data, SmSignature, 0x5000);
        var battlePointFound = Find(data, SmBattlePointSignature, 0x5000);
        var regularOffset = regularFound < 0 ? -1 : regularFound + SmSignature.Length;
        var battlePointOffset = battlePointFound < 0 ? -1 : battlePointFound + SmBattlePointSignature.Length;
        if (regularOffset < 0 || battlePointOffset < 0
            || regularOffset + (SmRegularLengths.Sum() * 2) > data.Length
            || battlePointOffset + (SmBattlePointLengths.Sum() * 4) > data.Length)
            throw new WorkspaceException("No encuentro las tablas de tiendas de Sol/Luna en Shop.cro.");
        return new LayoutInfo(regularOffset, battlePointOffset, SmRegularLengths, SmBattlePointLengths, SmRegularNames, SmBattlePointNames);
    }

    private static int Find(byte[] data, byte[] pattern, int start) => data.AsSpan(start).IndexOf(pattern) is var found && found >= 0 ? start + found : -1;

    private static string RequireCro(GameWorkspace workspace)
    {
        var path = Path.Combine(workspace.RomFsPath, CroFile);
        if (!File.Exists(path))
            throw new WorkspaceException($"Falta {CroFile} en el RomFS. Es necesario para editar tiendas.");
        return path;
    }

    private static void EnsureSupported(GameConfig config)
    {
        if (config.Generation != 7)
            throw new WorkspaceException("El editor de tiendas está disponible para Gen. VII.");
    }

    private sealed record LayoutInfo(int RegularOffset, int BattlePointOffset, int[] RegularLengths, int[] BattlePointLengths, string[] RegularNames, string[] BattlePointNames);
}
