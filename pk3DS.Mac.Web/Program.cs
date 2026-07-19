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
