using pk3DS.Core;
using pk3DS.Core.CTR;

namespace pk3DS.Editors;

/// <summary>Reports what the selected folder is and which modules its contents can support.</summary>
public static class WorkspaceInspector
{
    public static InspectResponse Inspect(WorkspaceRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var code = InspectCodeBin(workspace);
        var smdhReady = HasSmdh(workspace);
        var diagnostics = GetDiagnostics(workspace, code);
        return new InspectResponse(
            workspace.RomFsPath,
            workspace.Version.ToString(),
            true,
            workspace.TitleId,
            workspace.ExeFsPath,
            workspace.ExheaderPath,
            GetModuleAvailability(workspace, code.Ready, smdhReady),
            "El origen solo se leerá. La salida será un ZIP LayeredFS para Luma.")
        {
            CodeBinPath = code.Path,
            CodeBinBytes = code.Bytes,
            CodeBinReady = code.Ready,
            CodeBinCompressed = code.Compressed,
            SmdhReady = smdhReady,
            Diagnostics = diagnostics,
        };
    }

    private static ModuleAvailability[] GetModuleAvailability(GameWorkspace workspace, bool codeReady, bool smdhReady) =>
    [
        new("smdh", "Icono SMDH", "ExeFS", smdhReady, "ExeFS con icon.bin SMDH válido"),
        new("personal", "Personal Stats", "RomFS", HasGarc(workspace, "personal"), "GARC personal"),
        new("evolutions", "Evolutions", "RomFS", HasGarc(workspace, "evolution"), "GARC evolution"),
        new("levelup", "Level Up Moves", "RomFS", HasGarc(workspace, "levelup"), "GARC levelup"),
        new("eggmove", "Egg Moves", "RomFS", HasGarc(workspace, "eggmove"), "GARC eggmove"),
        new("wild", "Wild Encounters", "RomFS", workspace.Version is GameVersion.XY or GameVersion.ORAS
            ? HasGarc(workspace, "encdata")
            : HasAllGarcs(workspace, "encdata", "zonedata", "worlddata"),
            workspace.Version is GameVersion.XY or GameVersion.ORAS
                ? "GARC encdata"
                : "GARC encdata + zonedata + worlddata"),
        new("trainers", "Trainers", "RomFS", HasAllGarcs(workspace, "trclass", "trdata", "trpoke", "gametext"), "GARC trclass + trdata + trpoke + gametext"),
        new("moves", "Move Stats", "RomFS", HasGarc(workspace, "move"), "GARC move"),
        new("items", "Item Stats", "RomFS", HasGarc(workspace, "item"), "GARC item"),
        new("pickup", "Pickup Gen. VII", "RomFS", (workspace.Version is GameVersion.SM or GameVersion.USUM) && HasGarc(workspace, "pickup"), "GARC pickup Gen. VII"),
        new("pickup6", "Pickup Gen. VI", "ExeFS", (workspace.Version is GameVersion.XY or GameVersion.ORAS) && codeReady, "ExeFS con code.bin válido y alineado"),
        new("maison", "Battle Maison / Tree / Royal", "RomFS", HasAllGarcs(workspace, "maisonpkN", "maisontrN", "maisonpkS", "maisontrS"), "GARC maison normal/super"),
        new("static", "Static Encounters", "RomFS",
            workspace.Version is GameVersion.XY or GameVersion.ORAS
                ? HasFile(workspace.RomFsPath, "DllField.cro")
                : workspace.Version is GameVersion.SM or GameVersion.USUM
                    && HasGarc(workspace.RomFsPath, workspace.Version is GameVersion.SM ? 155 : 159),
            workspace.Version is GameVersion.XY or GameVersion.ORAS
                ? "DllField.cro"
                : "GARC encounterstatic"),
        new("text", "Game / Story Text", "RomFS", HasAllGarcs(workspace, "gametext", "storytext"), "GARC gametext + storytext"),
        new("mega", "Mega Evolutions", "RomFS", HasGarc(workspace, "megaevo"), "GARC megaevo"),
        new("owse", "OWSE / Scripts", "RomFS", workspace.Version is GameVersion.XY or GameVersion.ORAS
            ? HasGarc(workspace, "encdata")
            : HasAllGarcs(workspace, "encdata", "zonedata", "worlddata"),
            workspace.Version is GameVersion.XY or GameVersion.ORAS
                ? "GARC encdata"
                : "GARC encdata + zonedata + worlddata"),
        new("tm", "TMs / HMs", "ExeFS", codeReady, "ExeFS con code.bin válido y alineado"),
        new("shiny-rate", "Shiny Rate", "ExeFS", codeReady, "ExeFS con code.bin válido y alineado"),
        new("tutors", "Move Tutors Gen. VII", "RomFS", workspace.Version is GameVersion.SM or GameVersion.USUM && HasFile(workspace.RomFsPath, "Shop.cro"), "Shop.cro"),
        new("marts", "Poké Mart Gen. VII", "RomFS", workspace.Version is GameVersion.SM or GameVersion.USUM && HasFile(workspace.RomFsPath, "Shop.cro"), "Shop.cro"),
        new("tutors6", "Move Tutors Gen. VI", "ExeFS", (workspace.Version is GameVersion.XY or GameVersion.ORAS) && codeReady, "ExeFS con code.bin válido y alineado"),
        new("marts6", "Poké Mart Gen. VI", "ExeFS", (workspace.Version is GameVersion.XY or GameVersion.ORAS) && codeReady, "ExeFS con code.bin válido y alineado"),
        new("opowers", "O-Powers Gen. VI", "ExeFS", (workspace.Version is GameVersion.XY or GameVersion.ORAS) && codeReady, "ExeFS con code.bin válido y alineado"),
        new("starter", "Starter Pokémon Gen. VI", "RomFS", workspace.Version is GameVersion.XY or GameVersion.ORAS
            && HasFile(workspace.RomFsPath, "DllPoke3Select.cro")
            && HasFile(workspace.RomFsPath, "DllField.cro"), "DllPoke3Select.cro + DllField.cro"),
        new("gift6", "Gift Pokémon Gen. VI", "RomFS", workspace.Version is GameVersion.XY or GameVersion.ORAS
            && HasFile(workspace.RomFsPath, "DllField.cro"), "DllField.cro"),
        new("typechart", "Type Chart", "RomFS + ExeFS", workspace.Version is GameVersion.XY or GameVersion.ORAS
            ? HasFile(workspace.RomFsPath, "DllBattle.cro")
            : workspace.Version is GameVersion.SM or GameVersion.USUM && codeReady,
            "Gen. VI: DllBattle.cro; Gen. VII: ExeFS code.bin"),
    ];

