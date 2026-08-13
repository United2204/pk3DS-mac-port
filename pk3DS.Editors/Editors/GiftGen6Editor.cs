using pk3DS.Core;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>Gift Pokémon for X/Y and OR/AS, stored in the gift table inside DllField.cro.</summary>
public static class GiftGen6Editor
{
    private const string CroFile = "DllField.cro";
    private const int XyOffset = 0xF805C;
    private const int OrasOffset = 0xF906C;
    private const int XySize = 0x18;
    private const int OrasSize = 0x24;
    private const int XyCount = 0x13;
    private const int OrasCount = 0x25;

    public static GiftGen6CatalogResponse GetCatalog(GiftGen6CatalogRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        RequireCro(workspace);
        var config = EditorSession.OpenReadOnly(workspace, request.Language);
        EnsureSupported(config);
        return new GiftGen6CatalogResponse(config.Version.ToString(), GetCount(config.ORAS),
            Catalogs.Species(config), Catalogs.Items(config), Catalogs.Natures(config),
            "Los regalos se leen y escriben en DllField.cro. La salida requiere el parche RO de Luma para usarse en consola.");
    }

    public static GiftGen6EntryResponse GetEntry(GiftGen6EntryRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        RequireCro(workspace);
        var config = EditorSession.OpenReadOnly(workspace, request.Language);
        EnsureSupported(config);
        var data = File.ReadAllBytes(Path.Combine(workspace.RomFsPath, CroFile));
        return new GiftGen6EntryResponse(request.EntryIndex, Read(data, config.ORAS, request.EntryIndex));
    }

    public static ExportResult Export(GiftGen6ExportRequest request)
    {
        RequireCro(GameWorkspace.Open(request.WorkspacePath));
        return EditorSession.ExportLooseFiles(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "gift6", [CroFile], (_, config, scratchRomFs) =>
            {
                EnsureSupported(config);
                var entry = Validate(request.Entry, Catalogs.SpeciesCount(config), Catalogs.ItemCount(config), Catalogs.Natures(config).Length);
                var path = Path.Combine(scratchRomFs, CroFile);
                var data = File.ReadAllBytes(path);
                var offset = GetOffset(config.ORAS, request.EntryIndex);
                var size = GetSize(config.ORAS);
                if (offset + size > data.Length)
                    throw new WorkspaceException($"{CroFile} no contiene la tabla de regalos completa.");

                var gift = new EncounterGift6(data.Skip(offset).Take(size).ToArray(), config.ORAS)
                {
                    Species = (ushort)entry.Species,
                    Form = (byte)entry.Form,
                    Level = (byte)entry.Level,
                    HeldItem = entry.HeldItem,
                    Gender = (sbyte)entry.Gender,
                    Ability = (sbyte)entry.Ability,
                    Nature = (sbyte)entry.Nature,
                    ShinyLock = entry.ShinyLock,
                    IVs = entry.IVs.Select(value => (sbyte)value).ToArray(),
                };
                Array.Copy(gift.Write(), 0, data, offset, size);
                File.WriteAllBytes(path, data);
            });
    }

    internal static int GetCount(bool oras) => oras ? OrasCount : XyCount;
    internal static int GetSize(bool oras) => oras ? OrasSize : XySize;

    internal static int GetOffset(bool oras, int entryIndex)
    {
        if (entryIndex < 0 || entryIndex >= GetCount(oras))
            throw new WorkspaceException("La entrada de regalo no existe.");
        return (oras ? OrasOffset : XyOffset) + (entryIndex * GetSize(oras));
    }

    internal static GiftGen6Entry Read(byte[] data, bool oras, int entryIndex)
    {
        var offset = GetOffset(oras, entryIndex);
        var size = GetSize(oras);
        if (data is null || offset + size > data.Length)
            throw new WorkspaceException($"{CroFile} no contiene la tabla de regalos completa.");
        var gift = new EncounterGift6(data.Skip(offset).Take(size).ToArray(), oras);
        return new GiftGen6Entry(gift.Species, gift.Form, gift.Level, gift.HeldItem, gift.Gender, gift.Ability,
            gift.Nature, gift.ShinyLock, gift.IVs.Select(value => (int)value).ToArray());
    }

    internal static GiftGen6Entry Validate(GiftGen6Entry? entry, int speciesCount, int itemCount, int natureCount)
    {
        if (entry is null
            || entry.Species < 0 || entry.Species >= speciesCount
            || entry.Form is < 0 or > byte.MaxValue
            || entry.Level is < 0 or > 100
            || (entry.HeldItem < -1 || entry.HeldItem >= itemCount)
            || entry.Gender is < 0 or > 2
            || entry.Ability is < -1 or > 3
            || entry.Nature is < -1 || entry.Nature >= natureCount
            || entry.IVs is null || entry.IVs.Length != 6 || entry.IVs.Any(value => value is < -1 or > 31))
            throw new WorkspaceException("La especie, forma, nivel, objeto, naturaleza, género, habilidad o IVs no son válidos.");
        return entry;
    }

    private static void RequireCro(GameWorkspace workspace)
    {
        if (!File.Exists(Path.Combine(workspace.RomFsPath, CroFile)))
            throw new WorkspaceException($"Falta {CroFile} en el RomFS. Es necesario para editar regalos de Gen. VI.");
    }

    private static void EnsureSupported(GameConfig config)
    {
        if (config.Generation != 6 || (!config.XY && !config.ORAS))
            throw new WorkspaceException("El editor de regalos está disponible solo para X/Y y OR/AS.");
    }
}
