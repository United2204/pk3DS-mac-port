using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>Gen VI Poké Mart inventories stored in ExeFS code.bin.</summary>
public static class MartGen6Editor
{
    private const int SearchStart = 0x400000;
    private const int Alignment = 0x200;
    private static readonly byte[] VanillaSignature =
    [
        0x00, 0x72, 0x6F, 0x6D, 0x3A, 0x2F, 0x44, 0x6C, 0x6C, 0x53, 0x74, 0x61, 0x72, 0x74, 0x4D, 0x65,
        0x6E, 0x75, 0x2E, 0x63, 0x72, 0x6F, 0x00,
    ];
    private static readonly byte[] PatchedSignature =
    [
        0x00, 0x72, 0x6F, 0x6D, 0x32, 0x3A, 0x2F, 0x44, 0x6C, 0x6C, 0x53, 0x74, 0x61, 0x72, 0x74, 0x4D,
        0x65, 0x6E, 0x75, 0x2E, 0x63, 0x72, 0x6F, 0x00, 0xFF,
    ];
    private static readonly int[] XyLengths = [2, 11, 14, 17, 18, 19, 19, 19, 19, 1, 4, 10, 3, 9, 1, 1, 3, 3, 5, 5, 6, 7, 5, 5, 8, 3];
    private static readonly int[] OrasLengths = [3, 10, 14, 17, 18, 19, 19, 19, 19, 1, 9, 6, 4, 3, 8, 8, 3, 3, 4, 3, 6, 8, 7, 4];
    private static readonly string[] XyNames =
    [
        "No Gym Badges", "1 Gym Badge", "2 Gym Badges", "3 Gym Badges", "4 Gym Badges", "5 Gym Badges", "6 Gym Badges", "7 Gym Badges", "8 Gym Badges", "Unused",
        "Lumiose City [Herboriste]", "Lumiose City [Poké Ball Boutique]", "Lumiose City [Stone Emporium]", "Coumarine City [Incenses]", "Aquacorde Town [Poké Ball]", "Aquacorde Town [Potion]",
        "Lumiose City North Boulevard [Poké Balls]", "Cyllage City [Poké Balls]", "Shalour City [TMs]", "Lumiose City South Boulevard [TMs]", "Laverre City [Vitamins]", "Snowbelle City [Poké Balls]",
        "Kiloude City [TMs]", "Anistar City [TMs]", "Santalune City [X Items]", "Coumarine City [Poké Balls]",
    ];
    private static readonly string[] OrasNames =
    [
        "No Gym Badges [After Pokédex]", "1 Gym Badge", "2 Gym Badges", "3 Gym Badges", "4 Gym Badges", "5 Gym Badges", "6 Gym Badges", "7 Gym Badges", "8 Gym Badges",
        "No Gym Badges [Before Pokédex]", "Slateport Market [Incenses]", "Slateport Market [Vitamins]", "Slateport Market [TMs]", "Rustboro City [Poké Balls]", "Slateport City [X Items]",
        "Mauville City [TMs]", "Verdanturf Town [Poké Balls]", "Fallarbor Town [Poké Balls]", "Lavaridge Town [Herbs]", "Lilycove Dept Store, 2F Left [Run Away Items]",
        "Lilycove Dept Store, 3F Left [Vitamins]", "Lilycove Dept Store, 3F Right [X Items]", "Lilycove Dept Store, 4F Left [Offensive TMs]", "Lilycove Dept Store, 4F Right [Defensive TMs]",
    ];

