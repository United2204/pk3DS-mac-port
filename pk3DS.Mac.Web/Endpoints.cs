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
        workspace.MapPost("/pick", (IFolderPicker picker) =>
            Results.Ok(new PickFolderResponse(picker.PickFolder("Selecciona la carpeta extraída del juego"))));
        workspace.MapPost("/pick-output", (IFolderPicker picker) =>
            Results.Ok(new PickFolderResponse(picker.PickFolder("Selecciona dónde guardar la salida"))));
    }

    public static void MapEditorEndpoints(this WebApplication app)
    {
        var editors = app.MapGroup("/api/editors");

        editors.MapPost("/text/catalog", (TextCatalogRequest r) => Results.Ok(TextEditor.GetCatalog(r)));
        editors.MapPost("/text/table", (TextTableRequest r) => Results.Ok(TextEditor.GetTable(r)));
        editors.MapPost("/text/export", (TextExportRequest r) => Results.Ok(TextEditor.Export(r)));

        editors.MapPost("/levelup/catalog", (LearnsetCatalogRequest r) => Results.Ok(LearnsetEditor.GetCatalog(r)));
        editors.MapPost("/levelup/table", (LearnsetTableRequest r) => Results.Ok(LearnsetEditor.GetTable(r)));
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

        editors.MapPost("/wild/gen6/areas", (WildGen6CatalogRequest r) => Results.Ok(WildGen6Editor.GetCatalog(r)));
        editors.MapPost("/wild/gen6/table", (WildGen6TableRequest r) => Results.Ok(WildGen6Editor.GetTable(r)));
        editors.MapPost("/wild/gen6/export", (WildGen6ExportRequest r) => Results.Ok(WildGen6Editor.Export(r)));

        editors.MapPost("/static/catalog", (StaticCatalogRequest r) => Results.Ok(StaticEditor.GetCatalog(r)));
        editors.MapPost("/static/entry", (StaticEntryRequest r) => Results.Ok(StaticEditor.GetEntry(r)));
        editors.MapPost("/static/export", (StaticExportRequest r) => Results.Ok(StaticEditor.Export(r)));

        editors.MapPost("/static/gen6/catalog", (StaticGen6CatalogRequest r) => Results.Ok(StaticGen6Editor.GetCatalog(r)));
        editors.MapPost("/static/gen6/entry", (StaticGen6EntryRequest r) => Results.Ok(StaticGen6Editor.GetEntry(r)));
        editors.MapPost("/static/gen6/export", (StaticGen6ExportRequest r) => Results.Ok(StaticGen6Editor.Export(r)));

        editors.MapPost("/trainers/catalog", (TrainerCatalogRequest r) => Results.Ok(TrainerEditor.GetCatalog(r)));
        editors.MapPost("/trainers/entry", (TrainerEntryRequest r) => Results.Ok(TrainerEditor.GetEntry(r)));
        editors.MapPost("/trainers/export", (TrainerExportRequest r) => Results.Ok(TrainerEditor.Export(r)));
    }
}
