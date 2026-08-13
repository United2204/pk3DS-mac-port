using pk3DS.Core;

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
        new("pickup", "Pickup Gen. VI/VII", "RomFS + ExeFS", (workspace.Version is GameVersion.SM or GameVersion.USUM) || (workspace.HasExeFs && workspace.Version is (GameVersion.XY or GameVersion.ORAS)), "Gen. VI: ExeFS; Gen. VII: RomFS"),
        new("maison", "Battle Maison / Tree / Royal", "RomFS", workspace.Version is GameVersion.XY or GameVersion.ORAS or GameVersion.SM or GameVersion.USUM, "RomFS Gen. VI/VII"),
        new("tm", "TMs / HMs", "ExeFS", workspace.HasExeFs, "ExeFS code.bin descomprimido"),
        new("shiny-rate", "Shiny Rate", "ExeFS", workspace.HasExeFs, "ExeFS code.bin descomprimido"),
        new("tutors", "Move Tutors Gen. VII", "RomFS", workspace.Version is GameVersion.SM or GameVersion.USUM && File.Exists(Path.Combine(workspace.RomFsPath, "Shop.cro")), "Shop.cro"),
        new("marts", "Poké Mart Gen. VII", "RomFS", workspace.Version is GameVersion.SM or GameVersion.USUM && File.Exists(Path.Combine(workspace.RomFsPath, "Shop.cro")), "Shop.cro"),
        new("tutors6", "Move Tutors Gen. VI", "ExeFS", workspace.Version is GameVersion.XY or GameVersion.ORAS && workspace.HasExeFs, "ExeFS code.bin descomprimido"),
        new("marts6", "Poké Mart Gen. VI", "ExeFS", workspace.Version is GameVersion.XY or GameVersion.ORAS && workspace.HasExeFs, "ExeFS code.bin descomprimido"),
        new("opowers", "O-Powers Gen. VI", "ExeFS", workspace.Version is GameVersion.XY or GameVersion.ORAS && workspace.HasExeFs, "ExeFS code.bin descomprimido"),
        new("starter", "Starter Pokémon Gen. VI", "RomFS", workspace.Version is GameVersion.XY or GameVersion.ORAS
            && File.Exists(Path.Combine(workspace.RomFsPath, "DllPoke3Select.cro"))
            && File.Exists(Path.Combine(workspace.RomFsPath, "DllField.cro")), "DllPoke3Select.cro + DllField.cro"),
        new("gift6", "Gift Pokémon Gen. VI", "RomFS", workspace.Version is GameVersion.XY or GameVersion.ORAS
            && File.Exists(Path.Combine(workspace.RomFsPath, "DllField.cro")), "DllField.cro"),
        new("typechart", "Type Chart", "RomFS + ExeFS", workspace.Version is GameVersion.XY or GameVersion.ORAS
            ? File.Exists(Path.Combine(workspace.RomFsPath, "DllBattle.cro"))
            : workspace.Version is GameVersion.SM or GameVersion.USUM && workspace.HasExeFs,
            "Gen. VI: DllBattle.cro; Gen. VII: ExeFS code.bin"),
    ];
}