    public static MartTableResponse GetTable(MartTableRequest request)
    {
        var (workspace, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        EnsureSupported(config);
        var regular = Read(ReadCode(workspace), config);
        return new MartTableResponse(config.Version.ToString(), regular, [], Catalogs.Items(config),
            "Gen. VI modifica las tiendas normales en code.bin. Si está BLZ comprimido, se normaliza en memoria; la salida es un parche ExeFS para Luma.");
    }

    public static ExportResult Export(MartExportRequest request) =>
        EditorSession.ExportExeFs(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "marts6", (_, config, code) =>
            {
                EnsureSupported(config);
                if (request.BattlePoints is { Length: > 0 })
                    throw new WorkspaceException("Gen. VI no tiene inventarios de Battle Points en este formato.");
                var regular = Validate(request.Regular, Catalogs.ItemCount(config), config);
                Write(code, config, regular);
                return code;
            });

    internal static MartGroup[] Read(byte[] code, GameConfig config)
    {
        var layout = Layout(code, config);
        var groups = new MartGroup[layout.Lengths.Length];
        var offset = layout.Offset;
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var entries = new MartEntry[layout.Lengths[groupIndex]];
            for (var index = 0; index < entries.Length; index++)
                entries[index] = new MartEntry(BitConverter.ToUInt16(code, offset + (index * sizeof(ushort))));
            offset += entries.Length * sizeof(ushort);
            groups[groupIndex] = new MartGroup(layout.Names[groupIndex], entries);
        }
        return groups;
    }

    internal static MartGroup[] Validate(MartGroup[]? groups, int itemCount, GameConfig config)
    {
        var lengths = config.ORAS ? OrasLengths : XyLengths;
        if (groups is null || groups.Length != lengths.Length)
            throw new WorkspaceException("La tabla de tiendas Gen. VI no conserva la cantidad de inventarios original.");
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var entries = groups[groupIndex]?.Entries;
            if (entries is null || entries.Length != lengths[groupIndex])
                throw new WorkspaceException("La tabla de tiendas Gen. VI no conserva la cantidad de objetos original.");
            if (entries.Any(entry => entry.Item < 0 || entry.Item >= itemCount || entry.Price is not null))
                throw new WorkspaceException("La tabla de tiendas Gen. VI contiene objetos o precios inválidos.");
        }
        return groups;
    }

    internal static int FindDataOffset(byte[] code, GameConfig config)
    {
        var layout = Layout(code, config);
        return layout.Offset;
    }

    private static void Write(byte[] code, GameConfig config, MartGroup[] groups)
    {
        var offset = FindDataOffset(code, config);
        foreach (var group in groups)
        foreach (var entry in group.Entries)
        {
            BitConverter.GetBytes((ushort)entry.Item).CopyTo(code, offset);
            offset += sizeof(ushort);
        }
    }

    private static LayoutInfo Layout(byte[] code, GameConfig config)
    {
        if (code is null || code.Length == 0 || code.Length % Alignment != 0)
            throw new WorkspaceException("El code.bin debe estar descomprimido y alineado a 0x200 bytes.");
        var start = Math.Min(SearchStart, code.Length);
        var found = code.AsSpan(start).IndexOf(VanillaSignature);
        var signatureLength = VanillaSignature.Length;
        if (found < 0)
        {
            found = code.AsSpan(start).IndexOf(PatchedSignature);
            signatureLength = PatchedSignature.Length;
        }
        var offset = found < 0 ? -1 : start + found + signatureLength;
        var lengths = config.ORAS ? OrasLengths : XyLengths;
        var names = config.ORAS ? OrasNames : XyNames;
        var required = lengths.Sum() * sizeof(ushort);
        if (offset < 0 || names.Length != lengths.Length || offset + required > code.Length)
            throw new WorkspaceException("No encuentro los inventarios de tiendas completos en code.bin.");
        return new LayoutInfo(offset, lengths, names);
    }

    private static byte[] ReadCode(GameWorkspace workspace)
        => EditorSession.ReadCode(workspace);

    private static void EnsureSupported(GameConfig config)
    {
        if (config.Generation != 6 || (!config.XY && !config.ORAS))
            throw new WorkspaceException("El editor de tiendas está disponible solo para X/Y y OR/AS.");
    }

    private sealed record LayoutInfo(int Offset, int[] Lengths, string[] Names);
}
