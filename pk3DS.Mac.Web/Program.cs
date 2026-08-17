using System.Diagnostics;
using pk3DS.Editors;
using pk3DS.Mac.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IFolderPicker, MacFolderPicker>();
builder.Services.AddSingleton<IFilePicker, MacFilePicker>();

var app = builder.Build();
const string address = "http://127.0.0.1:38473";

// Every editor failure is translated here, so the endpoints below carry no error handling.
app.UseMiddleware<WorkspaceExceptionMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", address }));

app.MapWorkspaceEndpoints();
app.MapEditorEndpoints();
app.MapPost("/api/jobs/randomize", (RandomizeRequest request) => Results.Ok(RandomizerService.Randomize(request)));

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
        // The address is logged below if macOS does not have a default browser.
    }
});

app.Logger.LogInformation("pk3DS Mac Web listo en {Address}", address);
app.Run(address);