    private static CodeBinStatus InspectCodeBin(GameWorkspace workspace)
    {
        if (workspace.ExeFsPath is null)
            return new(null, null, false, false, "No se encontró ExeFS.");

        var path = Directory.EnumerateFiles(workspace.ExeFsPath, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(file => Path.GetFileName(file).Contains("code", StringComparison.OrdinalIgnoreCase));
        if (path is null)
            return new(null, null, false, false, "No se encontró code.bin dentro de ExeFS.");

        try
        {
            var source = File.ReadAllBytes(path);
            var compressed = BLZCoder.TryDecode(source, out var decoded);
            var effective = compressed ? decoded : source;
            var ready = effective.Length > 0 && effective.Length % 0x200 == 0;
            return new(path, source.LongLength, ready, compressed, ready
                ? null
                : "code.bin no queda alineado a 0x200 bytes después de descomprimirlo.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException)
        {
            return new(path, null, false, false, $"No se pudo leer code.bin: {ex.Message}");
        }
    }

    private static bool HasGarc(string romFsPath, int fileNumber)
    {
        var relative = Path.Combine("a",
            (fileNumber / 100 % 10).ToString(),
            (fileNumber / 10 % 10).ToString(),
            (fileNumber % 10).ToString());
        var path = Path.Combine(romFsPath, relative);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private static bool HasFile(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private static bool HasSmdh(GameWorkspace workspace)
    {
        if (workspace.ExeFsPath is null)
            return false;
        var path = Directory.EnumerateFiles(workspace.ExeFsPath, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(file => string.Equals(Path.GetFileName(file), "icon.bin", StringComparison.OrdinalIgnoreCase));
        if (path is null || new FileInfo(path).Length == 0)
            return false;
        try
        {
            SMDHPortable.Read(File.ReadAllBytes(path));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException)
        {
            return false;
        }
    }

    private static bool HasGarc(GameWorkspace workspace, string name)
    {
        var references = GetGarcReferences(workspace.Version)
            .Where(reference => string.Equals(reference.Name, name, StringComparison.Ordinal));
        foreach (var reference in references)
        {
            if (!reference.LanguageVariant && HasGarc(workspace.RomFsPath, reference.FileNumber))
                return true;

            if (reference.LanguageVariant && Enumerable.Range(0, 12)
                .Select(offset => reference.GetRelativeGARC(offset).FileNumber)
                .Any(fileNumber => HasGarc(workspace.RomFsPath, fileNumber)))
                return true;
        }
        return false;
    }

    private static bool HasAllGarcs(GameWorkspace workspace, params string[] names) =>
        names.All(name => HasGarc(workspace, name));

    private static GARCReference[] GetGarcReferences(GameVersion version) => version switch
    {
        GameVersion.XY => GARCReference.GARCReference_XY,
        GameVersion.ORAS => GARCReference.GARCReference_AO,
        GameVersion.SM => [.. GARCReference.GARCReference_SN, .. GARCReference.GARCReference_MN],
        GameVersion.USUM => [.. GARCReference.GARCReference_US, .. GARCReference.GARCReference_UM],
        _ => [],
    };

    private static InspectDiagnostic[] GetDiagnostics(GameWorkspace workspace, CodeBinStatus code)
    {
        var diagnostics = new List<InspectDiagnostic>
        {
            new("success", "romfs", $"RomFS válida y reconocida como {workspace.Version}.")
        };

        if (workspace.ExheaderPath is null)
            diagnostics.Add(new("warning", "exheader", "Falta exheader.bin: podrás inspeccionar RomFS, pero no exportar LayeredFS ni reconstruir una ROM con Title ID detectado."));
        else
            diagnostics.Add(new("success", "exheader", "exheader.bin válido; el Title ID fue detectado."));

        if (workspace.ExeFsPath is not null && !HasSmdh(workspace))
            diagnostics.Add(new("warning", "smdh", "Falta ExeFS/icon.bin: el editor de icono SMDH quedará deshabilitado."));

        if (code.Path is null)
            diagnostics.Add(new("warning", "code-bin", code.Message ?? "No hay code.bin utilizable; las funciones ExeFS quedarán deshabilitadas."));
        else if (!code.Ready)
            diagnostics.Add(new("error", "code-bin", code.Message ?? "code.bin no es utilizable."));
        else if (code.Compressed)
            diagnostics.Add(new("info", "code-bin", $"code.bin está comprimido con BLZ ({code.Bytes:N0} bytes); la app lo descomprime en memoria y conserva intacto el origen."));
        else
            diagnostics.Add(new("success", "code-bin", $"code.bin listo y alineado a 0x200 bytes ({code.Bytes:N0} bytes)."));

        return diagnostics.ToArray();
    }

    private sealed record CodeBinStatus(
        string? Path,
        long? Bytes,
        bool Ready,
        bool Compressed,
        string? Message);
}
