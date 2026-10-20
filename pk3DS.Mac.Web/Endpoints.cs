using pk3DS.Editors;

namespace pk3DS.Mac.Web;

/// <summary>
/// Maps the HTTP surface onto the editors. Every handler is a single call: validation lives in
/// the editors and error translation lives in <see cref="WorkspaceExceptionMiddleware"/>, so a new
/// module is one line here.
/// </summary>
public static class Endpoints
{
    public static void MapWorkspaceEndpoints(this WebApplication app)
    {
        var workspace = app.MapGroup("/api/workspace");

        workspace.MapPost("/inspect", (WorkspaceRequest request) => Results.Ok(WorkspaceInspector.Inspect(request)));
        workspace.MapPost("/backups", (WorkspaceBackupsRequest request) =>
            Results.Ok(WorkspaceBackupTools.List(request)));
        workspace.MapPost("/backups/create", (CreateWorkspaceBackupRequest request) =>
            Results.Ok(WorkspaceBackupTools.CreateBaseline(request)));
        workspace.MapPost("/backups/restore", (RestoreWorkspaceBackupRequest request) =>
            Results.Ok(WorkspaceBackupTools.Restore(request)));
        workspace.MapPost("/build-filesystems", (BuildFileSystemsRequest request) =>
            Results.Ok(ProjectTools.BuildFileSystems(request)));
        workspace.MapPost("/extract", (ExtractProjectRequest request) =>
            Results.Ok(ProjectTools.ExtractProject(request)));
        workspace.MapPost("/extract/jobs", (ExtractProjectRequest request, ExtractionJobService jobs) =>
        {
            var job = jobs.Start(request);
            return job is null
                ? Results.Conflict(new { error = "Ya hay una extracción en curso. Esperá a que termine antes de iniciar otra." })
                : Results.Accepted(job.StatusUrl, job);
        });
        workspace.MapGet("/extract/jobs/{jobId}", (string jobId, ExtractionJobService jobs) =>
        {
            var job = jobs.Get(jobId);
            return job is null ? Results.NotFound() : Results.Ok(job);
        });
        workspace.MapPost("/unpack-exefs", (ExeFsUnpackRequest request) =>
            Results.Ok(ExeFsProjectTools.Unpack(request)));
        workspace.MapPost("/pack-exefs", (ExeFsPackRequest request) =>
            Results.Ok(ExeFsProjectTools.Pack(request)));
        workspace.MapPost("/unpack-romfs", (RomFsUnpackRequest request) =>
            Results.Ok(RomFsProjectTools.Unpack(request)));
        workspace.MapPost("/pack-romfs", (RomFsPackRequest request) =>
            Results.Ok(RomFsProjectTools.Pack(request)));
        workspace.MapPost("/rebuild-rom", (RebuildRomRequest request) =>
            Results.Ok(ProjectTools.RebuildRom(request)));
        workspace.MapPost("/rebuild-crr", (RebuildCrrRequest request) =>
            Results.Ok(ProjectTools.RebuildCrr(request)));
        workspace.MapPost("/rebuild-cia", (RebuildCiaRequest request) =>
            Results.Ok(ProjectTools.RebuildCia(request)));
        workspace.MapPost("/redirect-patch", (RedirectPatchRequest request) =>
            Results.Ok(ProjectTools.CreateRedirectPatch(request)));
        workspace.MapPost("/redirect-patch/options", (WorkspaceRequest request) =>
            Results.Ok(ProjectTools.GetRedirectPatchOptions(request)));
        workspace.MapPost("/unpack-auto", (AutoUnpackRequest request) =>
            Results.Ok(ProjectTools.UnpackAuto(request)));
        workspace.MapPost("/pack-auto", (AutoPackRequest request) =>
            Results.Ok(ProjectTools.PackAuto(request)));
        workspace.MapPost("/unpack-garc", (UnpackGarcRequest request) =>
            Results.Ok(ProjectTools.UnpackGarc(request)));
        workspace.MapPost("/pack-garc", (PackGarcRequest request) =>
            Results.Ok(ProjectTools.PackGarc(request)));
        workspace.MapPost("/shuffle-garc", (ShuffleGarcRequest request) =>
            Results.Ok(ProjectTools.ShuffleGarc(request)));
        workspace.MapPost("/unpack-darc", (UnpackDarcRequest request) =>
            Results.Ok(ProjectTools.UnpackDarc(request)));
        workspace.MapPost("/pack-darc", (PackDarcRequest request) =>
            Results.Ok(ProjectTools.PackDarc(request)));
        workspace.MapPost("/unpack-sarc", (UnpackSarcRequest request) =>
            Results.Ok(ProjectTools.UnpackSarc(request)));
        workspace.MapPost("/pack-sarc", (PackSarcRequest request) =>
            Results.Ok(ProjectTools.PackSarc(request)));
        workspace.MapPost("/unpack-alyt", (UnpackAlytRequest request) =>
            Results.Ok(ProjectTools.UnpackAlyt(request)));
        workspace.MapPost("/pack-alyt", (PackAlytRequest request) =>
            Results.Ok(ProjectTools.PackAlyt(request)));
        workspace.MapPost("/unpack-shuffle-arc", (UnpackShuffleArcRequest request) =>
            Results.Ok(ProjectTools.UnpackShuffleArc(request)));
        workspace.MapPost("/unpack-gar", (UnpackGarRequest request) =>
            Results.Ok(ProjectTools.UnpackGar(request)));
        workspace.MapPost("/unpack-farc", (UnpackFarcRequest request) =>
            Results.Ok(ProjectTools.UnpackFarc(request)));
        workspace.MapPost("/pack-farc", (PackFarcRequest request) =>
            Results.Ok(ProjectTools.PackFarc(request)));
        workspace.MapPost("/unpack-mini", (UnpackMiniRequest request) =>
            Results.Ok(ProjectTools.UnpackMini(request)));
        workspace.MapPost("/pack-mini", (PackMiniRequest request) =>
            Results.Ok(ProjectTools.PackMini(request)));
        workspace.MapPost("/convert-image", (ConvertImageRequest request) =>
            Results.Ok(ProjectTools.ConvertImage(request)));
        workspace.MapPost("/preview-image", (PreviewImageRequest request) =>
            Results.Ok(ProjectTools.PreviewImage(request)));
        workspace.MapPost("/smdh/inspect", (SmdhInspectRequest request) =>
            Results.Ok(ProjectTools.InspectSmdh(request)));
        workspace.MapPost("/smdh/export", (SmdhExportRequest request) =>
            Results.Ok(ProjectTools.ExportSmdh(request)));
        workspace.MapPost("/smdh/update", (SmdhUpdateRequest request) =>
            Results.Ok(ProjectTools.UpdateSmdh(request)));
        workspace.MapPost("/smdh/import", (SmdhImportRequest request) =>
            Results.Ok(ProjectTools.ImportSmdh(request)));
        workspace.MapPost("/smdh/backups", (SmdhBackupsRequest request) =>
            Results.Ok(ProjectTools.GetSmdhBackups(request)));
        workspace.MapPost("/smdh/restore", (SmdhRestoreRequest request) =>
            Results.Ok(ProjectTools.RestoreSmdhBackup(request)));
        workspace.MapPost("/lz11", (Lz11Request request) =>
            Results.Ok(ProjectTools.ProcessLz11(request)));
        workspace.MapPost("/blz", (BlzRequest request) =>
            Results.Ok(ProjectTools.ProcessBlz(request)));
        workspace.MapPost("/pick", (IFolderPicker picker) =>
            Results.Ok(new PickFolderResponse(picker.PickFolder("Selecciona la carpeta extraída del juego"))));
        workspace.MapPost("/pick-output", (IFolderPicker picker) =>
            Results.Ok(new PickFolderResponse(picker.PickFolder("Selecciona dónde guardar la salida"))));
        workspace.MapPost("/pick-redirect-patch", (IFolderPicker picker) =>
            Results.Ok(new PickFolderResponse(picker.PickFolder("Selecciona la carpeta del parche de redirección"))));
        workspace.MapPost("/pick-file", (IFilePicker picker) =>
            Results.Ok(new PickFileResponse(picker.PickFile(
                "Selecciona un archivo CXI, 3DS o CIA", "cxi", "3ds", "cia"))));
        workspace.MapPost("/pick-archive", (IFilePicker picker) =>
            Results.Ok(new PickFileResponse(picker.PickFile("Selecciona un archivo GAR, GARC, Mini, ALYT, Shuffle ARC, DARC, SARC o FARC"))));
        workspace.MapPost("/pick-tool", (IFilePicker picker) =>
            Results.Ok(new PickFileResponse(picker.PickFile("Selecciona el ejecutable makerom"))));
        workspace.MapPost("/pick-image", (IFilePicker picker) =>
            Results.Ok(new PickFileResponse(picker.PickFile("Selecciona una imagen PNG, BCLIM o BFLIM"))));
        workspace.MapPost("/pick-exefs-file", (IFilePicker picker) =>
            Results.Ok(new PickFileResponse(picker.PickFile("Selecciona un archivo exefs.bin"))));
        workspace.MapPost("/pick-exefs-folder", (IFolderPicker picker) =>
            Results.Ok(new PickFolderResponse(picker.PickFolder("Selecciona la carpeta con los archivos ExeFS"))));
        workspace.MapPost("/pick-romfs-file", (IFilePicker picker) =>
            Results.Ok(new PickFileResponse(picker.PickFile("Selecciona un archivo romfs.bin"))));
        workspace.MapPost("/pick-romfs-folder", (IFolderPicker picker) =>
            Results.Ok(new PickFolderResponse(picker.PickFolder("Selecciona la carpeta raíz del RomFS"))));
        workspace.MapPost("/pick-smdh", (IFilePicker picker) =>
            Results.Ok(new PickFileResponse(picker.PickFile("Selecciona un archivo SMDH icon.bin"))));
        workspace.MapPost("/pick-any-file", (IFilePicker picker) =>
            Results.Ok(new PickFileResponse(picker.PickFile("Selecciona un archivo para comprimir o descomprimir con LZ11"))));
    }

