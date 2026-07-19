using System.IO.Compression;
using System.Diagnostics;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Randomizers;
using pk3DS.Core.Structures;

namespace pk3DS.Mac.Web;

public sealed class WorkspaceService
{
    private static readonly string[] RequiredGarcs = ["personal", "levelup", "gametext", "move", "evolution", "eggmove"];

    public InspectResponse Inspect(WorkspaceRequest request)
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

    public PickFolderResponse PickFolder(string prompt)
    {
        if (!OperatingSystem.IsMacOS())
            throw new WorkspaceException("El selector de carpetas solo está disponible en macOS.");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "-e", $"POSIX path of (choose folder with prompt \"{prompt}\")" },
        });
        if (process is null)
            throw new WorkspaceException("No pude abrir el selector de carpetas.");

        var path = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(path))
        {
            if (error.Contains("User canceled", StringComparison.OrdinalIgnoreCase))
                throw new WorkspaceException("No se seleccionó ninguna carpeta.");
            throw new WorkspaceException("No pude abrir el selector de carpetas.");
        }
        return new PickFolderResponse(path);
    }

    public TextCatalogResponse GetTextCatalog(TextCatalogRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var config = InitializeReadOnlyConfig(workspace, GetLanguage(request.Language));
        var garc = GetTextGarc(config, request.Kind);
        var knownNames = config.GameText.ToDictionary(reference => reference.Index, reference => reference.Name.ToString());
        var tables = garc.Files.Select((_, index) => new TextTableSummary(
            index,
            knownNames.GetValueOrDefault(index, $"Tabla {index:000}"),
            new TextFile(config, garc.Files[index], remapChars: true).Lines.Length)).ToArray();
        return new TextCatalogResponse(workspace.Version.ToString(), request.Kind, tables);
    }

    public TextTableResponse GetTextTable(TextTableRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var config = InitializeReadOnlyConfig(workspace, GetLanguage(request.Language));
        var garc = GetTextGarc(config, request.Kind);
        if (request.TableIndex < 0 || request.TableIndex >= garc.Files.Length)
            throw new WorkspaceException("La tabla de texto indicada no existe.");
        var lines = new TextFile(config, garc.Files[request.TableIndex], remapChars: true).Lines;
        return new TextTableResponse(request.Kind, request.TableIndex, lines);
    }

    public RandomizeResponse ExportText(TextExportRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        if (request.Lines is null)
            throw new WorkspaceException("No hay líneas de texto para exportar.");
        var titleId = request.TitleId ?? workspace.TitleId;
        if (string.IsNullOrWhiteSpace(titleId) || titleId.Length != 16 || titleId.Any(c => !Uri.IsHexDigit(c)))
            throw new WorkspaceException("No pude detectar un Title ID válido. Seleccioná la carpeta completa que contiene exheader.bin.");

        var language = GetLanguage(request.Language);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"pk3ds-mac-text-{Guid.NewGuid():N}");
        var temporaryRomFs = Path.Combine(temporaryRoot, "romfs");
        try
        {
            Directory.CreateDirectory(temporaryRomFs);
            var probe = new GameConfig(workspace.Version);
            probe.Initialize(workspace.RomFsPath, temporaryRoot, language);
            foreach (var name in RequiredGarcs)
                CopyRelativeFile(workspace.RomFsPath, temporaryRomFs, probe.GetGARCFileName(name));
            if (request.Kind == TextArchiveKind.Story)
                CopyRelativeFile(workspace.RomFsPath, temporaryRomFs, probe.GetGARCFileName("storytext"));

            var config = new GameConfig(workspace.Version);
            config.Initialize(temporaryRomFs, temporaryRoot, language);
            var garc = GetTextGarc(config, request.Kind);
            if (request.TableIndex < 0 || request.TableIndex >= garc.Files.Length)
                throw new WorkspaceException("La tabla de texto indicada no existe.");

            var text = new TextFile(config, garc.Files[request.TableIndex], remapChars: true) { Lines = request.Lines };
            garc.Files[request.TableIndex] = text.Data;
            garc.Save();

            var fileName = config.GetGARCFileName(request.Kind == TextArchiveKind.Story ? "storytext" : "gametext");
            return CreateLayeredFsArchive(request.OutputDirectory, workspace.RomFsPath, temporaryRomFs, titleId, [fileName], "text");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    public LearnsetCatalogResponse GetLearnsetCatalog(LearnsetCatalogRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var config = InitializeReadOnlyConfig(workspace, GetLanguage(request.Language));
        var names = config.GetText(TextName.SpeciesNames);
        var species = config.Learnsets.Select((set, index) => new LearnsetSpeciesSummary(index,
            index < names.Length && !string.IsNullOrWhiteSpace(names[index]) ? names[index] : $"Forma {index:000}", set.Count)).ToArray();
        var moves = config.GetText(TextName.MoveNames).Select((name, index) => new NamedEntry(index, name)).Where(entry => entry.Id > 0 && !string.IsNullOrWhiteSpace(entry.Name)).ToArray();
        return new LearnsetCatalogResponse(workspace.Version.ToString(), species, moves);
    }

    public LearnsetTableResponse GetLearnset(LearnsetTableRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var config = InitializeReadOnlyConfig(workspace, GetLanguage(request.Language));
        if (request.SpeciesIndex < 0 || request.SpeciesIndex >= config.Learnsets.Length)
            throw new WorkspaceException("La especie indicada no existe.");
        var set = config.Learnsets[request.SpeciesIndex];
        return new LearnsetTableResponse(request.SpeciesIndex, set.Moves.Select((move, index) => new LearnsetEntry(Math.Clamp(set.Levels[index], 1, 100), move)).ToArray());
    }

    public RandomizeResponse ExportLearnset(LearnsetExportRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var titleId = request.TitleId ?? workspace.TitleId;
        if (string.IsNullOrWhiteSpace(titleId) || titleId.Length != 16 || titleId.Any(c => !Uri.IsHexDigit(c)))
            throw new WorkspaceException("No pude detectar un Title ID válido. Seleccioná la carpeta completa que contiene exheader.bin.");
        var language = GetLanguage(request.Language);
        var root = Path.Combine(Path.GetTempPath(), $"pk3ds-mac-levelup-{Guid.NewGuid():N}");
        var tempRomFs = Path.Combine(root, "romfs");
        try
        {
            Directory.CreateDirectory(tempRomFs);
            var probe = new GameConfig(workspace.Version); probe.Initialize(workspace.RomFsPath, root, language);
            foreach (var name in RequiredGarcs) CopyRelativeFile(workspace.RomFsPath, tempRomFs, probe.GetGARCFileName(name));
            var config = new GameConfig(workspace.Version); config.Initialize(tempRomFs, root, language);
            if (request.SpeciesIndex < 0 || request.SpeciesIndex >= config.Learnsets.Length) throw new WorkspaceException("La especie indicada no existe.");
            var entries = request.Entries ?? [];
            if (entries.Any(entry => entry.MoveId < 1 || entry.MoveId >= config.Moves.Length)) throw new WorkspaceException("Una de las entradas tiene un movimiento inválido.");
            var set = config.Learnsets[request.SpeciesIndex];
            set.Levels = entries.Select(entry => Math.Clamp(entry.Level, 1, 100)).ToArray();
            set.Moves = entries.Select(entry => entry.MoveId).ToArray();
            config.GARCLearnsets.Files[request.SpeciesIndex] = set.Write(); config.GARCLearnsets.Save();
            return CreateLayeredFsArchive(request.OutputDirectory, workspace.RomFsPath, tempRomFs, titleId, [config.GetGARCFileName("levelup")], "levelup");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    public EggMoveTableResponse GetEggMoves(EggMoveTableRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath); var config = InitializeReadOnlyConfig(workspace, GetLanguage(request.Language));
        var garc = config.GetGARCData("eggmove");
        if (request.SpeciesIndex < 0 || request.SpeciesIndex >= garc.Files.Length) throw new WorkspaceException("La especie indicada no existe.");
        EggMoves set = config.Generation == 6 ? new EggMoves6(garc.Files[request.SpeciesIndex]) : new EggMoves7(garc.Files[request.SpeciesIndex]);
        return new EggMoveTableResponse(request.SpeciesIndex, set.Moves, set.FormTableIndex);
    }

    public RandomizeResponse ExportEggMoves(EggMoveExportRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath); var titleId = request.TitleId ?? workspace.TitleId;
        if (string.IsNullOrWhiteSpace(titleId) || titleId.Length != 16 || titleId.Any(c => !Uri.IsHexDigit(c))) throw new WorkspaceException("No pude detectar un Title ID válido.");
        var language = GetLanguage(request.Language); var root = Path.Combine(Path.GetTempPath(), $"pk3ds-mac-eggmove-{Guid.NewGuid():N}"); var temp = Path.Combine(root, "romfs");
        try { Directory.CreateDirectory(temp); var probe = new GameConfig(workspace.Version); probe.Initialize(workspace.RomFsPath, root, language); foreach (var name in RequiredGarcs) CopyRelativeFile(workspace.RomFsPath, temp, probe.GetGARCFileName(name)); var config = new GameConfig(workspace.Version); config.Initialize(temp, root, language); var garc = config.GetGARCData("eggmove"); if (request.SpeciesIndex < 0 || request.SpeciesIndex >= garc.Files.Length) throw new WorkspaceException("La especie indicada no existe."); var moves = request.Moves ?? []; if (moves.Any(move => move < 1 || move >= config.Moves.Length)) throw new WorkspaceException("Hay un movimiento inválido."); EggMoves set = config.Generation == 6 ? new EggMoves6(garc.Files[request.SpeciesIndex]) : new EggMoves7(garc.Files[request.SpeciesIndex]); set.Moves = moves.Distinct().ToArray(); if (config.Generation == 7 && request.FormTableIndex is not null) set.FormTableIndex = Math.Clamp(request.FormTableIndex.Value, 0, ushort.MaxValue); garc.Files[request.SpeciesIndex] = set.Write(); garc.Save(); return CreateLayeredFsArchive(request.OutputDirectory, workspace.RomFsPath, temp, titleId, [config.GetGARCFileName("eggmove")], "eggmove"); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    public EvolutionTableResponse GetEvolutionTable(EvolutionTableRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath); var config = InitializeReadOnlyConfig(workspace, GetLanguage(request.Language));
        if (request.SpeciesIndex < 0 || request.SpeciesIndex >= config.Evolutions.Length) throw new WorkspaceException("La especie indicada no existe.");
        return new EvolutionTableResponse(request.SpeciesIndex, config.Evolutions[request.SpeciesIndex].PossibleEvolutions.Select(e => new EvolutionEntry(e.Method, e.Argument, e.Species, e.Form, e.Level)).ToArray());
    }

    public RandomizeResponse ExportEvolutionTable(EvolutionExportRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath); var titleId = request.TitleId ?? workspace.TitleId; if (string.IsNullOrWhiteSpace(titleId) || titleId.Length != 16 || titleId.Any(c => !Uri.IsHexDigit(c))) throw new WorkspaceException("No pude detectar un Title ID válido.");
        var language = GetLanguage(request.Language); var root = Path.Combine(Path.GetTempPath(), $"pk3ds-mac-evolution-{Guid.NewGuid():N}"); var temp = Path.Combine(root, "romfs");
        try { Directory.CreateDirectory(temp); var probe = new GameConfig(workspace.Version); probe.Initialize(workspace.RomFsPath, root, language); foreach (var name in RequiredGarcs) CopyRelativeFile(workspace.RomFsPath, temp, probe.GetGARCFileName(name)); var config = new GameConfig(workspace.Version); config.Initialize(temp, root, language); if (request.SpeciesIndex < 0 || request.SpeciesIndex >= config.Evolutions.Length) throw new WorkspaceException("La especie indicada no existe."); var entries = request.Entries ?? []; if (entries.Length != 8 || entries.Any(e => e.Method is < 0 or > ushort.MaxValue || e.Argument is < 0 or > ushort.MaxValue || e.Species is < 0 or > ushort.MaxValue || e.Level is < 0 or > byte.MaxValue || e.Form is < sbyte.MinValue or > sbyte.MaxValue)) throw new WorkspaceException("Cada especie debe tener ocho entradas de evolución válidas."); config.Evolutions[request.SpeciesIndex].PossibleEvolutions = entries.Select(e => new EvolutionMethod { Method=e.Method, Argument=e.Argument, Species=e.Species, Form=e.Form, Level=e.Level }).ToArray(); SaveEvolutions(config); return CreateLayeredFsArchive(request.OutputDirectory, workspace.RomFsPath, temp, titleId, [config.GetGARCFileName("evolution")], "evolution"); } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    public PersonalEntryResponse GetPersonalEntry(PersonalEntryRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath); var config = InitializeReadOnlyConfig(workspace, GetLanguage(request.Language));
        if (request.SpeciesIndex < 0 || request.SpeciesIndex >= config.Personal.Table.Length) throw new WorkspaceException("La especie indicada no existe.");
        var p = config.Personal[request.SpeciesIndex]; return new PersonalEntryResponse(request.SpeciesIndex, [p.HP,p.ATK,p.DEF,p.SPE,p.SPA,p.SPD], p.Types, p.CatchRate, p.Abilities, p.Items, p.EggGroups);
    }

    public RandomizeResponse ExportPersonalEntry(PersonalExportRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath); var titleId = request.TitleId ?? workspace.TitleId; if (string.IsNullOrWhiteSpace(titleId) || titleId.Length != 16 || titleId.Any(c => !Uri.IsHexDigit(c))) throw new WorkspaceException("No pude detectar un Title ID válido.");
        if (request.Stats?.Length != 6 || request.Types?.Length != 2 || request.Abilities?.Length != 3 || request.Items?.Length != 3 || request.EggGroups?.Length != 2) throw new WorkspaceException("La entrada personal está incompleta.");
        var lang=GetLanguage(request.Language); var root=Path.Combine(Path.GetTempPath(),$"pk3ds-mac-personal-{Guid.NewGuid():N}"); var temp=Path.Combine(root,"romfs");
        try { Directory.CreateDirectory(temp); var probe=new GameConfig(workspace.Version); probe.Initialize(workspace.RomFsPath,root,lang); foreach(var n in RequiredGarcs) CopyRelativeFile(workspace.RomFsPath,temp,probe.GetGARCFileName(n)); var config=new GameConfig(workspace.Version); config.Initialize(temp,root,lang); if(request.SpeciesIndex<0||request.SpeciesIndex>=config.Personal.Table.Length) throw new WorkspaceException("La especie indicada no existe."); var p=config.Personal[request.SpeciesIndex]; p.Stats=request.Stats.Select(x=>Math.Clamp(x,0,255)).ToArray(); p.Types=request.Types.Select(x=>Math.Clamp(x,0,255)).ToArray(); p.CatchRate=Math.Clamp(request.CatchRate,0,255); p.Abilities=request.Abilities.Select(x=>Math.Clamp(x,0,255)).ToArray(); p.Items=request.Items.Select(x=>Math.Clamp(x,0,ushort.MaxValue)).ToArray(); p.EggGroups=request.EggGroups.Select(x=>Math.Clamp(x,0,255)).ToArray(); SavePersonal(config); return CreateLayeredFsArchive(request.OutputDirectory,workspace.RomFsPath,temp,titleId,[config.GetGARCFileName("personal")],"personal"); } finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }

    public MoveEntryResponse GetMoveEntry(MoveEntryRequest request)
    {
        var workspace=GameWorkspace.Open(request.WorkspacePath); var config=InitializeReadOnlyConfig(workspace,GetLanguage(request.Language)); if(request.MoveIndex<1||request.MoveIndex>=config.Moves.Length) throw new WorkspaceException("El movimiento indicado no existe."); var m=config.Moves[request.MoveIndex]; return new MoveEntryResponse(request.MoveIndex,m.Type,m.Category,m.Power,m.Accuracy,m.PP,m.Priority);
    }
    public RandomizeResponse ExportMoveEntry(MoveExportRequest request)
    {
        var workspace=GameWorkspace.Open(request.WorkspacePath);var titleId=request.TitleId??workspace.TitleId;if(string.IsNullOrWhiteSpace(titleId)||titleId.Length!=16||titleId.Any(c=>!Uri.IsHexDigit(c)))throw new WorkspaceException("No pude detectar un Title ID válido.");var lang=GetLanguage(request.Language);var root=Path.Combine(Path.GetTempPath(),$"pk3ds-mac-move-{Guid.NewGuid():N}");var temp=Path.Combine(root,"romfs");try{Directory.CreateDirectory(temp);var probe=new GameConfig(workspace.Version);probe.Initialize(workspace.RomFsPath,root,lang);foreach(var n in RequiredGarcs)CopyRelativeFile(workspace.RomFsPath,temp,probe.GetGARCFileName(n));var config=new GameConfig(workspace.Version);config.Initialize(temp,root,lang);if(request.MoveIndex<1||request.MoveIndex>=config.Moves.Length)throw new WorkspaceException("El movimiento indicado no existe.");var m=config.Moves[request.MoveIndex];m.Type=Math.Clamp(request.Type,0,17);m.Category=Math.Clamp(request.Category,0,2);m.Power=Math.Clamp(request.Power,0,255);m.Accuracy=Math.Clamp(request.Accuracy,0,255);m.PP=Math.Clamp(request.PP,0,255);m.Priority=Math.Clamp(request.Priority,-128,127);SaveMoves(config);return CreateLayeredFsArchive(request.OutputDirectory,workspace.RomFsPath,temp,titleId,[config.GetGARCFileName("move")],"move");}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    public ItemEntryResponse GetItemEntry(ItemEntryRequest request)
    {
        var w=GameWorkspace.Open(request.WorkspacePath);var c=InitializeReadOnlyConfig(w,GetLanguage(request.Language));var g=c.GetGARCData("item");if(request.ItemIndex<1||request.ItemIndex>=g.Files.Length)throw new WorkspaceException("El objeto indicado no existe.");var i=new Item(g.Files[request.ItemIndex]);return new ItemEntryResponse(request.ItemIndex,i.BuyPrice,i.HeldEffect,i.HeldArgument,i.FlingPower,i.EffectField,i.EffectBattle,i.HealValue);
    }
    public RandomizeResponse ExportItemEntry(ItemExportRequest request)
    {
        var w=GameWorkspace.Open(request.WorkspacePath);var id=request.TitleId??w.TitleId;if(string.IsNullOrWhiteSpace(id)||id.Length!=16||id.Any(c=>!Uri.IsHexDigit(c)))throw new WorkspaceException("No pude detectar un Title ID válido.");var l=GetLanguage(request.Language);var root=Path.Combine(Path.GetTempPath(),$"pk3ds-mac-item-{Guid.NewGuid():N}");var temp=Path.Combine(root,"romfs");try{Directory.CreateDirectory(temp);var p=new GameConfig(w.Version);p.Initialize(w.RomFsPath,root,l);foreach(var n in RequiredGarcs)CopyRelativeFile(w.RomFsPath,temp,p.GetGARCFileName(n));var c=new GameConfig(w.Version);c.Initialize(temp,root,l);var g=c.GetGARCData("item");if(request.ItemIndex<1||request.ItemIndex>=g.Files.Length)throw new WorkspaceException("El objeto indicado no existe.");var i=new Item(g.Files[request.ItemIndex]){BuyPrice=Math.Clamp(request.BuyPrice,0,655350),HeldEffect=(byte)Math.Clamp(request.HeldEffect,0,255),HeldArgument=(byte)Math.Clamp(request.HeldArgument,0,255),FlingPower=(byte)Math.Clamp(request.FlingPower,0,255),EffectField=(byte)Math.Clamp(request.EffectField,0,255),EffectBattle=(byte)Math.Clamp(request.EffectBattle,0,255),HealValue=Math.Clamp(request.HealValue,0,255)};g.Files[request.ItemIndex]=i.Write();g.Save();return CreateLayeredFsArchive(request.OutputDirectory,w.RomFsPath,temp,id,[c.GetGARCFileName("item")],"item");}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    public MegaTableResponse GetMegaTable(MegaTableRequest request)
    {
        var w=GameWorkspace.Open(request.WorkspacePath);var c=InitializeReadOnlyConfig(w,GetLanguage(request.Language));var g=c.GetGARCData("megaevo");if(request.SpeciesIndex<0||request.SpeciesIndex>=g.Files.Length)throw new WorkspaceException("La especie indicada no existe.");var m=new MegaEvolutions(g.Files[request.SpeciesIndex]);return new MegaTableResponse(request.SpeciesIndex,m.Form.Select((_,i)=>new MegaEntry(m.Form[i],m.Method[i],m.Argument[i],m.u6[i])).ToArray());
    }
    public RandomizeResponse ExportMegaTable(MegaExportRequest request)
    {
        var w=GameWorkspace.Open(request.WorkspacePath);var id=request.TitleId??w.TitleId;if(string.IsNullOrWhiteSpace(id)||id.Length!=16||id.Any(c=>!Uri.IsHexDigit(c)))throw new WorkspaceException("No pude detectar un Title ID válido.");var l=GetLanguage(request.Language);var root=Path.Combine(Path.GetTempPath(),$"pk3ds-mac-mega-{Guid.NewGuid():N}");var temp=Path.Combine(root,"romfs");try{Directory.CreateDirectory(temp);var p=new GameConfig(w.Version);p.Initialize(w.RomFsPath,root,l);foreach(var n in RequiredGarcs)CopyRelativeFile(w.RomFsPath,temp,p.GetGARCFileName(n));var c=new GameConfig(w.Version);c.Initialize(temp,root,l);var g=c.GetGARCData("megaevo");if(request.SpeciesIndex<0||request.SpeciesIndex>=g.Files.Length)throw new WorkspaceException("La especie indicada no existe.");var old=new MegaEvolutions(g.Files[request.SpeciesIndex]);var e=request.Entries??[];if(e.Length!=old.Form.Length||e.Any(x=>x.Form is<0 or>ushort.MaxValue||x.Method is<0 or>ushort.MaxValue||x.Argument is<0 or>ushort.MaxValue||x.Auxiliary is<0 or>ushort.MaxValue))throw new WorkspaceException("Las entradas mega no son válidas.");old.Form=e.Select(x=>(ushort)x.Form).ToArray();old.Method=e.Select(x=>(ushort)x.Method).ToArray();old.Argument=e.Select(x=>(ushort)x.Argument).ToArray();old.u6=e.Select(x=>(ushort)x.Auxiliary).ToArray();g.Files[request.SpeciesIndex]=old.Write();g.Save();return CreateLayeredFsArchive(request.OutputDirectory,w.RomFsPath,temp,id,[c.GetGARCFileName("megaevo")],"megaevo");}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    public WildAreaCatalogResponse GetWildAreaCatalog(WildAreaCatalogRequest request)
    {
        var w=GameWorkspace.Open(request.WorkspacePath);var c=InitializeReadOnlyConfig(w,GetLanguage(request.Language));if(c.Generation!=7)throw new WorkspaceException("El editor inicial de encuentros está disponible primero para Gen. VII.");var areas=Area7.GetArray(c.GetlzGARCData("encdata"),c.GetlzGARCData("zonedata"),c.GetlzGARCData("worlddata"),c.GetText(TextName.metlist_000000));return new WildAreaCatalogResponse(areas.Where(a=>a.HasTables).Select(a=>new WildAreaSummary(a.FileNumber,a.Name,a.Tables.Count/2)).ToArray());
    }

    public WildTableResponse GetWildTable(WildTableRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        var c = InitializeReadOnlyConfig(w, GetLanguage(request.Language));
        EnsureGen7(c);
        var area = FindWildArea(c, request.FileNumber);
        if (request.TableIndex < 0 || request.TableIndex >= area.Tables.Count / 2)
            throw new WorkspaceException("La tabla indicada no existe en esta área.");

        var species = c.GetText(TextName.SpeciesNames)
            .Select((name, id) => new NamedEntry(id, string.IsNullOrWhiteSpace(name) ? $"Especie {id}" : name))
            .ToArray();
        return new WildTableResponse(area.FileNumber, area.Name, request.TableIndex,
            ToWildTable(area.Tables[request.TableIndex * 2]),
            ToWildTable(area.Tables[request.TableIndex * 2 + 1]), species);
    }

    public RandomizeResponse ExportWildTable(WildExportRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        var titleId = request.TitleId ?? w.TitleId;
        if (string.IsNullOrWhiteSpace(titleId) || titleId.Length != 16 || titleId.Any(c => !Uri.IsHexDigit(c)))
            throw new WorkspaceException("No pude detectar un Title ID válido.");

        var language = GetLanguage(request.Language);
        var root = Path.Combine(Path.GetTempPath(), $"pk3ds-mac-wild-{Guid.NewGuid():N}");
        var temp = Path.Combine(root, "romfs");
        try
        {
            Directory.CreateDirectory(temp);
            var probe = new GameConfig(w.Version);
            probe.Initialize(w.RomFsPath, root, language);
            foreach (var name in RequiredGarcs)
                CopyRelativeFile(w.RomFsPath, temp, probe.GetGARCFileName(name));
            foreach (var name in new[] { "encdata", "zonedata", "worlddata" })
                CopyRelativeFile(w.RomFsPath, temp, probe.GetGARCFileName(name));

            var c = new GameConfig(w.Version);
            c.Initialize(temp, root, language);
            EnsureGen7(c);
            var encdata = c.GetlzGARCData("encdata");
            var area = FindWildArea(c, request.FileNumber, encdata);
            if (request.TableIndex < 0 || request.TableIndex >= area.Tables.Count / 2)
                throw new WorkspaceException("La tabla indicada no existe en esta área.");

            var speciesCount = c.GetText(TextName.SpeciesNames).Length;
            ApplyWildTable(area.Tables[request.TableIndex * 2], request.Day, speciesCount);
            ApplyWildTable(area.Tables[request.TableIndex * 2 + 1], request.Night, speciesCount);
            encdata[area.FileNumber] = Area7.GetDayNightTableBinary(area.Tables);
            encdata.Save();

            return CreateLayeredFsArchive(request.OutputDirectory, w.RomFsPath, temp, titleId,
                [c.GetGARCFileName("encdata")], "wild");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void EnsureGen7(GameConfig config)
    {
        if (config.Generation != 7)
            throw new WorkspaceException("El editor inicial de encuentros está disponible primero para Gen. VII.");
    }

    private static Area7 FindWildArea(GameConfig config, int fileNumber, LazyGARCFile? encounterData = null)
    {
        var areas = Area7.GetArray(encounterData ?? config.GetlzGARCData("encdata"), config.GetlzGARCData("zonedata"),
            config.GetlzGARCData("worlddata"), config.GetText(TextName.metlist_000000));
        return areas.FirstOrDefault(area => area.FileNumber == fileNumber && area.HasTables)
            ?? throw new WorkspaceException("El área indicada no contiene tablas de encuentros.");
    }

    private static WildEncounterTable ToWildTable(EncounterTable table) => new(
        table.MinLevel, table.MaxLevel,
        table.Encounter7s[0].Select((slot, index) => new WildEncounterSlot((int)slot.Species, (int)slot.Forme, table.Rates[index])).ToArray(),
        table.Encounter7s.Skip(1).Take(7).Select(group => group.Select(slot => new WildEncounterCompanionSlot((int)slot.Species, (int)slot.Forme)).ToArray()).ToArray(),
        table.AdditionalSOS.Select(slot => new WildEncounterCompanionSlot((int)slot.Species, (int)slot.Forme)).ToArray());

    private static void ApplyWildTable(EncounterTable target, WildEncounterTable? source, int speciesCount)
    {
        if (source is null || source.Slots is null || source.Slots.Length != 10)
            throw new WorkspaceException("Cada tabla debe incluir exactamente diez slots.");
        if (source.MinLevel is < 1 or > 100 || source.MaxLevel is < 1 or > 100 || source.MinLevel > source.MaxLevel)
            throw new WorkspaceException("Los niveles deben estar entre 1 y 100, con el mínimo no mayor que el máximo.");
        if (source.Slots.Any(slot => slot.Species is < 0 || slot.Species >= speciesCount || slot.Form is < 0 or > 31 || slot.Rate is < 0 or > 100))
            throw new WorkspaceException("Hay una especie, forma o probabilidad de encuentro inválida.");
        if (source.SosSlots is null || source.SosSlots.Length != 7 || source.SosSlots.Any(group => group is null || group.Length != 10) || source.WeatherSlots is null || source.WeatherSlots.Length != 6)
            throw new WorkspaceException("Los slots SOS o de clima están incompletos.");
        if (source.SosSlots.SelectMany(group => group).Concat(source.WeatherSlots).Any(slot => slot.Species is < 0 || slot.Species >= speciesCount || slot.Form is < 0 or > 31))
            throw new WorkspaceException("Hay una especie o forma inválida en los slots SOS o de clima.");
        var rateTotal = source.Slots.Sum(slot => slot.Rate);
        if (rateTotal is not 0 and not 100)
            throw new WorkspaceException("Las probabilidades de cada tabla deben sumar 100% (o 0% para una tabla vacía).");

        target.MinLevel = source.MinLevel;
        target.MaxLevel = source.MaxLevel;
        for (var i = 0; i < source.Slots.Length; i++)
        {
            target.Encounter7s[0][i].Species = (uint)source.Slots[i].Species;
            target.Encounter7s[0][i].Forme = (uint)source.Slots[i].Form;
            target.Rates[i] = source.Slots[i].Rate;
        }
        for (var group = 0; group < source.SosSlots.Length; group++)
        for (var slot = 0; slot < source.SosSlots[group].Length; slot++)
        {
            target.Encounter7s[group + 1][slot].Species = (uint)source.SosSlots[group][slot].Species;
            target.Encounter7s[group + 1][slot].Forme = (uint)source.SosSlots[group][slot].Form;
        }
        for (var slot = 0; slot < source.WeatherSlots.Length; slot++)
        {
            target.AdditionalSOS[slot].Species = (uint)source.WeatherSlots[slot].Species;
            target.AdditionalSOS[slot].Forme = (uint)source.WeatherSlots[slot].Form;
        }
        target.Write();
    }

    public WildGen6CatalogResponse GetWildGen6Catalog(WildGen6CatalogRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        var c = InitializeReadOnlyConfig(w, GetLanguage(request.Language));
        EnsureGen6(c);
        var garc = c.GetGARCData("encdata");
        var firstMapFile = c.ORAS ? 2 : 1;
        var zonedata = garc.Files[0];
        var locations = c.GetText(TextName.metlist_000000);
        var areas = Enumerable.Range(firstMapFile, garc.Files.Length - firstMapFile).Select(fileIndex =>
        {
            var locationIndex = fileIndex - firstMapFile;
            var name = GetGen6AreaName(zonedata, locations, locationIndex);
            return new WildGen6AreaSummary(fileIndex, locationIndex, name, TryGetGen6EncounterOffset(garc.Files[fileIndex], c.ORAS, out _));
        }).ToArray();
        return new WildGen6CatalogResponse(c.ORAS ? "ORAS" : "XY", areas);
    }

    public WildGen6TableResponse GetWildGen6Table(WildGen6TableRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        var c = InitializeReadOnlyConfig(w, GetLanguage(request.Language));
        EnsureGen6(c);
        var garc = c.GetGARCData("encdata");
        ValidateGen6FileIndex(c, garc, request.FileIndex);
        var file = garc.Files[request.FileIndex];
        if (!TryGetGen6EncounterOffset(file, c.ORAS, out var offset))
            throw new WorkspaceException("Esta área no contiene una tabla de encuentros que se pueda editar.");
        var locationIndex = request.FileIndex - (c.ORAS ? 2 : 1);
        var slots = ReadGen6Slots(file, offset, GetGen6SlotCount(c.ORAS));
        var groups = GetGen6Groups(c.ORAS, slots);
        var species = c.GetText(TextName.SpeciesNames).Select((name, id) => new NamedEntry(id, string.IsNullOrWhiteSpace(name) ? $"Especie {id}" : name)).ToArray();
        return new WildGen6TableResponse(request.FileIndex, GetGen6AreaName(garc.Files[0], c.GetText(TextName.metlist_000000), locationIndex), groups, species);
    }

    public RandomizeResponse ExportWildGen6Table(WildGen6ExportRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        var titleId = request.TitleId ?? w.TitleId;
        if (string.IsNullOrWhiteSpace(titleId) || titleId.Length != 16 || titleId.Any(c => !Uri.IsHexDigit(c)))
            throw new WorkspaceException("No pude detectar un Title ID válido.");
        var language = GetLanguage(request.Language);
        var root = Path.Combine(Path.GetTempPath(), $"pk3ds-mac-wild6-{Guid.NewGuid():N}");
        var temp = Path.Combine(root, "romfs");
        try
        {
            Directory.CreateDirectory(temp);
            var probe = new GameConfig(w.Version);
            probe.Initialize(w.RomFsPath, root, language);
            foreach (var name in RequiredGarcs) CopyRelativeFile(w.RomFsPath, temp, probe.GetGARCFileName(name));
            CopyRelativeFile(w.RomFsPath, temp, probe.GetGARCFileName("encdata"));
            var c = new GameConfig(w.Version);
            c.Initialize(temp, root, language);
            EnsureGen6(c);
            var garc = c.GetGARCData("encdata");
            ValidateGen6FileIndex(c, garc, request.FileIndex);
            var file = garc.Files[request.FileIndex];
            if (!TryGetGen6EncounterOffset(file, c.ORAS, out var offset))
                throw new WorkspaceException("No se pueden añadir encuentros a un área que no tiene tabla.");
            var slotCount = GetGen6SlotCount(c.ORAS);
            var slots = FlattenGen6Groups(request.Groups, slotCount, c.GetText(TextName.SpeciesNames).Length);
            WriteGen6Slots(file, offset, slots);
            garc.Files[request.FileIndex] = file;
            if (c.ORAS)
            {
                var locationIndex = request.FileIndex - 2;
                var packed = garc.Files[1];
                var packedOffset = BitConverter.ToInt32(packed, (locationIndex + 1) * 4) + 0xE;
                if (packedOffset < 0 || packedOffset + (slotCount * 4) > packed.Length)
                    throw new WorkspaceException("La tabla interna de encuentros de OR/AS no es válida.");
                WriteGen6Slots(packed, packedOffset, slots);
                garc.Files[1] = packed;
            }
            garc.Save();
            return CreateLayeredFsArchive(request.OutputDirectory, w.RomFsPath, temp, titleId, [c.GetGARCFileName("encdata")], "wild");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void EnsureGen6(GameConfig config)
    {
        if (config.Generation != 6 || (!config.XY && !config.ORAS))
            throw new WorkspaceException("Este editor de encuentros es solo para X/Y y OR/AS.");
    }

    private static void ValidateGen6FileIndex(GameConfig config, GARCFile garc, int fileIndex)
    {
        var firstMapFile = config.ORAS ? 2 : 1;
        if (fileIndex < firstMapFile || fileIndex >= garc.Files.Length)
            throw new WorkspaceException("El área indicada no existe.");
    }

    private static string GetGen6AreaName(byte[] zonedata, string[] locations, int locationIndex)
    {
        var offset = (locationIndex * 56) + 0x1C;
        if (offset + 1 >= zonedata.Length) return $"Área {locationIndex:000}";
        var locationId = zonedata[offset] + (0x100 * (zonedata[offset + 1] & 1));
        var name = locationId >= 0 && locationId < locations.Length ? locations[locationId] : "";
        return string.IsNullOrWhiteSpace(name) ? $"Área {locationIndex:000}" : $"{locationIndex:000} · {name}";
    }

    private static int GetGen6SlotCount(bool oras) => oras ? 61 : 94;
    private static bool TryGetGen6EncounterOffset(byte[] file, bool oras, out int offset)
    {
        offset = 0;
        if (file.Length < 0x18) return false;
        offset = BitConverter.ToInt32(file, 0x10) + (oras ? 0xE : 0x10);
        return offset >= 0 && offset + (GetGen6SlotCount(oras) * 4) <= file.Length;
    }

    private static WildGen6Slot[] ReadGen6Slots(byte[] data, int offset, int count) => Enumerable.Range(0, count).Select(index =>
    {
        var raw = BitConverter.ToUInt16(data, offset + (index * 4));
        return new WildGen6Slot(raw & 0x7FF, raw >> 11, data[offset + (index * 4) + 2], data[offset + (index * 4) + 3]);
    }).ToArray();

    private static void WriteGen6Slots(byte[] data, int offset, WildGen6Slot[] slots)
    {
        for (var index = 0; index < slots.Length; index++)
        {
            var value = (ushort)(slots[index].Species | (slots[index].Form << 11));
            BitConverter.GetBytes(value).CopyTo(data, offset + (index * 4));
            data[offset + (index * 4) + 2] = (byte)slots[index].MinLevel;
            data[offset + (index * 4) + 3] = (byte)slots[index].MaxLevel;
        }
    }

    private static WildGen6Group[] GetGen6Groups(bool oras, WildGen6Slot[] slots)
    {
        var definitions = oras
            ? new (string Name, int Count)[] { ("Hierba",12), ("Hierba alta",12), ("Enjambre",3), ("Surf",5), ("Golpe Roca",5), ("Caña Vieja",3), ("Caña Buena",3), ("Caña Super",3), ("Horda A · 60%",5), ("Horda B · 35%",5), ("Horda C · 5%",5) }
            : new (string Name, int Count)[] { ("Hierba",12), ("Flores amarillas",12), ("Flores moradas",12), ("Flores rojas",12), ("Terreno rocoso",12), ("Surf",5), ("Golpe Roca",5), ("Caña Vieja",3), ("Caña Buena",3), ("Caña Super",3), ("Horda A · 60%",5), ("Horda B · 35%",5), ("Horda C · 5%",5) };
        var offset = 0;
        return definitions.Select(definition =>
        {
            var group = new WildGen6Group(definition.Name, slots.Skip(offset).Take(definition.Count).ToArray());
            offset += definition.Count;
            return group;
        }).ToArray();
    }

    private static WildGen6Slot[] FlattenGen6Groups(WildGen6Group[]? groups, int expectedCount, int speciesCount)
    {
        var slots = groups?.SelectMany(group => group.Slots ?? []).ToArray() ?? [];
        if (slots.Length != expectedCount || slots.Any(slot => slot.Species is < 0 || slot.Species >= speciesCount || slot.Form is < 0 or > 31 || slot.MinLevel is < 0 or > 100 || slot.MaxLevel is < 0 or > 100 || slot.MinLevel > slot.MaxLevel))
            throw new WorkspaceException("Los slots de encuentro no son válidos.");
        return slots;
    }

    public StaticCatalogResponse GetStaticCatalog(StaticCatalogRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        var c = InitializeReadOnlyConfig(w, GetLanguage(request.Language));
        EnsureGen7(c);
        var garc = c.GetGARCData("encounterstatic");
        if (garc.Files.Length <= 4)
            throw new WorkspaceException("El archivo de encuentros estáticos no tiene el formato esperado.");
        var species = c.GetText(TextName.SpeciesNames).Select((name, id) => new NamedEntry(id, string.IsNullOrWhiteSpace(name) ? $"Especie {id}" : name)).ToArray();
        var items = c.GetText(TextName.ItemNames).Select((name, id) => new NamedEntry(id, string.IsNullOrWhiteSpace(name) ? $"Objeto {id}" : name)).ToArray();
        var moves = c.GetText(TextName.MoveNames).Select((name, id) => new NamedEntry(id, string.IsNullOrWhiteSpace(name) ? $"Movimiento {id}" : name)).ToArray();
        return new StaticCatalogResponse(
            [
                new StaticGroupSummary("gift", "Regalos", garc.Files[0].Length / EncounterGift7.SIZE),
                new StaticGroupSummary("static", "Encuentros fijos", garc.Files[1].Length / EncounterStatic7.SIZE),
                new StaticGroupSummary("trade", "Intercambios", garc.Files[4].Length / EncounterTrade7.SIZE),
            ], species, items, moves);
    }

    public StaticEntryResponse GetStaticEntry(StaticEntryRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        var c = InitializeReadOnlyConfig(w, GetLanguage(request.Language));
        EnsureGen7(c);
        var garc = c.GetGARCData("encounterstatic");
        return ToStaticEntryResponse(garc, request.Group, request.EntryIndex);
    }

    public RandomizeResponse ExportStaticEntry(StaticExportRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        var titleId = request.TitleId ?? w.TitleId;
        if (string.IsNullOrWhiteSpace(titleId) || titleId.Length != 16 || titleId.Any(c => !Uri.IsHexDigit(c)))
            throw new WorkspaceException("No pude detectar un Title ID válido.");
        var language = GetLanguage(request.Language);
        var root = Path.Combine(Path.GetTempPath(), $"pk3ds-mac-static-{Guid.NewGuid():N}");
        var temp = Path.Combine(root, "romfs");
        try
        {
            Directory.CreateDirectory(temp);
            var probe = new GameConfig(w.Version);
            probe.Initialize(w.RomFsPath, root, language);
            foreach (var name in RequiredGarcs) CopyRelativeFile(w.RomFsPath, temp, probe.GetGARCFileName(name));
            CopyRelativeFile(w.RomFsPath, temp, probe.GetGARCFileName("encounterstatic"));
            var c = new GameConfig(w.Version);
            c.Initialize(temp, root, language);
            EnsureGen7(c);
            var garc = c.GetGARCData("encounterstatic");
            ValidateStaticEntry(request.Entry, request.Group, c.GetText(TextName.SpeciesNames).Length, c.GetText(TextName.ItemNames).Length, c.GetText(TextName.MoveNames).Length);
            ApplyStaticEntry(garc, request.Group, request.EntryIndex, request.Entry);
            garc.Save();
            return CreateLayeredFsArchive(request.OutputDirectory, w.RomFsPath, temp, titleId, [c.GetGARCFileName("encounterstatic")], "static");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static StaticEntryResponse ToStaticEntryResponse(GARCFile garc, string group, int entryIndex)
    {
        var (fileIndex, size) = GetStaticGroupLayout(group);
        if (fileIndex >= garc.Files.Length || entryIndex < 0 || (entryIndex + 1) * size > garc.Files[fileIndex].Length)
            throw new WorkspaceException("La entrada de encuentro estático no existe.");
        var data = garc.Files[fileIndex].Skip(entryIndex * size).Take(size).ToArray();
        return group switch
        {
            "gift" => ToStaticEntryResponse(group, entryIndex, new EncounterGift7(data)),
            "static" => ToStaticEntryResponse(group, entryIndex, new EncounterStatic7(data)),
            "trade" => ToStaticEntryResponse(group, entryIndex, new EncounterTrade7(data)),
            _ => throw new WorkspaceException("El grupo de encuentros estáticos no es válido."),
        };
    }

    private static StaticEntryResponse ToStaticEntryResponse(string group, int index, EncounterGift7 entry) => new(group, index, new StaticEntry(entry.Species, entry.Form, entry.Level, entry.HeldItem, Ability: entry.Ability, Nature: entry.Nature, ShinyLock: entry.ShinyLock, IsEgg: entry.IsEgg, SpecialMove: entry.SpecialMove));
    private static StaticEntryResponse ToStaticEntryResponse(string group, int index, EncounterStatic7 entry) => new(group, index, new StaticEntry(entry.Species, entry.Form, entry.Level, entry.HeldItem, Gender: entry.Gender, Ability: entry.Ability, Nature: entry.Nature, ShinyLock: entry.ShinyLock, RelearnMoves: entry.RelearnMoves, IVs: entry.IVs, EVs: entry.EVs, Aura: entry.Aura, Ally1: entry.Ally1, Ally2: entry.Ally2));
    private static StaticEntryResponse ToStaticEntryResponse(string group, int index, EncounterTrade7 entry) => new(group, index, new StaticEntry(entry.Species, entry.Form, entry.Level, entry.HeldItem, Gender: entry.Gender, Ability: entry.Ability, Nature: entry.Nature, IVs: entry.IVs, TradeRequestSpecies: entry.TradeRequestSpecies, TID: entry.TID));

    private static void ApplyStaticEntry(GARCFile garc, string group, int entryIndex, StaticEntry entry)
    {
        var (fileIndex, size) = GetStaticGroupLayout(group);
        if (fileIndex >= garc.Files.Length || entryIndex < 0 || (entryIndex + 1) * size > garc.Files[fileIndex].Length)
            throw new WorkspaceException("La entrada de encuentro estático no existe.");
        var data = garc.Files[fileIndex].Skip(entryIndex * size).Take(size).ToArray();
        switch (group)
        {
            case "gift":
                var gift = new EncounterGift7(data) { Species = entry.Species, Form = entry.Form, Level = entry.Level, HeldItem = entry.HeldItem };
                if (entry.Ability is not null) gift.Ability = (sbyte)entry.Ability.Value;
                if (entry.Nature is not null) gift.Nature = (sbyte)entry.Nature.Value;
                if (entry.ShinyLock is not null) gift.ShinyLock = entry.ShinyLock.Value;
                if (entry.IsEgg is not null) gift.IsEgg = entry.IsEgg.Value;
                if (entry.SpecialMove is not null) gift.SpecialMove = entry.SpecialMove.Value;
                data = gift.Data;
                break;
            case "static":
                var encounter = new EncounterStatic7(data) { Species = entry.Species, Form = entry.Form, Level = entry.Level, HeldItem = entry.HeldItem };
                if (entry.Gender is not null) encounter.Gender = entry.Gender.Value;
                if (entry.Ability is not null) encounter.Ability = entry.Ability.Value;
                if (entry.Nature is not null) encounter.Nature = entry.Nature.Value;
                if (entry.ShinyLock is not null) encounter.ShinyLock = entry.ShinyLock.Value;
                if (entry.RelearnMoves is { Length: 4 }) encounter.RelearnMoves = entry.RelearnMoves;
                if (entry.IVs is { Length: 6 }) encounter.IVs = entry.IVs;
                if (entry.EVs is { Length: 6 }) encounter.EVs = entry.EVs;
                if (entry.Aura is not null) encounter.Aura = entry.Aura.Value;
                if (entry.Ally1 is not null) encounter.Ally1 = entry.Ally1.Value;
                if (entry.Ally2 is not null) encounter.Ally2 = entry.Ally2.Value;
                data = encounter.Data;
                break;
            case "trade":
                var trade = new EncounterTrade7(data) { Species = entry.Species, Form = entry.Form, Level = entry.Level, HeldItem = entry.HeldItem };
                if (entry.Gender is not null) trade.Gender = entry.Gender.Value;
                if (entry.Ability is not null) trade.Ability = entry.Ability.Value;
                if (entry.Nature is not null) trade.Nature = entry.Nature.Value;
                if (entry.IVs is { Length: 6 }) trade.IVs = entry.IVs;
                if (entry.TradeRequestSpecies is not null) trade.TradeRequestSpecies = entry.TradeRequestSpecies.Value;
                if (entry.TID is not null) trade.TID = entry.TID.Value;
                data = trade.Data;
                break;
            default:
                throw new WorkspaceException("El grupo de encuentros estáticos no es válido.");
        }
        Array.Copy(data, 0, garc.Files[fileIndex], entryIndex * size, size);
    }

    private static (int FileIndex, int Size) GetStaticGroupLayout(string group) => group switch
    {
        "gift" => (0, EncounterGift7.SIZE),
        "static" => (1, EncounterStatic7.SIZE),
        "trade" => (4, EncounterTrade7.SIZE),
        _ => throw new WorkspaceException("El grupo de encuentros estáticos no es válido."),
    };

    private static void ValidateStaticEntry(StaticEntry? entry, string group, int speciesCount, int itemCount, int moveCount)
    {
        if (entry is null || entry.Species is < 0 || entry.Species >= speciesCount || entry.Form is < 0 or > byte.MaxValue || entry.Level is < 1 or > 100 || entry.HeldItem is < 0 || entry.HeldItem >= itemCount || entry.Gender is < -1 or > 3 || entry.Ability is < -1 or > 7 || entry.Nature is < -1 or > 25 || entry.Aura is < 0 or > 18 || entry.Ally1 is < 0 or > byte.MaxValue || entry.Ally2 is < 0 or > byte.MaxValue || entry.SpecialMove is < 0 || entry.SpecialMove >= moveCount || entry.TradeRequestSpecies is < 0 || entry.TradeRequestSpecies >= speciesCount || entry.TID is < 0 or > ushort.MaxValue || entry.RelearnMoves is { Length: not 4 } || entry.RelearnMoves?.Any(move => move < 0 || move >= moveCount) == true || entry.IVs is { Length: not 6 } || entry.IVs?.Any(iv => iv is < -3 or > 31) == true || entry.EVs is { Length: not 6 } || entry.EVs?.Any(ev => ev is < 0 or > byte.MaxValue) == true)
            throw new WorkspaceException("La especie, forma, nivel u objeto no son válidos.");
    }

    public StaticGen6CatalogResponse GetStaticGen6Catalog(StaticGen6CatalogRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        EnsureGen6StaticFile(w);
        var c = InitializeReadOnlyConfig(w, GetLanguage(request.Language));
        EnsureGen6(c);
        return new StaticGen6CatalogResponse(c.ORAS ? "ORAS" : "XY", GetStaticGen6Count(c.ORAS),
            c.GetText(TextName.SpeciesNames).Select((name, id) => new NamedEntry(id, string.IsNullOrWhiteSpace(name) ? $"Especie {id}" : name)).ToArray(),
            c.GetText(TextName.ItemNames).Select((name, id) => new NamedEntry(id, string.IsNullOrWhiteSpace(name) ? $"Objeto {id}" : name)).ToArray(),
            "Este cambio usa DllField.cro. En consola requiere el parche RO de Luma para evitar fallos.");
    }

    public StaticGen6EntryResponse GetStaticGen6Entry(StaticGen6EntryRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        EnsureGen6StaticFile(w);
        var c = InitializeReadOnlyConfig(w, GetLanguage(request.Language));
        EnsureGen6(c);
        var data = File.ReadAllBytes(Path.Combine(w.RomFsPath, "DllField.cro"));
        return new StaticGen6EntryResponse(request.EntryIndex, ToStaticGen6Entry(data, c.ORAS, request.EntryIndex));
    }

    public RandomizeResponse ExportStaticGen6Entry(StaticGen6ExportRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        EnsureGen6StaticFile(w);
        var titleId = request.TitleId ?? w.TitleId;
        if (string.IsNullOrWhiteSpace(titleId) || titleId.Length != 16 || titleId.Any(c => !Uri.IsHexDigit(c)))
            throw new WorkspaceException("No pude detectar un Title ID válido.");
        var language = GetLanguage(request.Language);
        var root = Path.Combine(Path.GetTempPath(), $"pk3ds-mac-static6-{Guid.NewGuid():N}");
        var temp = Path.Combine(root, "romfs");
        try
        {
            Directory.CreateDirectory(temp);
            CopyRelativeFile(w.RomFsPath, temp, "DllField.cro");
            var c = InitializeReadOnlyConfig(w, language);
            EnsureGen6(c);
            var data = File.ReadAllBytes(Path.Combine(temp, "DllField.cro"));
            ValidateStaticGen6Entry(request.Entry, c.GetText(TextName.SpeciesNames).Length, c.GetText(TextName.ItemNames).Length);
            var offset = GetStaticGen6Offset(c.ORAS, request.EntryIndex);
            if (offset + 0xC > data.Length) throw new WorkspaceException("La entrada de encuentro estático no existe.");
            var entry = new EncounterStatic6(data.Skip(offset).Take(0xC).ToArray())
            {
                Species = (ushort)request.Entry!.Species,
                Form = (byte)request.Entry.Form,
                Level = (byte)request.Entry.Level,
                HeldItem = request.Entry.HeldItem,
                Gender = request.Entry.Gender,
                Ability = request.Entry.Ability,
                ShinyLock = request.Entry.ShinyLock,
                IV3 = request.Entry.IV3,
            };
            Array.Copy(entry.Write(), 0, data, offset, 0xC);
            File.WriteAllBytes(Path.Combine(temp, "DllField.cro"), data);
            return CreateLayeredFsArchive(request.OutputDirectory, w.RomFsPath, temp, titleId, ["DllField.cro"], "static6");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void EnsureGen6StaticFile(GameWorkspace workspace)
    {
        if (!File.Exists(Path.Combine(workspace.RomFsPath, "DllField.cro")))
            throw new WorkspaceException("Falta DllField.cro en el RomFS. Es necesario para editar estáticos de Gen. VI.");
    }

    private static int GetStaticGen6Count(bool oras) => oras ? 0x3B : 0xD;
    private static int GetStaticGen6Offset(bool oras, int entryIndex)
    {
        var count = GetStaticGen6Count(oras);
        if (entryIndex < 0 || entryIndex >= count) throw new WorkspaceException("La entrada de encuentro estático no existe.");
        return (oras ? 0xF1B20 : 0xEE46C) + (entryIndex * 0xC);
    }

    private static StaticGen6Entry ToStaticGen6Entry(byte[] data, bool oras, int entryIndex)
    {
        var offset = GetStaticGen6Offset(oras, entryIndex);
        if (offset + 0xC > data.Length) throw new WorkspaceException("DllField.cro no tiene el tamaño esperado.");
        var entry = new EncounterStatic6(data.Skip(offset).Take(0xC).ToArray());
        return new StaticGen6Entry(entry.Species, entry.Form, entry.Level, entry.HeldItem, entry.Gender, entry.Ability, entry.ShinyLock, entry.IV3);
    }

    private static void ValidateStaticGen6Entry(StaticGen6Entry? entry, int speciesCount, int itemCount)
    {
        if (entry is null || entry.Species is < 0 || entry.Species >= speciesCount || entry.Form is < 0 or > byte.MaxValue || entry.Level is < 1 or > 100 || entry.HeldItem is < 0 || entry.HeldItem >= itemCount || entry.Gender is < 0 or > 3 || entry.Ability is < 0 or > 7)
            throw new WorkspaceException("La especie, forma, nivel, objeto o flags no son válidos.");
    }

    public TrainerCatalogResponse GetTrainerCatalog(TrainerCatalogRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        var c = InitializeReadOnlyConfig(w, GetLanguage(request.Language));
        EnsureTrainerGen7(c);
        var trainers = c.GetGARCData("trdata");
        var names = c.GetText(TextName.TrainerNames);
        var classes = c.GetText(TextName.TrainerClasses);
        return new TrainerCatalogResponse(
            trainers.Files.Select((_, index) => new TrainerSummary(index, index < names.Length && !string.IsNullOrWhiteSpace(names[index]) ? names[index] : $"Entrenador {index}")).Skip(1).ToArray(),
            classes.Select((name, index) => new NamedEntry(index, string.IsNullOrWhiteSpace(name) ? $"Clase {index}" : name)).ToArray(),
            c.GetText(TextName.SpeciesNames).Select((name, index) => new NamedEntry(index, string.IsNullOrWhiteSpace(name) ? $"Especie {index}" : name)).ToArray(),
            c.GetText(TextName.ItemNames).Select((name, index) => new NamedEntry(index, string.IsNullOrWhiteSpace(name) ? $"Objeto {index}" : name)).ToArray(),
            c.GetText(TextName.MoveNames).Select((name, index) => new NamedEntry(index, string.IsNullOrWhiteSpace(name) ? $"Movimiento {index}" : name)).ToArray());
    }

    public TrainerEntryResponse GetTrainerEntry(TrainerEntryRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        var c = InitializeReadOnlyConfig(w, GetLanguage(request.Language));
        EnsureTrainerGen7(c);
        var trdata = c.GetGARCData("trdata");
        var trpoke = c.GetGARCData("trpoke");
        if (request.TrainerIndex < 0 || request.TrainerIndex >= trdata.Files.Length || request.TrainerIndex >= trpoke.Files.Length)
            throw new WorkspaceException("El entrenador indicado no existe.");
        var trainer = new TrainerData7(trdata.Files[request.TrainerIndex], trpoke.Files[request.TrainerIndex]);
        return new TrainerEntryResponse(request.TrainerIndex, ToTrainerEntry(trainer));
    }

    public RandomizeResponse ExportTrainerEntry(TrainerExportRequest request)
    {
        var w = GameWorkspace.Open(request.WorkspacePath);
        var titleId = request.TitleId ?? w.TitleId;
        if (string.IsNullOrWhiteSpace(titleId) || titleId.Length != 16 || titleId.Any(c => !Uri.IsHexDigit(c)))
            throw new WorkspaceException("No pude detectar un Title ID válido.");
        var language = GetLanguage(request.Language);
        var root = Path.Combine(Path.GetTempPath(), $"pk3ds-mac-trainer-{Guid.NewGuid():N}");
        var temp = Path.Combine(root, "romfs");
        try
        {
            Directory.CreateDirectory(temp);
            var probe = new GameConfig(w.Version);
            probe.Initialize(w.RomFsPath, root, language);
            foreach (var name in RequiredGarcs) CopyRelativeFile(w.RomFsPath, temp, probe.GetGARCFileName(name));
            foreach (var name in new[] { "trdata", "trpoke" }) CopyRelativeFile(w.RomFsPath, temp, probe.GetGARCFileName(name));
            var c = new GameConfig(w.Version);
            c.Initialize(temp, root, language);
            EnsureTrainerGen7(c);
            var trdata = c.GetGARCData("trdata");
            var trpoke = c.GetGARCData("trpoke");
            if (request.TrainerIndex < 0 || request.TrainerIndex >= trdata.Files.Length || request.TrainerIndex >= trpoke.Files.Length)
                throw new WorkspaceException("El entrenador indicado no existe.");
            ValidateTrainerEntry(request.Entry, c.GetText(TextName.TrainerClasses).Length, c.GetText(TextName.SpeciesNames).Length, c.GetText(TextName.ItemNames).Length, c.GetText(TextName.MoveNames).Length);
            var trainer = new TrainerData7(trdata.Files[request.TrainerIndex], trpoke.Files[request.TrainerIndex]);
            ApplyTrainerEntry(trainer, request.Entry!);
            trainer.Write(out var data, out var team);
            trdata.Files[request.TrainerIndex] = data;
            trpoke.Files[request.TrainerIndex] = team;
            trdata.Save(); trpoke.Save();
            return CreateLayeredFsArchive(request.OutputDirectory, w.RomFsPath, temp, titleId, [c.GetGARCFileName("trdata"), c.GetGARCFileName("trpoke")], "trainer");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void EnsureTrainerGen7(GameConfig config)
    {
        if (config.Generation != 7)
            throw new WorkspaceException("El editor inicial de entrenadores está disponible primero para Gen. VII.");
    }

    private static TrainerEntry ToTrainerEntry(TrainerData7 trainer) => new(trainer.TrainerClass, (int)trainer.Mode, [trainer.Item1, trainer.Item2, trainer.Item3, trainer.Item4], trainer.AI, trainer.Flag, trainer.Money,
        trainer.Pokemon.Select(pokemon => new TrainerPokemonEntry(pokemon.Species, pokemon.Form, pokemon.Level, pokemon.Item, pokemon.Moves, pokemon.Ability, pokemon.Gender, pokemon.Nature, pokemon.Shiny, pokemon.IVs, pokemon.EVs)).ToArray());

    private static void ApplyTrainerEntry(TrainerData7 trainer, TrainerEntry entry)
    {
        trainer.TrainerClass = entry.TrainerClass;
        trainer.Mode = (BattleMode)entry.Mode;
        trainer.Item1 = entry.Items[0]; trainer.Item2 = entry.Items[1]; trainer.Item3 = entry.Items[2]; trainer.Item4 = entry.Items[3];
        trainer.AI = entry.AI; trainer.Flag = entry.Flag; trainer.Money = entry.Money;
        for (var index = 0; index < trainer.Pokemon.Count; index++)
        {
            var source = entry.Team[index]; var target = trainer.Pokemon[index];
            target.Species = source.Species; target.Form = source.Form; target.Level = source.Level; target.Item = source.Item; target.Moves = source.Moves;
            target.Ability = source.Ability; target.Gender = source.Gender; target.Nature = source.Nature; target.Shiny = source.Shiny; target.IVs = source.IVs; target.EVs = source.EVs;
        }
    }

    private static void ValidateTrainerEntry(TrainerEntry? entry, int classCount, int speciesCount, int itemCount, int moveCount)
    {
        if (entry is null || entry.TrainerClass is < 0 || entry.TrainerClass >= classCount || entry.Mode is < 0 or > 2 || entry.Items is not { Length: 4 } || entry.Items.Any(item => item < 0 || item >= itemCount) || entry.AI is < 0 or > byte.MaxValue || entry.Money is < 0 or > byte.MaxValue || entry.Team is null || entry.Team.Length is < 1 or > 6 || entry.Team.Any(pokemon => pokemon.Species is < 0 || pokemon.Species >= speciesCount || pokemon.Form is < 0 or > byte.MaxValue || pokemon.Level is < 1 or > 100 || pokemon.Item is < 0 || pokemon.Item >= itemCount || pokemon.Moves is not { Length: 4 } || pokemon.Moves.Any(move => move < 0 || move >= moveCount) || pokemon.Ability is < 0 or > 3 || pokemon.Gender is < 0 or > 3 || pokemon.Nature is < 0 or > 25 || pokemon.IVs is not { Length: 6 } || pokemon.IVs.Any(iv => iv is < 0 or > 31) || pokemon.EVs is not { Length: 6 } || pokemon.EVs.Any(ev => ev is < 0 or > byte.MaxValue)))
            throw new WorkspaceException("Los datos del entrenador no son válidos.");
    }

    public RandomizeResponse Randomize(RandomizeRequest request)
    {
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var romfs = workspace.RomFsPath;
        var language = request.Language ?? 1;
        if (language is < 0 or > 11)
            throw new WorkspaceException("El idioma debe estar entre 0 y 11.");
        var personal = request.Personal ?? PersonalOptions.FromLegacy(request.RandomizeAbilities, request.RandomizeHeldItems);
        var learnsets = request.Learnsets ?? LearnsetOptions.FromLegacy(request.RandomizeLearnsets);
        var eggMoves = request.EggMoves ?? new EggMoveOptions();
        var moves = request.Moves ?? new MoveOptions();
        var evolutions = request.Evolutions ?? new EvolutionOptions();
        if (!personal.HasChanges && !learnsets.Enabled && !eggMoves.Enabled && !moves.HasChanges && evolutions.Mode == EvolutionMode.None)
            throw new WorkspaceException("Seleccioná al menos una opción para randomizar.");

        var probe = new GameConfig(workspace.Version);
        var titleId = request.TitleId ?? workspace.TitleId;
        if (string.IsNullOrWhiteSpace(titleId))
            throw new WorkspaceException("No pude detectar el Title ID. Seleccioná la carpeta extraída del juego que también contiene exheader.bin.");
        if (titleId.Length != 16 || titleId.Any(c => !Uri.IsHexDigit(c)))
            throw new WorkspaceException("El Title ID detectado no es válido.");

        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"pk3ds-mac-{Guid.NewGuid():N}");
        var temporaryRomFs = Path.Combine(temporaryRoot, "romfs");
        try
        {
            Directory.CreateDirectory(temporaryRomFs);
            probe.Initialize(romfs, temporaryRoot, language);
            foreach (var garcName in RequiredGarcs)
                CopyRelativeFile(romfs, temporaryRomFs, probe.GetGARCFileName(garcName));

            var config = new GameConfig(probe.Version);
            config.Initialize(temporaryRomFs, temporaryRoot, language);

            var changed = new List<string>();
            if (personal.HasChanges)
            {
                RandomizePersonal(config, personal);
                ModifyPersonal(config, personal);
                SavePersonal(config);
                changed.Add(config.GetGARCFileName("personal"));
            }

            if (learnsets.Enabled)
            {
                var randomizer = new LearnsetRandomizer(config, config.Learnsets)
                {
                    Expand = learnsets.Expand,
                    ExpandTo = Math.Clamp(learnsets.MoveCount, 1, 75),
                    Spread = learnsets.Spread,
                    SpreadTo = Math.Clamp(learnsets.MaxLevel, 1, 100),
                    STAB = learnsets.Stab,
                    STABPercent = Math.Clamp(learnsets.StabPercent, 0, 100),
                    // Windows uses its single “Bias by Type” checkbox for both settings.
                    STABFirst = learnsets.Stab,
                    OrderByPower = learnsets.OrderByPower,
                    Learn4Level1 = learnsets.FourMovesAtLevel1,
                    BannedMoves = learnsets.ExcludeFixedDamage
                        ? [165, 621, .. MoveRandomizer.FixedDamageMoves]
                        : [165, 621],
                };
                randomizer.Execute();
                SaveLearnsets(config);
                changed.Add(config.GetGARCFileName("levelup"));
            }

            if (eggMoves.Enabled)
            {
                RandomizeEggMoves(config, eggMoves);
                changed.Add(config.GetGARCFileName("eggmove"));
            }

            if (moves.HasChanges)
            {
                ModifyMoves(config, moves);
                SaveMoves(config);
                changed.Add(config.GetGARCFileName("move"));
            }

            if (evolutions.Mode != EvolutionMode.None)
            {
                RandomizeEvolutions(config, evolutions);
                SaveEvolutions(config);
                changed.Add(config.GetGARCFileName("evolution"));
            }

            return CreateLayeredFsArchive(request.OutputDirectory, romfs, temporaryRomFs, titleId, changed, "randomizer");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void RandomizePersonal(GameConfig config, PersonalOptions options)
    {
        var randomizer = new PersonalRandomizer(config.Personal.Table, config)
        {
            TypeCount = 18,
            ModifyAbilities = options.RandomizeAbilities,
            AllowWonderGuard = options.AllowWonderGuard,
            ModifyHeldItems = options.RandomizeHeldItems,
            ModifyCatchRate = options.RandomizeCatchRate,
            ModifyLearnsetTM = options.RandomizeTmCompatibility,
            ModifyLearnsetHM = options.RandomizeHmCompatibility,
            ModifyLearnsetTypeTutors = options.RandomizeTypeTutors,
            ModifyLearnsetMoveTutors = config.ORAS && options.RandomizeMoveTutors,
            ModifyStats = options.RandomizeStats,
            ShuffleStats = options.ShuffleStats,
            StatsToRandomize = NormalizeStats(options.StatsToRandomize),
            StatDeviation = Math.Clamp(options.StatDeviation, 1, 95),
            ModifyTypes = options.RandomizeTypes,
            SameTypeChance = Math.Clamp(options.SameTypeChance, 0, 100),
            ModifyEggGroup = options.RandomizeEggGroups,
            SameEggGroupChance = Math.Clamp(options.SameEggGroupChance, 0, 100),
        };
        randomizer.Execute();
    }

    private static int GetLanguage(int? language)
    {
        var value = language ?? 1;
        if (value is < 0 or > 11)
            throw new WorkspaceException("El idioma debe estar entre 0 y 11.");
        return value;
    }

    private static GameConfig InitializeReadOnlyConfig(GameWorkspace workspace, int language)
    {
        var config = new GameConfig(workspace.Version);
        config.Initialize(workspace.RomFsPath, workspace.RootPath, language);
        return config;
    }

    private static GARCFile GetTextGarc(GameConfig config, TextArchiveKind kind) => kind == TextArchiveKind.Story
        ? config.GetGARCData("storytext")
        : config.GARCGameText;

    private static RandomizeResponse CreateLayeredFsArchive(string? outputDirectory, string sourceRomFs, string temporaryRomFs, string titleId, IEnumerable<string> changedFiles, string labelPrefix)
    {
        var outputBase = ResolveOutputBase(outputDirectory, sourceRomFs);
        var label = $"pk3ds-mac-{labelPrefix}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var outputRoot = Path.Combine(outputBase, label);
        var layeredRomFs = Path.Combine(outputRoot, "luma", "titles", titleId.ToUpperInvariant(), "romfs");
        var changed = changedFiles.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var relativePath in changed)
            CopyRelativeFile(temporaryRomFs, layeredRomFs, relativePath);

        var zipPath = Path.Combine(outputBase, $"{label}-LayeredFS.zip");
        ZipFile.CreateFromDirectory(outputRoot, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
        return new RandomizeResponse(outputRoot, zipPath, changed.Select(path => path.Replace(Path.DirectorySeparatorChar, '/')).ToArray());
    }

    private static bool[] NormalizeStats(bool[]? values) => values is { Length: 6 } ? values : [true, true, true, true, true, true];

    /// <summary>
    /// Ports the non-random "Modify All" enhancements from Personal Stats in pk3DS for Windows.
    /// These run after the optional randomizer so a user can combine both operations in one export.
    /// </summary>
    private static void ModifyPersonal(GameConfig config, PersonalOptions options)
    {
        if (!options.HasBulkChanges)
            return;

        // Index 0 is the empty personal entry, not a Pokémon species.
        foreach (var species in config.Personal.Table.Skip(1))
        {
            if (options.RemoveEvYields)
            {
                species.EV_HP = 0;
                species.EV_ATK = 0;
                species.EV_DEF = 0;
                species.EV_SPE = 0;
                species.EV_SPA = 0;
                species.EV_SPD = 0;
            }

            if (options.SetFastGrowth)
                species.EXPGrowth = 5;
            if (options.BaseExperiencePercent is not null)
                species.BaseEXP = Math.Clamp((int)Math.Round(species.BaseEXP * options.BaseExperiencePercent.Value / 100m), 0, ushort.MaxValue);
            if (options.QuickHatch)
                species.HatchCycles = 1;
            if (options.SetCatchRate is not null)
                species.CatchRate = Math.Clamp(options.SetCatchRate.Value, 0, byte.MaxValue);

            if (options.RemoveTutorCompatibility)
            {
                // Windows keeps HMs (bits after index 100) for story progression.
                for (var i = 0; i < Math.Min(101, species.TMHM.Length); i++)
                    species.TMHM[i] = false;
                Array.Fill(species.TypeTutors, false);
                foreach (var tutorSet in species.SpecialTutors)
                    Array.Fill(tutorSet, false);
            }
            if (options.FullTmCompatibility)
            {
                for (var i = 0; i < Math.Min(100, species.TMHM.Length); i++)
                    species.TMHM[i] = true;
            }
            if (options.FullHmCompatibility)
            {
                for (var i = 100; i < species.TMHM.Length; i++)
                    species.TMHM[i] = true;
            }
            if (options.FullMoveTutorCompatibility)
                Array.Fill(species.TypeTutors, true);
        }
    }

    private static void RandomizeEvolutions(GameConfig config, EvolutionOptions options)
    {
        var randomizer = new EvolutionRandomizer(config, config.Evolutions)
        {
            Randomizer =
            {
                rBST = options.MatchBst,
                rEXP = options.MatchExperience,
                rType = options.MatchType,
                L = options.IncludeLegendary,
                E = options.IncludeMythical,
            },
        };
        randomizer.Randomizer.Initialize();
        switch (options.Mode)
        {
            case EvolutionMode.Replacements:
                randomizer.Execute();
                break;
            case EvolutionMode.RemoveTrades:
                randomizer.ExecuteTrade();
                break;
            case EvolutionMode.EveryLevel:
                randomizer.ExecuteEvolveEveryLevel();
                randomizer.Execute();
                break;
        }
    }

    private static void SavePersonal(GameConfig config)
    {
        var files = config.GARCPersonal.Files;
        for (var i = 0; i < files.Length - 1; i++)
            config.Personal.Table[i].Write().CopyTo(files[^1], i * files[i].Length);
        config.GARCPersonal.Files = files;
        config.GARCPersonal.Save();
    }

    private static void SaveLearnsets(GameConfig config)
    {
        var files = config.GARCLearnsets.Files;
        for (var i = 0; i < files.Length; i++)
            files[i] = config.Learnsets[i].Write();
        config.GARCLearnsets.Files = files;
        config.GARCLearnsets.Save();
    }

    private static void SaveEvolutions(GameConfig config)
    {
        var evolutionGarc = config.GetGARCData("evolution");
        evolutionGarc.Files = config.Evolutions.Select(evolution => evolution.Write()).ToArray();
        evolutionGarc.Save();
    }

    private static void ModifyMoves(GameConfig config, MoveOptions options)
    {
        var random = Util.Rand;
        for (var moveId = 1; moveId < config.Moves.Length; moveId++)
        {
            var move = config.Moves[moveId];
            // The Windows editor leaves Struggle and Curse unchanged.
            if (moveId is 165 or 174)
                continue;

            if (options.RandomizeCategory && move.Category > 0)
                move.Category = random.Next(1, 3);
            if (options.RandomizeType)
                move.Type = random.Next(0, 18);
        }

        if (!options.MetronomeMode)
            return;

        // Same values used by the Windows "Metronome Mode" button.
        for (var moveId = 1; moveId < config.Moves.Length; moveId++)
            config.Moves[moveId].PP = moveId switch { 117 => 40, 32 => 1, _ => 0 };
    }

    private static void SaveMoves(GameConfig config)
    {
        var files = config.Moves.Select(move => move.Write()).ToArray();
        config.GARCMoves.Files = config.XY ? files : [Mini.PackMini(files, "WD")];
        config.GARCMoves.Save();
    }

    private static void RandomizeEggMoves(GameConfig config, EggMoveOptions options)
    {
        var garc = config.GetGARCData("eggmove");
        EggMoves[] sets = config.Generation == 6
            ? EggMoves6.GetArray(garc.Files)
            : EggMoves7.GetArray(garc.Files);
        var banned = config.Generation == 7
            ? new List<int>([165, 621, 464, .. Legal.Z_Moves])
            : new List<int>([165, 621]);
        var randomizer = new EggMoveRandomizer(config, sets)
        {
            Expand = options.Expand,
            ExpandTo = Math.Clamp(options.MoveCount, 1, 18),
            STAB = options.Stab,
            STABPercent = Math.Clamp(options.StabPercent, 0, 100),
            BannedMoves = banned.ToArray(),
        };
        randomizer.Execute();
        garc.Files = sets.Select(set => set.Write()).ToArray();
        garc.Save();
    }

    private static string ResolveOutputBase(string? outputDirectory, string romfs)
    {
        var target = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Directory.GetParent(romfs)!.FullName, "pk3ds-mac-output")
            : Path.GetFullPath(outputDirectory.Trim());
        Directory.CreateDirectory(target);
        return target;
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

    private static void CopyRelativeFile(string sourceRoot, string destinationRoot, string relativePath)
    {
        var source = GetChildPath(sourceRoot, relativePath);
        if (!File.Exists(source))
            throw new WorkspaceException($"Falta un archivo necesario en el RomFS: {relativePath}");
        var destination = GetChildPath(destinationRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static string GetChildPath(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!path.StartsWith(normalizedRoot, StringComparison.Ordinal))
            throw new WorkspaceException("La ruta de archivo no es válida.");
        return path;
    }
}

public sealed record WorkspaceRequest(string WorkspacePath);
public sealed record RandomizeRequest(
    string WorkspacePath,
    string? OutputDirectory,
    string? TitleId,
    int? Language,
    bool RandomizeAbilities,
    bool RandomizeHeldItems,
    bool RandomizeLearnsets,
    PersonalOptions? Personal = null,
    LearnsetOptions? Learnsets = null,
    EggMoveOptions? EggMoves = null,
    MoveOptions? Moves = null,
    EvolutionOptions? Evolutions = null);
public sealed record InspectResponse(
    string RomFsPath,
    string GameVersion,
    bool IsComplete,
    string? TitleId,
    string? ExeFsPath,
    string? ExheaderPath,
    ModuleAvailability[] Modules,
    string Note);
public sealed record PickFolderResponse(string Path);
public enum TextArchiveKind { Game, Story }
public sealed record TextCatalogRequest(string WorkspacePath, TextArchiveKind Kind = TextArchiveKind.Game, int? Language = null);
public sealed record TextTableRequest(string WorkspacePath, TextArchiveKind Kind, int TableIndex, int? Language = null);
public sealed record TextExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, TextArchiveKind Kind, int TableIndex, string[] Lines, int? Language = null);
public sealed record TextTableSummary(int Index, string Name, int LineCount);
public sealed record TextCatalogResponse(string GameVersion, TextArchiveKind Kind, TextTableSummary[] Tables);
public sealed record TextTableResponse(TextArchiveKind Kind, int TableIndex, string[] Lines);
public sealed record LearnsetCatalogRequest(string WorkspacePath, int? Language = null);
public sealed record LearnsetTableRequest(string WorkspacePath, int SpeciesIndex, int? Language = null);
public sealed record LearnsetExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int SpeciesIndex, LearnsetEntry[] Entries, int? Language = null);
public sealed record LearnsetSpeciesSummary(int Id, string Name, int MoveCount);
public sealed record NamedEntry(int Id, string Name);
public sealed record LearnsetEntry(int Level, int MoveId);
public sealed record LearnsetCatalogResponse(string GameVersion, LearnsetSpeciesSummary[] Species, NamedEntry[] Moves);
public sealed record LearnsetTableResponse(int SpeciesIndex, LearnsetEntry[] Entries);
public sealed record EggMoveTableRequest(string WorkspacePath, int SpeciesIndex, int? Language = null);
public sealed record EggMoveExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int SpeciesIndex, int[] Moves, int? FormTableIndex = null, int? Language = null);
public sealed record EggMoveTableResponse(int SpeciesIndex, int[] Moves, int FormTableIndex);
public sealed record EvolutionTableRequest(string WorkspacePath, int SpeciesIndex, int? Language = null);
public sealed record EvolutionExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int SpeciesIndex, EvolutionEntry[] Entries, int? Language = null);
public sealed record EvolutionEntry(int Method, int Argument, int Species, int Form, int Level);
public sealed record EvolutionTableResponse(int SpeciesIndex, EvolutionEntry[] Entries);
public sealed record PersonalEntryRequest(string WorkspacePath, int SpeciesIndex, int? Language = null);
public sealed record PersonalExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int SpeciesIndex, int[] Stats, int[] Types, int CatchRate, int[] Abilities, int[] Items, int[] EggGroups, int? Language = null);
public sealed record PersonalEntryResponse(int SpeciesIndex, int[] Stats, int[] Types, int CatchRate, int[] Abilities, int[] Items, int[] EggGroups);
public sealed record MoveEntryRequest(string WorkspacePath,int MoveIndex,int? Language=null);
public sealed record MoveExportRequest(string WorkspacePath,string? OutputDirectory,string? TitleId,int MoveIndex,int Type,int Category,int Power,int Accuracy,int PP,int Priority,int? Language=null);
public sealed record MoveEntryResponse(int MoveIndex,int Type,int Category,int Power,int Accuracy,int PP,int Priority);
public sealed record ItemEntryRequest(string WorkspacePath,int ItemIndex,int? Language=null);
public sealed record ItemExportRequest(string WorkspacePath,string? OutputDirectory,string? TitleId,int ItemIndex,int BuyPrice,int HeldEffect,int HeldArgument,int FlingPower,int EffectField,int EffectBattle,int HealValue,int? Language=null);
public sealed record ItemEntryResponse(int ItemIndex,int BuyPrice,int HeldEffect,int HeldArgument,int FlingPower,int EffectField,int EffectBattle,int HealValue);
public sealed record MegaTableRequest(string WorkspacePath,int SpeciesIndex,int? Language=null);
public sealed record MegaExportRequest(string WorkspacePath,string? OutputDirectory,string? TitleId,int SpeciesIndex,MegaEntry[] Entries,int? Language=null);
public sealed record MegaEntry(int Form,int Method,int Argument,int Auxiliary);
public sealed record MegaTableResponse(int SpeciesIndex,MegaEntry[] Entries);
public sealed record WildAreaCatalogRequest(string WorkspacePath,int? Language=null);
public sealed record WildAreaSummary(int FileNumber,string Name,int TableCount);
public sealed record WildAreaCatalogResponse(WildAreaSummary[] Areas);
public sealed record WildTableRequest(string WorkspacePath, int FileNumber, int TableIndex, int? Language = null);
public sealed record WildEncounterSlot(int Species, int Form, int Rate);
public sealed record WildEncounterCompanionSlot(int Species, int Form);
public sealed record WildEncounterTable(int MinLevel, int MaxLevel, WildEncounterSlot[] Slots, WildEncounterCompanionSlot[][]? SosSlots = null, WildEncounterCompanionSlot[]? WeatherSlots = null);
public sealed record WildTableResponse(int FileNumber, string AreaName, int TableIndex, WildEncounterTable Day, WildEncounterTable Night, NamedEntry[] Species);
public sealed record WildExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int FileNumber, int TableIndex, WildEncounterTable Day, WildEncounterTable Night, int? Language = null);
public sealed record WildGen6CatalogRequest(string WorkspacePath, int? Language = null);
public sealed record WildGen6AreaSummary(int FileIndex, int LocationIndex, string Name, bool HasEncounters);
public sealed record WildGen6CatalogResponse(string Game, WildGen6AreaSummary[] Areas);
public sealed record WildGen6TableRequest(string WorkspacePath, int FileIndex, int? Language = null);
public sealed record WildGen6Slot(int Species, int Form, int MinLevel, int MaxLevel);
public sealed record WildGen6Group(string Name, WildGen6Slot[] Slots);
public sealed record WildGen6TableResponse(int FileIndex, string AreaName, WildGen6Group[] Groups, NamedEntry[] Species);
public sealed record WildGen6ExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int FileIndex, WildGen6Group[] Groups, int? Language = null);
public sealed record StaticCatalogRequest(string WorkspacePath, int? Language = null);
public sealed record StaticGroupSummary(string Id, string Name, int Count);
public sealed record StaticCatalogResponse(StaticGroupSummary[] Groups, NamedEntry[] Species, NamedEntry[] Items, NamedEntry[] Moves);
public sealed record StaticEntryRequest(string WorkspacePath, string Group, int EntryIndex, int? Language = null);
public sealed record StaticEntry(int Species, int Form, int Level, int HeldItem, int? Gender = null, int? Ability = null, int? Nature = null, bool? ShinyLock = null, bool? IsEgg = null, int? SpecialMove = null, int[]? RelearnMoves = null, int[]? IVs = null, int[]? EVs = null, int? Aura = null, int? Ally1 = null, int? Ally2 = null, int? TradeRequestSpecies = null, int? TID = null);
public sealed record StaticEntryResponse(string Group, int EntryIndex, StaticEntry Entry);
public sealed record StaticExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, string Group, int EntryIndex, StaticEntry? Entry, int? Language = null);
public sealed record StaticGen6CatalogRequest(string WorkspacePath, int? Language = null);
public sealed record StaticGen6CatalogResponse(string Game, int Count, NamedEntry[] Species, NamedEntry[] Items, string Warning);
public sealed record StaticGen6EntryRequest(string WorkspacePath, int EntryIndex, int? Language = null);
public sealed record StaticGen6Entry(int Species, int Form, int Level, int HeldItem, int Gender, int Ability, bool ShinyLock, bool IV3);
public sealed record StaticGen6EntryResponse(int EntryIndex, StaticGen6Entry Entry);
public sealed record StaticGen6ExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int EntryIndex, StaticGen6Entry? Entry, int? Language = null);
public sealed record TrainerCatalogRequest(string WorkspacePath, int? Language = null);
public sealed record TrainerSummary(int Id, string Name);
public sealed record TrainerCatalogResponse(TrainerSummary[] Trainers, NamedEntry[] Classes, NamedEntry[] Species, NamedEntry[] Items, NamedEntry[] Moves);
public sealed record TrainerEntryRequest(string WorkspacePath, int TrainerIndex, int? Language = null);
public sealed record TrainerPokemonEntry(int Species, int Form, int Level, int Item, int[] Moves, int Ability, int Gender, int Nature, bool Shiny, int[] IVs, int[] EVs);
public sealed record TrainerEntry(int TrainerClass, int Mode, int[] Items, int AI, bool Flag, int Money, TrainerPokemonEntry[] Team);
public sealed record TrainerEntryResponse(int TrainerIndex, TrainerEntry Entry);
public sealed record TrainerExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int TrainerIndex, TrainerEntry? Entry, int? Language = null);
public sealed record RandomizeResponse(string OutputFolder, string ZipPath, string[] ChangedFiles);
public sealed class WorkspaceException(string message) : Exception(message);

public sealed record PersonalOptions(
    bool RandomizeAbilities = false,
    bool AllowWonderGuard = true,
    bool RandomizeHeldItems = false,
    bool RandomizeCatchRate = false,
    bool RandomizeTmCompatibility = false,
    bool RandomizeHmCompatibility = false,
    bool RandomizeTypeTutors = false,
    bool RandomizeMoveTutors = false,
    bool RandomizeStats = false,
    bool ShuffleStats = false,
    bool[]? StatsToRandomize = null,
    decimal StatDeviation = 25,
    bool RandomizeTypes = false,
    decimal SameTypeChance = 50,
    bool RandomizeEggGroups = false,
    decimal SameEggGroupChance = 50,
    bool RemoveEvYields = false,
    bool SetFastGrowth = false,
    int? BaseExperiencePercent = null,
    bool QuickHatch = false,
    int? SetCatchRate = null,
    bool RemoveTutorCompatibility = false,
    bool FullTmCompatibility = false,
    bool FullHmCompatibility = false,
    bool FullMoveTutorCompatibility = false)
{
    public bool HasBulkChanges => RemoveEvYields || SetFastGrowth || BaseExperiencePercent is not null || QuickHatch || SetCatchRate is not null || RemoveTutorCompatibility || FullTmCompatibility || FullHmCompatibility || FullMoveTutorCompatibility;
    public bool HasChanges => RandomizeAbilities || RandomizeHeldItems || RandomizeCatchRate || RandomizeTmCompatibility || RandomizeHmCompatibility || RandomizeTypeTutors || RandomizeMoveTutors || RandomizeStats || ShuffleStats || RandomizeTypes || RandomizeEggGroups || HasBulkChanges;
    public static PersonalOptions FromLegacy(bool abilities, bool heldItems) => new(RandomizeAbilities: abilities, RandomizeHeldItems: heldItems);
}

public sealed record LearnsetOptions(
    bool Enabled = false,
    bool Expand = true,
    int MoveCount = 25,
    bool Spread = true,
    int MaxLevel = 75,
    bool Stab = true,
    decimal StabPercent = 52.3m,
    bool StabFirst = true,
    bool OrderByPower = true,
    bool FourMovesAtLevel1 = false,
    bool ExcludeFixedDamage = false)
{
    public static LearnsetOptions FromLegacy(bool enabled) => new(Enabled: enabled);
}

public sealed record EggMoveOptions(
    bool Enabled = false,
    bool Expand = true,
    int MoveCount = 18,
    bool Stab = true,
    decimal StabPercent = 32.1m);

public sealed record MoveOptions(
    bool RandomizeType = false,
    bool RandomizeCategory = false,
    bool MetronomeMode = false)
{
    public bool HasChanges => RandomizeType || RandomizeCategory || MetronomeMode;
}

public enum EvolutionMode { None, Replacements, RemoveTrades, EveryLevel }

public sealed record EvolutionOptions(
    EvolutionMode Mode = EvolutionMode.None,
    bool MatchBst = true,
    bool MatchExperience = false,
    bool MatchType = false,
    bool IncludeLegendary = false,
    bool IncludeMythical = false);
