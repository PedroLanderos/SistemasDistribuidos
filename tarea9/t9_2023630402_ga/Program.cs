// Microservicio Gestión de Artículos (GA) - t9_2023630402_ga
// ASP.NET Core Minimal API + MySQL
// Endpoints:
// POST /ga/alta_articulo
// GET  /ga/consulta_articulos?palabra=...&id_usuario=...&token=...
// GET  /ga/consulta_articulo?id_articulo=...&id_usuario=...&token=...   <-- NUEVO

using System.Text;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// -------------------- Helpers --------------------

IResult Bad(string msg) => Results.BadRequest(JsonConvert.SerializeObject(new { mensaje = msg }));

string RequireEnv(string name)
    => Environment.GetEnvironmentVariable(name) ?? throw new Exception($"{name} no definido");

MySqlConnection OpenDb()
{
    var host = Environment.GetEnvironmentVariable("MYSQL_HOST") ?? throw new Exception("MYSQL_HOST no definido");
    var user = Environment.GetEnvironmentVariable("MYSQL_USER") ?? throw new Exception("MYSQL_USER no definido");
    var pass = Environment.GetEnvironmentVariable("MYSQL_PASSWORD") ?? throw new Exception("MYSQL_PASSWORD no definido");
    var db   = Environment.GetEnvironmentVariable("MYSQL_DB") ?? throw new Exception("MYSQL_DB no definido");
    var ssl  = Environment.GetEnvironmentVariable("MYSQL_SSLMODE") ?? "Preferred";

    var cs = $"Server={host};UserID={user};Password={pass};Database={db};SslMode={ssl};";
    var conn = new MySqlConnection(cs);
    conn.Open();
    return conn;
}

var http = new HttpClient();

// Verifica acceso llamando a GU: GET {GU_BASEURL}/gu/verifica_acceso?id_usuario=...&token=...
async Task<bool> VerificaAccesoAsync(int idUsuario, string token)
{
    var guBase = RequireEnv("GU_BASEURL").TrimEnd('/');
    var url = $"{guBase}/gu/verifica_acceso?id_usuario={idUsuario}&token={Uri.EscapeDataString(token)}";

    using var resp = await http.GetAsync(url);
    if (!resp.IsSuccessStatusCode) return false;

    var text = (await resp.Content.ReadAsStringAsync()).Trim();

    if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return true;
    if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return false;
    if (bool.TryParse(text, out var b)) return b;

    return false;
}

// Llama a GC para registrar stock/cantidad: POST {GC_BASEURL}/gc/alta_articulo
async Task CallGcAltaArticuloAsync(long idArticulo, int cantidad, int idUsuario, string token)
{
    var gcBase = RequireEnv("GC_BASEURL").TrimEnd('/');
    var url = $"{gcBase}/gc/alta_articulo";

    var payload = new
    {
        id_articulo = idArticulo,
        cantidad = cantidad,
        id_usuario = idUsuario,
        token = token
    };

    var json = JsonConvert.SerializeObject(payload);
    using var content = new StringContent(json, Encoding.UTF8, "application/json");

    using var resp = await http.PostAsync(url, content);
    if (!resp.IsSuccessStatusCode)
    {
        var body = await resp.Content.ReadAsStringAsync();
        throw new Exception($"GC alta_articulo falló: {(int)resp.StatusCode} {body}");
    }
}

// -------------------- Endpoints --------------------

app.MapGet("/health", () => Results.Ok(new { ok = true }));

