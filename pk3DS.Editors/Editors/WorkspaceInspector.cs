namespace pk3DS.Editors;

/// <summary>Reports what the selected folder is and which modules its contents can support.</summary>
public static class WorkspaceInspector
{
    public static InspectResponse Inspect(WorkspaceRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        return new InspectResponse(
            workspace.RomFsPath,
            workspace.Version.ToString(),
            true,
            workspace.TitleId,
            workspace.ExeFsPath,
            workspace.ExheaderPath,
            GetModuleAvailability(workspace),
            "El origen solo se leerá. La salida será un ZIP LayeredFS para Luma.");
    }

    private static ModuleAvailability[] GetModuleAvailability(GameWorkspace workspace) =>
    [
        new("personal", "Personal Stats", "RomFS", true, "RomFS"),
        new("evolutions", "Evolutions", "RomFS", true, "RomFS"),
        new("levelup", "Level Up Moves", "RomFS", true, "RomFS"),
        new("eggmove", "Egg Moves", "RomFS", true, "RomFS"),
        new("wild", "Wild Encounters", "RomFS", true, "RomFS"),
        new("trainers", "Trainers", "RomFS", true, "RomFS"),
        new("moves", "Move Stats", "RomFS", true, "RomFS"),
        new("items", "Item Stats", "RomFS", true, "RomFS"),
        new("tm", "TMs / HMs", "ExeFS", workspace.HasExeFs, "ExeFS"),
        new("marts", "Poké Mart", "ExeFS/CRO", workspace.HasExeFs, "ExeFS o CRO según el juego"),
        new("starter", "Starter Pokémon", "CRO", workspace.HasExeFs, "Workspace completo y CRO"),
        new("typechart", "Type Chart", "CRO", workspace.HasExeFs, "Workspace completo y CRO"),
    ];
}
