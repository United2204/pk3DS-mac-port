using System.Diagnostics;
using Microsoft.Extensions.FileProviders;
using pk3DS.Editors;
using pk3DS.Mac.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IFolderPicker, MacFolderPicker>();
builder.Services.AddSingleton<IFilePicker, MacFilePicker>();

var app = builder.Build();
const string address = "http://127.0.0.1:38473";

// Every editor failure is translated here, so the endpoints below carry no error handling.
app.UseMiddleware<WorkspaceExceptionMiddleware>();
var webRoot = new[]
{
    app.Environment.WebRootPath,
    Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
    Path.Combine(app.Environment.ContentRootPath, "pk3DS.Mac.Web", "wwwroot"),
    Path.Combine(AppContext.BaseDirectory, "wwwroot"),
}
.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
.Select(path => Path.GetFullPath(path!))
.FirstOrDefault();
if (webRoot is null)
    throw new DirectoryNotFoundException("No se encontró pk3DS.Mac.Web/wwwroot.");
app.Environment.WebRootPath = webRoot;
app.Environment.WebRootFileProvider = new PhysicalFileProvider(webRoot);
// Keep the existing HTML editors available under a stable prefix while the React shell
// takes over navigation. The iframe bridge uses this prefix to preserve old tools safely.
app.UseStaticFiles(new StaticFileOptions
{
    RequestPath = "/legacy",
    FileProvider = new PhysicalFileProvider(webRoot),
});
app.UseStaticFiles();

app.MapGet("/", () => Results.Redirect("/app/"));
app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html");

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
        var startInfo = OperatingSystem.IsMacOS()
            ? new ProcessStartInfo
            {
                FileName = "open",
                UseShellExecute = false,
            }
            : new ProcessStartInfo(address) { UseShellExecute = true };
        if (OperatingSystem.IsMacOS())
            startInfo.ArgumentList.Add(address);
        Process.Start(startInfo);
    }
    catch
    {
        // The address is logged below if macOS does not have a default browser.
    }
});

app.Logger.LogInformation("pk3DS Mac Web listo en {Address}", address);
app.Run(address);
