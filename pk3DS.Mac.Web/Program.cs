using System.Diagnostics;
using pk3DS.Mac.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<WorkspaceService>();

var app = builder.Build();
const string address = "http://127.0.0.1:38473";

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", address }));
app.MapPost("/api/workspace/inspect", (WorkspaceRequest request, WorkspaceService service) =>
{
    try
    {
        return Results.Ok(service.Inspect(request));
    }
    catch (WorkspaceException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/workspace/pick", (WorkspaceService service) =>
{
    try
    {
        return Results.Ok(service.PickFolder("Selecciona la carpeta extraída del juego"));
    }
    catch (WorkspaceException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/workspace/pick-output", (WorkspaceService service) =>
{
    try
    {
        return Results.Ok(service.PickFolder("Elige dónde guardar el LayeredFS"));
    }
    catch (WorkspaceException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/editors/text/catalog", (TextCatalogRequest request, WorkspaceService service) =>
{
    try
    {
        return Results.Ok(service.GetTextCatalog(request));
    }
    catch (WorkspaceException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Text catalog failed");
        return Results.Problem("No pude leer las tablas de texto de este dump.");
    }
});

app.MapPost("/api/editors/text/table", (TextTableRequest request, WorkspaceService service) =>
{
    try
    {
        return Results.Ok(service.GetTextTable(request));
    }
    catch (WorkspaceException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Text table failed");
        return Results.Problem("No pude leer esa tabla de texto.");
    }
});

app.MapPost("/api/editors/text/export", (TextExportRequest request, WorkspaceService service) =>
{
    try
    {
        return Results.Ok(service.ExportText(request));
    }
    catch (WorkspaceException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Text export failed");
        return Results.Problem("No pude exportar los cambios de texto. Revisá la sintaxis de las variables entre corchetes.");
    }
});

app.MapPost("/api/editors/levelup/catalog", (LearnsetCatalogRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetLearnsetCatalog(request)); }
    catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { app.Logger.LogError(ex, "Learnset catalog failed"); return Results.Problem("No pude leer los movimientos por nivel."); }
});

app.MapPost("/api/editors/levelup/table", (LearnsetTableRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetLearnset(request)); }
    catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { app.Logger.LogError(ex, "Learnset table failed"); return Results.Problem("No pude leer esa lista de movimientos."); }
});

app.MapPost("/api/editors/levelup/export", (LearnsetExportRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.ExportLearnset(request)); }
    catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { app.Logger.LogError(ex, "Learnset export failed"); return Results.Problem("No pude exportar los movimientos por nivel."); }
});

app.MapPost("/api/editors/eggmoves/table", (EggMoveTableRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetEggMoves(request)); }
    catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { app.Logger.LogError(ex, "Egg move table failed"); return Results.Problem("No pude leer los movimientos huevo."); }
});
app.MapPost("/api/editors/eggmoves/export", (EggMoveExportRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.ExportEggMoves(request)); }
    catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { app.Logger.LogError(ex, "Egg move export failed"); return Results.Problem("No pude exportar los movimientos huevo."); }
});
app.MapPost("/api/editors/evolutions/table", (EvolutionTableRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetEvolutionTable(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex, "Evolution table failed"); return Results.Problem("No pude leer las evoluciones."); }
});
app.MapPost("/api/editors/evolutions/export", (EvolutionExportRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.ExportEvolutionTable(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex, "Evolution export failed"); return Results.Problem("No pude exportar las evoluciones."); }
});
app.MapPost("/api/editors/personal/entry", (PersonalEntryRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetPersonalEntry(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex, "Personal entry failed"); return Results.Problem("No pude leer los datos personales."); }
});
app.MapPost("/api/editors/personal/export", (PersonalExportRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.ExportPersonalEntry(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex, "Personal export failed"); return Results.Problem("No pude exportar los datos personales."); }
});
app.MapPost("/api/editors/moves/entry", (MoveEntryRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetMoveEntry(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Move entry failed"); return Results.Problem("No pude leer el movimiento."); }
});
app.MapPost("/api/editors/moves/export", (MoveExportRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.ExportMoveEntry(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Move export failed"); return Results.Problem("No pude exportar el movimiento."); }
});
app.MapPost("/api/editors/items/entry", (ItemEntryRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetItemEntry(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Item entry failed"); return Results.Problem("No pude leer el objeto."); }
});
app.MapPost("/api/editors/items/export", (ItemExportRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.ExportItemEntry(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Item export failed"); return Results.Problem("No pude exportar el objeto."); }
});
app.MapPost("/api/editors/mega/table", (MegaTableRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetMegaTable(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Mega table failed"); return Results.Problem("No pude leer las mega evoluciones."); }
});
app.MapPost("/api/editors/mega/export", (MegaExportRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.ExportMegaTable(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Mega export failed"); return Results.Problem("No pude exportar las mega evoluciones."); }
});
app.MapPost("/api/editors/wild/areas", (WildAreaCatalogRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetWildAreaCatalog(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Wild area catalog failed"); return Results.Problem("No pude leer las áreas de encuentros."); }
});
app.MapPost("/api/editors/wild/table", (WildTableRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetWildTable(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Wild table failed"); return Results.Problem("No pude leer esa tabla de encuentros."); }
});
app.MapPost("/api/editors/wild/export", (WildExportRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.ExportWildTable(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Wild export failed"); return Results.Problem("No pude exportar los encuentros."); }
});
app.MapPost("/api/editors/wild/gen6/areas", (WildGen6CatalogRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetWildGen6Catalog(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Gen6 wild catalog failed"); return Results.Problem("No pude leer las áreas de encuentros."); }
});
app.MapPost("/api/editors/wild/gen6/table", (WildGen6TableRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetWildGen6Table(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Gen6 wild table failed"); return Results.Problem("No pude leer esa tabla de encuentros."); }
});
app.MapPost("/api/editors/wild/gen6/export", (WildGen6ExportRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.ExportWildGen6Table(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Gen6 wild export failed"); return Results.Problem("No pude exportar los encuentros."); }
});
app.MapPost("/api/editors/static/catalog", (StaticCatalogRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetStaticCatalog(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Static catalog failed"); return Results.Problem("No pude leer los encuentros estáticos."); }
});
app.MapPost("/api/editors/static/entry", (StaticEntryRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.GetStaticEntry(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Static entry failed"); return Results.Problem("No pude leer ese encuentro estático."); }
});
app.MapPost("/api/editors/static/export", (StaticExportRequest request, WorkspaceService service) =>
{
    try { return Results.Ok(service.ExportStaticEntry(request)); } catch (WorkspaceException ex) { return Results.BadRequest(new { error = ex.Message }); } catch (Exception ex) { app.Logger.LogError(ex,"Static export failed"); return Results.Problem("No pude exportar el encuentro estático."); }
});

app.MapPost("/api/jobs/randomize", (RandomizeRequest request, WorkspaceService service) =>
{
    try
    {
        return Results.Ok(service.Randomize(request));
    }
    catch (WorkspaceException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Randomization failed");
        return Results.Problem("El núcleo no pudo procesar ese RomFS. El dump debe estar desencriptado y completo.");
    }
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    if (Environment.GetEnvironmentVariable("PK3DS_NO_BROWSER") == "1")
        return;
    try
    {
        Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
    }
    catch
    {
        // The address is printed below if macOS does not have a default browser.
    }
});

app.Logger.LogInformation("pk3DS Mac Web listo en {Address}", address);
app.Run(address);