// POST /ga/alta_articulo
// Body: { nombre, descripcion, precio, cantidad, foto(base64 opcional), id_usuario, token }
app.MapPost("/ga/alta_articulo", async (HttpRequest req) =>
{
    try
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var art = JsonConvert.DeserializeObject<AltaArticuloRequest>(body);

        if (art == null) return Bad("Se esperan los datos del artículo");
        if (art.id_usuario == null) return Bad("Falta id_usuario");
        if (string.IsNullOrWhiteSpace(art.token)) return Bad("Falta token");
        if (string.IsNullOrWhiteSpace(art.nombre)) return Bad("Falta nombre");
        if (string.IsNullOrWhiteSpace(art.descripcion)) return Bad("Falta descripción");
        if (art.precio == null || art.precio <= 0) return Bad("Precio inválido");
        if (art.cantidad == null || art.cantidad <= 0) return Bad("Cantidad inválida");
        // foto opcional

        bool ok = await VerificaAccesoAsync(art.id_usuario.Value, art.token!);
        if (!ok) return Bad("Acceso denegado");

        using var conn = OpenDb();
        using var tx = conn.BeginTransaction();

        try
        {
            using var cmd1 = new MySqlCommand(
                "INSERT INTO stock(id_articulo, nombre, descripcion, precio) VALUES (0,@n,@d,@p)",
                conn, tx);

            cmd1.Parameters.AddWithValue("@n", art.nombre);
            cmd1.Parameters.AddWithValue("@d", art.descripcion);
            cmd1.Parameters.AddWithValue("@p", art.precio);

            cmd1.ExecuteNonQuery();
            long idArticulo = cmd1.LastInsertedId;

            if (!string.IsNullOrWhiteSpace(art.foto))
            {
                using var cmd2 = new MySqlCommand(
                    "INSERT INTO fotos_articulos(foto, id_articulo) VALUES (@f,@id)",
                    conn, tx);

                cmd2.Parameters.AddWithValue("@f", Convert.FromBase64String(art.foto));
                cmd2.Parameters.AddWithValue("@id", idArticulo);
                cmd2.ExecuteNonQuery();
            }

            await CallGcAltaArticuloAsync(idArticulo, art.cantidad.Value, art.id_usuario.Value, art.token!);

            tx.Commit();
            return Results.Ok(new { mensaje = "Se dio de alta el artículo", id_articulo = idArticulo });
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
    catch (Exception e)
    {
        return Bad(e.Message);
    }
});

// GET /ga/consulta_articulos?palabra=...&id_usuario=...&token=...
app.MapGet("/ga/consulta_articulos", async (HttpRequest req) =>
{
    try
    {
        string? palabra = req.Query["palabra"];
        string? token = req.Query["token"];
        if (!int.TryParse(req.Query["id_usuario"], out var idUsuario)) return Bad("Falta id_usuario");

        if (string.IsNullOrWhiteSpace(palabra)) return Bad("Debe indicar una palabra clave");
        if (string.IsNullOrWhiteSpace(token)) return Bad("Falta token");

        bool ok = await VerificaAccesoAsync(idUsuario, token!);
        if (!ok) return Bad("Acceso denegado");

        using var conn = OpenDb();

        var lista = new List<ArticuloResponse>();

        using var cmd = new MySqlCommand(
            @"SELECT a.id_articulo,a.nombre,a.descripcion,a.precio,
                     b.foto,LENGTH(b.foto)
              FROM stock a
              LEFT JOIN fotos_articulos b ON a.id_articulo=b.id_articulo
              WHERE a.nombre LIKE @p OR a.descripcion LIKE @p",
            conn);

        cmd.Parameters.AddWithValue("@p", "%" + palabra + "%");

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var art = new ArticuloResponse
            {
                id_articulo = r.GetInt32(0),
                nombre = r.GetString(1),
                descripcion = r.GetString(2),
                precio = r.GetDecimal(3),
                foto = null
            };

            if (!r.IsDBNull(4))
            {
                int len = r.GetInt32(5);
                byte[] foto = new byte[len];
                r.GetBytes(4, 0, foto, 0, len);
                art.foto = Convert.ToBase64String(foto);
            }

            lista.Add(art);
        }

        return Results.Ok(lista);
    }
    catch (Exception e)
    {
        return Bad(e.Message);
    }
});

// ✅ NUEVO: GET /ga/consulta_articulo?id_articulo=...&id_usuario=...&token=...
app.MapGet("/ga/consulta_articulo", async (HttpRequest req) =>
{
    try
    {
        if (!int.TryParse(req.Query["id_articulo"], out var idArticulo)) return Bad("Falta id_articulo");
        if (!int.TryParse(req.Query["id_usuario"], out var idUsuario)) return Bad("Falta id_usuario");

        string? token = req.Query["token"];
        if (string.IsNullOrWhiteSpace(token)) return Bad("Falta token");

        bool ok = await VerificaAccesoAsync(idUsuario, token!);
        if (!ok) return Bad("Acceso denegado");

        using var conn = OpenDb();

        using var cmd = new MySqlCommand(
            @"SELECT a.id_articulo,a.nombre,a.descripcion,a.precio,
                     b.foto,LENGTH(b.foto)
              FROM stock a
              LEFT JOIN fotos_articulos b ON a.id_articulo=b.id_articulo
              WHERE a.id_articulo=@id
              LIMIT 1",
            conn);

        cmd.Parameters.AddWithValue("@id", idArticulo);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return Results.NotFound();

        var art = new ArticuloResponse
        {
            id_articulo = r.GetInt32(0),
            nombre = r.GetString(1),
            descripcion = r.GetString(2),
            precio = r.GetDecimal(3),
            foto = null
        };

        if (!r.IsDBNull(4))
        {
            int len = r.GetInt32(5);
            byte[] foto = new byte[len];
            r.GetBytes(4, 0, foto, 0, len);
            art.foto = Convert.ToBase64String(foto);
        }

        return Results.Ok(art);
    }
    catch (Exception e)
    {
        return Bad(e.Message);
    }
});

app.Run();

// -------------------- DTOs --------------------

class AltaArticuloRequest
{
    public int? id_usuario { get; set; }
    public string? token { get; set; }
    public string? nombre { get; set; }
    public string? descripcion { get; set; }
    public decimal? precio { get; set; }
    public int? cantidad { get; set; } // se manda a GC
    public string? foto { get; set; }  // base64 opcional
}

class ArticuloResponse
{
    public int? id_articulo { get; set; }
    public string? nombre { get; set; }
    public string? descripcion { get; set; }
    public decimal? precio { get; set; }
    public string? foto { get; set; }  // base64 o null
}
