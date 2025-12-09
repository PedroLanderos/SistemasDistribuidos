// Carlos Pineda G. 2025
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;

namespace servicio;

    public static class Get
    {
        [Function("Get")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get")]
            HttpRequest req)
        {
            try
            {
                // obtiene los parámetros que pasan en la URL
                string? path = req.Query["nombre"];
                bool descargar = req.Query["descargar"] == "si";
                string? root = Environment.GetEnvironmentVariable("ROOT");
                byte[] contenido;
                try
                {
                    // lee el contenido solicitado en la petición GET
                    contenido = File.ReadAllBytes(root + path);
                }
                catch (FileNotFoundException)
                {
                    return new NotFoundResult();
                }
                string? nombre = Path.GetFileName(path);
                string? tipo_mime = MimeMapping.GetMimeMapping(nombre);
                DateTime fecha_modificacion = File.GetLastWriteTime(root + path);
                // verifica si viene el encabezado "If-Modified-Since"
                // si es así, compara la fecha que envía el cliente con la fecha del archivo
                // si son iguales regresa el código 304
                string? fecha = req.Headers["If-Modified-Since"];
                if (!string.IsNullOrEmpty(fecha))
                    if (DateTime.Parse(fecha) == fecha_modificacion)
                        return new StatusCodeResult(304);
                if (descargar) // indica al navegador que descargue el archivo
                    return new FileContentResult(contenido, tipo_mime) { FileDownloadName = nombre };
                else // indica al navegador que guarde el contenido en la cache
                    return new FileContentResult(contenido, tipo_mime) { LastModified = fecha_modificacion };
            }
            catch (Exception e)
            {
                return new BadRequestObjectResult(e.Message);
            }
        }
    }
