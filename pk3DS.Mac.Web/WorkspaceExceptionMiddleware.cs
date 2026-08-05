using pk3DS.Editors;

namespace pk3DS.Mac.Web;

/// <summary>
/// Turns editor failures into responses, so endpoints stay one line each.
/// <para>
/// A <see cref="WorkspaceException"/> is the editors' way of saying "this request cannot be
/// served" — bad folder, wrong generation, invalid payload — and its message is written for the
/// user, so it goes out as-is with 400. Anything else is a bug or a broken dump: it gets logged
/// with the stack trace and the user sees a generic message instead of internals.
/// </para>
/// </summary>
public sealed class WorkspaceExceptionMiddleware(RequestDelegate next, ILogger<WorkspaceExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (WorkspaceException ex) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            logger.LogError(ex, "Fallo no controlado en {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "El núcleo no pudo procesar ese RomFS. El dump debe estar desencriptado y completo.",
            });
        }
    }
}
