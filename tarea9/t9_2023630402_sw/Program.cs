using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { ok = true }));

IResult HandleGet(HttpRequest req)
{
    try
    {
        string? path = req.Query["nombre"];
        bool descargar = string.Equals(req.Query["descargar"], "si", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(path))
            return Results.BadRequest("Falta parametro nombre");

        string root = Environment.GetEnvironmentVariable("ROOT") ?? throw new Exception("ROOT no definido");

        // Seguridad básica: evitar ../
        path = path.Replace('\\', '/');
        if (path.Contains(".."))
            return Results.BadRequest("Ruta no valida");

        string fullPath = Path.Combine(root, path.TrimStart('/'));

        if (!System.IO.File.Exists(fullPath))
            return Results.NotFound();

        byte[] contenido = System.IO.File.ReadAllBytes(fullPath);
        string nombre = Path.GetFileName(fullPath);

        // MIME
        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(nombre, out var mime))
            mime = "application/octet-stream";

        DateTime fechaMod = System.IO.File.GetLastWriteTime(fullPath);

        // If-Modified-Since -> 304
        if (req.Headers.TryGetValue("If-Modified-Since", out var ims))
        {
            if (DateTime.TryParse(ims.ToString(), out var fechaCliente))
            {
                if (fechaCliente.ToUniversalTime() == fechaMod.ToUniversalTime())
                    return Results.StatusCode(304);
            }
        }

        if (descargar)
        {
            return Results.File(contenido, mime, nombre);
        }
        else
        {
            req.HttpContext.Response.Headers["Last-Modified"] = fechaMod.ToUniversalTime().ToString("R");
            return Results.File(contenido, mime);
        }
    }
    catch (Exception e)
    {
        return Results.BadRequest(e.Message);
    }
}

// Rutas compatibles con tu front y con pruebas directas
app.MapGet("/api/Get", (HttpRequest req) => HandleGet(req));
app.MapGet("/Get", (HttpRequest req) => HandleGet(req));
app.MapGet("/", (HttpRequest req) => HandleGet(req));

app.Run();
