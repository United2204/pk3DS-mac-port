using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>
/// Static encounters for X/Y and OR/AS. Unlike Gen VII these live inside <c>DllField.cro</c> at a
/// hard-coded offset, so editing them needs Luma's RO patch to take effect on console.
/// </summary>
public static class StaticGen6Editor
{
    private const string CroFile = "DllField.cro";
    private const int EntrySize = 0xC;

    public static StaticGen6CatalogResponse GetCatalog(StaticGen6CatalogRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        RequireCro(workspace);
        var config = EditorSession.OpenReadOnly(workspace, request.Language);
        Guard.Gen6(config);
        return new StaticGen6CatalogResponse(config.ORAS ? "ORAS" : "XY", GetCount(config.ORAS),
            Catalogs.Species(config), Catalogs.Items(config),
            "Este cambio usa DllField.cro. En consola requiere el parche RO de Luma para evitar fallos.");
    }

    public static StaticGen6EntryResponse GetEntry(StaticGen6EntryRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        RequireCro(workspace);
        var config = EditorSession.OpenReadOnly(workspace, request.Language);
        Guard.Gen6(config);
        var data = File.ReadAllBytes(Path.Combine(workspace.RomFsPath, CroFile));
        return new StaticGen6EntryResponse(request.EntryIndex, Read(data, config.ORAS, request.EntryIndex));
    }

    public static ExportResult Export(StaticGen6ExportRequest request)
    {
        RequireCro(GameWorkspace.Open(request.WorkspacePath));
        return EditorSession.ExportLooseFiles(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "static6", [CroFile], (_, config, scratchRomFs) =>
            {
                Guard.Gen6(config);
                var path = Path.Combine(scratchRomFs, CroFile);
                var data = File.ReadAllBytes(path);
                var entry = Validate(request.Entry, Catalogs.SpeciesCount(config), Catalogs.ItemCount(config));
                var offset = GetOffset(config.ORAS, request.EntryIndex);
                if (offset + EntrySize > data.Length)
                    throw new WorkspaceException("La entrada de encuentro estático no existe.");

                var encounter = new EncounterStatic6(data.Skip(offset).Take(EntrySize).ToArray())
                {
                    Species = (ushort)entry.Species,
                    Form = (byte)entry.Form,
                    Level = (byte)entry.Level,
                    HeldItem = entry.HeldItem,
                    Gender = entry.Gender,
                    Ability = entry.Ability,
                    ShinyLock = entry.ShinyLock,
                    IV3 = entry.IV3,
                };
                Array.Copy(encounter.Write(), 0, data, offset, EntrySize);
                File.WriteAllBytes(path, data);
            });
    }

    private static void RequireCro(GameWorkspace workspace)
    {
        if (!File.Exists(Path.Combine(workspace.RomFsPath, CroFile)))
            throw new WorkspaceException($"Falta {CroFile} en el RomFS. Es necesario para editar estáticos de Gen. VI.");
    }

    internal static int GetCount(bool oras) => oras ? 0x3B : 0xD;

    internal static int GetOffset(bool oras, int entryIndex)
    {
        if (entryIndex < 0 || entryIndex >= GetCount(oras))
            throw new WorkspaceException("La entrada de encuentro estático no existe.");
        return (oras ? 0xF1B20 : 0xEE46C) + (entryIndex * EntrySize);
    }

    internal static StaticGen6Entry Read(byte[] data, bool oras, int entryIndex)
    {
        var offset = GetOffset(oras, entryIndex);
        if (offset + EntrySize > data.Length)
            throw new WorkspaceException($"{CroFile} no tiene el tamaño esperado.");
        var entry = new EncounterStatic6(data.Skip(offset).Take(EntrySize).ToArray());
        return new StaticGen6Entry(entry.Species, entry.Form, entry.Level, entry.HeldItem, entry.Gender, entry.Ability, entry.ShinyLock, entry.IV3);
    }

    /// <summary>Returns the validated entry so callers get a non-null value to apply.</summary>
    internal static StaticGen6Entry Validate(StaticGen6Entry? entry, int speciesCount, int itemCount)
    {
        if (entry is null
            || entry.Species < 0 || entry.Species >= speciesCount
            || entry.Form is < 0 or > byte.MaxValue
            || entry.Level is < 1 or > 100
            || entry.HeldItem < 0 || entry.HeldItem >= itemCount
            || entry.Gender is < 0 or > 3
            || entry.Ability is < 0 or > 7)
            throw new WorkspaceException("La especie, forma, nivel, objeto o flags no son válidos.");
        return entry;
    }
}