    public static void MapEditorEndpoints(this WebApplication app)
    {
        var editors = app.MapGroup("/api/editors");

        editors.MapPost("/text/catalog", (TextCatalogRequest r) => Results.Ok(TextEditor.GetCatalog(r)));
        editors.MapPost("/text/table", (TextTableRequest r) => Results.Ok(TextEditor.GetTable(r)));
        editors.MapPost("/text/all", (TextAllTablesRequest r) => Results.Ok(TextEditor.GetAllTables(r)));
        editors.MapPost("/text/export", (TextExportRequest r) => Results.Ok(TextEditor.Export(r)));
        editors.MapPost("/text/export-all", (TextArchiveExportRequest r) => Results.Ok(TextEditor.ExportAll(r)));

        editors.MapPost("/levelup/catalog", (LearnsetCatalogRequest r) => Results.Ok(LearnsetEditor.GetCatalog(r)));
        editors.MapPost("/levelup/table", (LearnsetTableRequest r) => Results.Ok(LearnsetEditor.GetTable(r)));
        editors.MapPost("/levelup/metronome", (LearnsetMetronomeExportRequest r) => Results.Ok(LearnsetEditor.ExportMetronome(r)));
        editors.MapPost("/levelup/export", (LearnsetExportRequest r) => Results.Ok(LearnsetEditor.Export(r)));

        editors.MapPost("/eggmoves/table", (EggMoveTableRequest r) => Results.Ok(EggMoveEditor.GetTable(r)));
        editors.MapPost("/eggmoves/export", (EggMoveExportRequest r) => Results.Ok(EggMoveEditor.Export(r)));

        editors.MapPost("/evolutions/table", (EvolutionTableRequest r) => Results.Ok(EvolutionEditor.GetTable(r)));
        editors.MapPost("/evolutions/export", (EvolutionExportRequest r) => Results.Ok(EvolutionEditor.Export(r)));

        editors.MapPost("/personal/entry", (PersonalEntryRequest r) => Results.Ok(PersonalEditor.GetEntry(r)));
        editors.MapPost("/personal/export", (PersonalExportRequest r) => Results.Ok(PersonalEditor.Export(r)));

        editors.MapPost("/moves/entry", (MoveEntryRequest r) => Results.Ok(MoveEditor.GetEntry(r)));
        editors.MapPost("/moves/export", (MoveExportRequest r) => Results.Ok(MoveEditor.Export(r)));

        editors.MapPost("/items/entry", (ItemEntryRequest r) => Results.Ok(ItemEditor.GetEntry(r)));
        editors.MapPost("/items/export", (ItemExportRequest r) => Results.Ok(ItemEditor.Export(r)));

        editors.MapPost("/mega/table", (MegaTableRequest r) => Results.Ok(MegaEditor.GetTable(r)));
        editors.MapPost("/mega/export", (MegaExportRequest r) => Results.Ok(MegaEditor.Export(r)));

        editors.MapPost("/wild/areas", (WildAreaCatalogRequest r) => Results.Ok(WildEditor.GetCatalog(r)));
        editors.MapPost("/wild/table", (WildTableRequest r) => Results.Ok(WildEditor.GetTable(r)));
        editors.MapPost("/wild/export", (WildExportRequest r) => Results.Ok(WildEditor.Export(r)));
        editors.MapPost("/wild/gen7/reports/export", (WildGen7BatchReportRequest r) => Results.Ok(WildEditor.ExportBatchReport(r)));

        editors.MapPost("/wild/gen6/areas", (WildGen6CatalogRequest r) => Results.Ok(WildGen6Editor.GetCatalog(r)));
        editors.MapPost("/wild/gen6/table", (WildGen6TableRequest r) => Results.Ok(WildGen6Editor.GetTable(r)));
        editors.MapPost("/wild/gen6/export", (WildGen6ExportRequest r) => Results.Ok(WildGen6Editor.Export(r)));
        editors.MapPost("/wild/gen6/reports/export", (WildGen6BatchReportRequest r) => Results.Ok(WildGen6Editor.ExportBatchReport(r)));

        editors.MapPost("/owse/catalog", (OverworldCatalogRequest r) => Results.Ok(OverworldEditor.GetCatalog(r)));
        editors.MapPost("/owse/entry", (OverworldScriptEntryRequest r) => Results.Ok(OverworldEditor.GetEntry(r)));
        editors.MapPost("/owse/gen6/zone", (OverworldGen6ZoneRequest r) => Results.Ok(OverworldEditor.GetGen6Zone(r)));
        editors.MapPost("/owse/gen6/export", (OverworldGen6ExportRequest r) => Results.Ok(OverworldEditor.Export(r)));
        editors.MapPost("/owse/gen6/script", (OverworldGen6ScriptExportRequest r) => Results.Ok(OverworldEditor.ExportScript(r)));
        editors.MapPost("/owse/script/export", (OverworldScriptExportRequest r) => Results.Ok(OverworldEditor.ExportScript(r)));
        editors.MapPost("/owse/gen7/zone", (OverworldGen7ZoneRequest r) => Results.Ok(OverworldEditor.GetGen7Zone(r)));
        editors.MapPost("/owse/gen7/zone/export", (OverworldGen7ZoneExportRequest r) => Results.Ok(OverworldEditor.ExportGen7Zone(r)));
        editors.MapPost("/owse/gen7/entities", (OverworldGen7EntityRequest r) => Results.Ok(OverworldEditor.GetGen7Entities(r)));
        editors.MapPost("/owse/gen7/entities/export", (OverworldGen7EntityExportRequest r) => Results.Ok(OverworldEditor.ExportGen7Entities(r)));
        editors.MapPost("/owse/gen7/entities/raw-export", (OverworldGen7EntityRawExportRequest r) => Results.Ok(OverworldEditor.ExportGen7EntityRaw(r)));
        editors.MapPost("/owse/gen7/map-resources", (OverworldGen7MapResourceRequest r) => Results.Ok(OverworldEditor.GetGen7MapResources(r)));
        editors.MapPost("/owse/gen7/map-resources/export", (OverworldGen7MapResourceExportRequest r) => Results.Ok(OverworldEditor.ExportGen7MapResources(r)));
        editors.MapPost("/owse/gen6/map", (OverworldGen6MapRequest r) => Results.Ok(OverworldEditor.GetGen6Map(r)));
        editors.MapPost("/owse/gen6/map/export", (OverworldGen6MapExportRequest r) => Results.Ok(OverworldEditor.ExportMap(r)));
        editors.MapPost("/owse/gen6/maps/export", (OverworldGen6MapBatchExportRequest r) => Results.Ok(OverworldEditor.ExportGen6Maps(r)));
        editors.MapPost("/owse/gen6/zones/export", (OverworldGen6ZoneBatchExportRequest r) => Results.Ok(OverworldEditor.ExportGen6Zones(r)));
        editors.MapPost("/owse/gen6/scripts/export", (OverworldGen6ScriptBatchExportRequest r) => Results.Ok(OverworldEditor.ExportGen6Scripts(r)));
        editors.MapPost("/owse/gen7/scripts/export", (OverworldGen7ScriptBatchExportRequest r) => Results.Ok(OverworldEditor.ExportGen7Scripts(r)));

        editors.MapPost("/static/catalog", (StaticCatalogRequest r) => Results.Ok(StaticEditor.GetCatalog(r)));
        editors.MapPost("/static/entry", (StaticEntryRequest r) => Results.Ok(StaticEditor.GetEntry(r)));
        editors.MapPost("/static/export", (StaticExportRequest r) => Results.Ok(StaticEditor.Export(r)));

        editors.MapPost("/static/gen6/catalog", (StaticGen6CatalogRequest r) => Results.Ok(StaticGen6Editor.GetCatalog(r)));
        editors.MapPost("/static/gen6/entry", (StaticGen6EntryRequest r) => Results.Ok(StaticGen6Editor.GetEntry(r)));
        editors.MapPost("/static/gen6/export", (StaticGen6ExportRequest r) => Results.Ok(StaticGen6Editor.Export(r)));
        editors.MapPost("/static/gen6/randomize", (StaticGen6RandomizeRequest r) => Results.Ok(StaticGen6Randomizer.Randomize(r)));
        editors.MapPost("/static/gen7/randomize", (StaticGen7RandomizeRequest r) => Results.Ok(StaticGen7Randomizer.Randomize(r)));

        editors.MapPost("/gift/gen6/catalog", (GiftGen6CatalogRequest r) => Results.Ok(GiftGen6Editor.GetCatalog(r)));
        editors.MapPost("/gift/gen6/entry", (GiftGen6EntryRequest r) => Results.Ok(GiftGen6Editor.GetEntry(r)));
        editors.MapPost("/gift/gen6/export", (GiftGen6ExportRequest r) => Results.Ok(GiftGen6Editor.Export(r)));
        editors.MapPost("/gift/gen6/randomize", (GiftGen6RandomizeRequest r) => Results.Ok(GiftGen6Randomizer.Randomize(r)));

        editors.MapPost("/tutors/gen6/table", (TutorGen6TableRequest r) => Results.Ok(TutorGen6Editor.GetTable(r)));
        editors.MapPost("/tutors/gen6/export", (TutorGen6ExportRequest r) => Results.Ok(TutorGen6Editor.Export(r)));
        editors.MapPost("/marts/gen6/table", (MartTableRequest r) => Results.Ok(MartGen6Editor.GetTable(r)));
        editors.MapPost("/marts/gen6/export", (MartExportRequest r) => Results.Ok(MartGen6Editor.Export(r)));

        editors.MapPost("/trainers/catalog", (TrainerCatalogRequest r) => Results.Ok(TrainerEditor.GetCatalog(r)));
        editors.MapPost("/trainers/entry", (TrainerEntryRequest r) => Results.Ok(TrainerEditor.GetEntry(r)));
        editors.MapPost("/trainers/export", (TrainerExportRequest r) => Results.Ok(TrainerEditor.Export(r)));

        editors.MapPost("/tmhm/table", (TmHmTableRequest r) => Results.Ok(TmHmEditor.GetTable(r)));
        editors.MapPost("/tmhm/export", (TmHmExportRequest r) => Results.Ok(TmHmEditor.Export(r)));

        editors.MapPost("/pickup/gen6/table", (PickupGen6TableRequest r) => Results.Ok(PickupGen6Editor.GetTable(r)));
        editors.MapPost("/pickup/gen6/export", (PickupGen6ExportRequest r) => Results.Ok(PickupGen6Editor.Export(r)));

        editors.MapPost("/shiny-rate/table", (ShinyRateTableRequest r) => Results.Ok(ShinyRateEditor.GetTable(r)));
        editors.MapPost("/shiny-rate/export", (ShinyRateExportRequest r) => Results.Ok(ShinyRateEditor.Export(r)));

        editors.MapPost("/marts/table", (MartTableRequest r) => Results.Ok(MartEditor.GetTable(r)));
        editors.MapPost("/marts/export", (MartExportRequest r) => Results.Ok(MartEditor.Export(r)));

        editors.MapPost("/opowers/table", (OPowerTableRequest r) => Results.Ok(OPowerEditor.GetTable(r)));
        editors.MapPost("/opowers/export", (OPowerExportRequest r) => Results.Ok(OPowerEditor.Export(r)));

        editors.MapPost("/typechart/table", (TypeChartTableRequest r) => Results.Ok(TypeChartEditor.GetTable(r)));
        editors.MapPost("/typechart/export", (TypeChartExportRequest r) => Results.Ok(TypeChartEditor.Export(r)));

        editors.MapPost("/starters/table", (StarterTableRequest r) => Results.Ok(StarterEditor.GetTable(r)));
        editors.MapPost("/starters/export", (StarterExportRequest r) => Results.Ok(StarterEditor.Export(r)));
        editors.MapPost("/starters/randomize", (StarterRandomizeRequest r) => Results.Ok(StarterGen6Randomizer.Randomize(r)));

        editors.MapPost("/tutors/table", (TutorTableRequest r) => Results.Ok(TutorEditor.GetTable(r)));
        editors.MapPost("/tutors/export", (TutorExportRequest r) => Results.Ok(TutorEditor.Export(r)));

        editors.MapPost("/pickup/table", (PickupTableRequest r) => Results.Ok(PickupEditor.GetTable(r)));
        editors.MapPost("/pickup/export", (PickupExportRequest r) => Results.Ok(PickupEditor.Export(r)));

        editors.MapPost("/maison/catalog", (MaisonCatalogRequest r) => Results.Ok(MaisonEditor.GetCatalog(r)));
        editors.MapPost("/maison/trainer", (MaisonTrainerRequest r) => Results.Ok(MaisonEditor.GetTrainer(r)));
        editors.MapPost("/maison/pokemon", (MaisonPokemonRequest r) => Results.Ok(MaisonEditor.GetPokemon(r)));
        editors.MapPost("/maison/trainer/export", (MaisonTrainerExportRequest r) => Results.Ok(MaisonEditor.ExportTrainer(r)));
        editors.MapPost("/maison/pokemon/export", (MaisonPokemonExportRequest r) => Results.Ok(MaisonEditor.ExportPokemon(r)));

        editors.MapPost("/titlescreen/catalog", (TitleScreenCatalogRequest r) => Results.Ok(TitleScreenEditor.GetCatalog(r)));
        editors.MapPost("/titlescreen/preview", (TitleScreenPreviewRequest r) => Results.Ok(TitleScreenEditor.Preview(r)));
        editors.MapPost("/titlescreen/export", (TitleScreenExportRequest r) => Results.Ok(TitleScreenEditor.Export(r)));
        editors.MapPost("/titlescreen/replace", (TitleScreenReplaceRequest r) => Results.Ok(TitleScreenEditor.Replace(r)));
        editors.MapPost("/titlescreen/replace-garc", (TitleScreenReplaceRequest r) => Results.Ok(TitleScreenEditor.ReplaceGarc(r)));
        editors.MapPost("/titlescreen/apply", (TitleScreenApplyRequest r) => Results.Ok(TitleScreenEditor.Apply(r)));
        editors.MapPost("/titlescreen/replace-garc-batch", (TitleScreenBatchReplaceRequest r) => Results.Ok(TitleScreenEditor.ReplaceGarcBatch(r)));
        editors.MapPost("/titlescreen/apply-batch", (TitleScreenBatchApplyRequest r) => Results.Ok(TitleScreenEditor.ApplyBatch(r)));
        editors.MapPost("/titlescreen/backups", (TitleScreenBackupsRequest r) => Results.Ok(TitleScreenEditor.GetBackups(r)));
        editors.MapPost("/titlescreen/restore", (TitleScreenRestoreRequest r) => Results.Ok(TitleScreenEditor.RestoreBackup(r)));
    }
}
