// Microservicio Gestión de Usuarios (GU) - t9_2023630402_gu
// ASP.NET Core Minimal API + MySQL
// Endpoints:
// POST   /gu/alta_usuario
// GET    /gu/consulta_usuario?email=...&id_usuario=...&token=...
// PUT    /gu/modifica_usuario?email=...&id_usuario=...&token=...
// DELETE /gu/borra_usuario?email=...&id_usuario=...&token=...
// POST   /gu/login
// GET    /gu/verifica_acceso?id_usuario=...&token=...

using System.Security.Cryptography;
using System.Text;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// -------------------- Helpers --------------------

IResult Bad(string msg) => Results.BadRequest(JsonConvert.SerializeObject(new { mensaje = msg }));

MySqlConnection OpenDb()
{
    var host = Environment.GetEnvironmentVariable("MYSQL_HOST") ?? throw new Exception("MYSQL_HOST no definido");
    var user = Environment.GetEnvironmentVariable("MYSQL_USER") ?? throw new Exception("MYSQL_USER no definido");
    var pass = Environment.GetEnvironmentVariable("MYSQL_PASSWORD") ?? throw new Exception("MYSQL_PASSWORD no definido");
    var db = Environment.GetEnvironmentVariable("MYSQL_DB") ?? throw new Exception("MYSQL_DB no definido");

    // En Azure MySQL, si te da error SSL, cambia a: SslMode=Required;
    var ssl = Environment.GetEnvironmentVariable("MYSQL_SSLMODE") ?? "Preferred";
    var cs = $"Server={host};UserID={user};Password={pass};Database={db};SslMode={ssl};";

    var conn = new MySqlConnection(cs);
    conn.Open();
    return conn;
}

string NewToken(int length = 20)
{
    const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    var bytes = RandomNumberGenerator.GetBytes(length);
    var sb = new StringBuilder(length);
    for (int i = 0; i < length; i++)
        sb.Append(alphabet[bytes[i] % alphabet.Length]);
    return sb.ToString();
}

bool VerificaAcceso(MySqlConnection conn, int idUsuario, string token)
{
    using var cmd = new MySqlCommand("SELECT 1 FROM usuarios WHERE id_usuario=@id AND token=@t LIMIT 1", conn);
    cmd.Parameters.AddWithValue("@id", idUsuario);
    cmd.Parameters.AddWithValue("@t", token);
    using var r = cmd.ExecuteReader();
    return r.Read();
}

// -------------------- Endpoints --------------------

app.MapGet("/health", () => Results.Ok(new { ok = true }));

// 1) alta_usuario
app.MapPost("/gu/alta_usuario", async (HttpRequest req) =>
{
    try
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var u = JsonConvert.DeserializeObject<UsuarioAlta>(body);

        if (u == null) return Bad("Se esperan los datos del usuario");
        if (string.IsNullOrWhiteSpace(u.email)) return Bad("Se debe ingresar el email");
        if (string.IsNullOrWhiteSpace(u.password)) return Bad("Se debe ingresar el password hash");
        if (string.IsNullOrWhiteSpace(u.nombre)) return Bad("Se debe ingresar el nombre");
        if (string.IsNullOrWhiteSpace(u.apellido_paterno)) return Bad("Se debe ingresar el apellido_paterno");
        if (u.fecha_nacimiento == null) return Bad("Se debe ingresar la fecha de nacimiento");

        using var conn = OpenDb();
        using var tx = conn.BeginTransaction();

        try
        {
            using var cmd1 = new MySqlCommand(
                "INSERT INTO usuarios(id_usuario,email,password,nombre,apellido_paterno,apellido_materno,fecha_nacimiento,telefono,genero) " +
                "VALUES (0,@email,@password,@nombre,@ap,@am,@fn,@tel,@gen)",
                conn, tx);

            cmd1.Parameters.AddWithValue("@email", u.email);
            cmd1.Parameters.AddWithValue("@password", u.password);
            cmd1.Parameters.AddWithValue("@nombre", u.nombre);
            cmd1.Parameters.AddWithValue("@ap", u.apellido_paterno);
            cmd1.Parameters.AddWithValue("@am", u.apellido_materno);
            cmd1.Parameters.AddWithValue("@fn", u.fecha_nacimiento);
            cmd1.Parameters.AddWithValue("@tel", u.telefono);
            cmd1.Parameters.AddWithValue("@gen", u.genero);

            cmd1.ExecuteNonQuery();
            long idUsuario = cmd1.LastInsertedId;

            if (!string.IsNullOrWhiteSpace(u.foto))
            {
                using var cmd2 = new MySqlCommand(
                    "INSERT INTO fotos_usuarios (foto,id_usuario) VALUES (@foto,@id)",
                    conn, tx);

                cmd2.Parameters.AddWithValue("@foto", Convert.FromBase64String(u.foto));
                cmd2.Parameters.AddWithValue("@id", idUsuario);
                cmd2.ExecuteNonQuery();
            }

            tx.Commit();
            return Results.Ok(new { mensaje = "Se dio de alta el usuario", id_usuario = idUsuario });
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

// 2) login
// Recibe: { "email": "...", "password": "HASH" }
// Si ok: genera token 20 chars, lo guarda en usuarios.token y regresa {id_usuario, token}
// Si no: 400 + {"mensaje":"Acceso denegado"}
app.MapPost("/gu/login", async (HttpRequest req) =>
{
    try
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        dynamic? data = JsonConvert.DeserializeObject(body);

        string? email = data?.email;
        string? password = data?.password;

        if (string.IsNullOrWhiteSpace(email)) return Bad("Falta email");
        if (string.IsNullOrWhiteSpace(password)) return Bad("Falta password hash");

        using var conn = OpenDb();

        using var cmd = new MySqlCommand(
            "SELECT id_usuario FROM usuarios WHERE email=@e AND password=@p LIMIT 1",
            conn);

        cmd.Parameters.AddWithValue("@e", email);
        cmd.Parameters.AddWithValue("@p", password);

        object? idObj = cmd.ExecuteScalar();

        // ✅ CAMBIO CLAVE: si NO existe, NO regreses 200
        if (idObj == null)
            return Bad("Acceso denegado");

        int idUsuario = Convert.ToInt32(idObj);
        string token = NewToken(20);

        using var cmd2 = new MySqlCommand(
            "UPDATE usuarios SET token=@t WHERE id_usuario=@id",
            conn);

        cmd2.Parameters.AddWithValue("@t", token);
        cmd2.Parameters.AddWithValue("@id", idUsuario);
        cmd2.ExecuteNonQuery();

        return Results.Ok(new { id_usuario = idUsuario, token });
    }
    catch (Exception e)
    {
        return Bad(e.Message);
    }
});



// 3) verifica_acceso
app.MapGet("/gu/verifica_acceso", (HttpRequest req) =>
{
    try
    {
        if (!int.TryParse(req.Query["id_usuario"], out var idUsuario)) return Bad("Falta id_usuario");
        string? token = req.Query["token"];
        if (string.IsNullOrWhiteSpace(token)) return Bad("Falta token");

        using var conn = OpenDb();
        bool ok = VerificaAcceso(conn, idUsuario, token);
        return Results.Ok(ok);
    }
    catch (Exception e)
    {
        return Bad(e.Message);
    }
});

// 4) consulta_usuario
app.MapGet("/gu/consulta_usuario", (HttpRequest req) =>
{
    try
    {
        string? email = req.Query["email"];
        if (string.IsNullOrWhiteSpace(email)) return Bad("Falta email");

        if (!int.TryParse(req.Query["id_usuario"], out var idUsuario)) return Bad("Falta id_usuario");
        string? token = req.Query["token"];
        if (string.IsNullOrWhiteSpace(token)) return Bad("Falta token");

        using var conn = OpenDb();
        if (!VerificaAcceso(conn, idUsuario, token)) throw new Exception("Acceso denegado");

        using var cmd = new MySqlCommand(
            "SELECT a.id_usuario,a.email,a.nombre,a.apellido_paterno,a.apellido_materno,a.fecha_nacimiento,a.telefono,a.genero," +
            "b.foto, LENGTH(b.foto) " +
            "FROM usuarios a LEFT JOIN fotos_usuarios b ON a.id_usuario=b.id_usuario " +
            "WHERE a.email=@email",
            conn);

        cmd.Parameters.AddWithValue("@email", email);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new Exception("El email no existe");

        var u = new UsuarioConsulta
        {
            id_usuario = r.GetInt32(0),
            email = r.GetString(1),
            nombre = r.GetString(2),
            apellido_paterno = r.GetString(3),
            apellido_materno = !r.IsDBNull(4) ? r.GetString(4) : null,
            fecha_nacimiento = r.GetDateTime(5),
            telefono = !r.IsDBNull(6) ? r.GetInt64(6) : null,
            genero = !r.IsDBNull(7) ? r.GetString(7) : null,
        };

        if (!r.IsDBNull(8))
        {
            int len = r.GetInt32(9);
            var foto = new byte[len];
            r.GetBytes(8, 0, foto, 0, len);
            u.foto = Convert.ToBase64String(foto);
        }

        return Results.Ok(u);
    }
    catch (Exception e)
    {
        return Bad(e.Message);
    }
});

// 5) modifica_usuario
app.MapPut("/gu/modifica_usuario", async (HttpRequest req) =>
{
    try
    {
        string? email = req.Query["email"];
        if (string.IsNullOrWhiteSpace(email)) return Bad("Falta email");

        if (!int.TryParse(req.Query["id_usuario"], out var idUsuario)) return Bad("Falta id_usuario");
        string? token = req.Query["token"];
        if (string.IsNullOrWhiteSpace(token)) return Bad("Falta token");

        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var u = JsonConvert.DeserializeObject<UsuarioModifica>(body);
        if (u == null) return Bad("Se esperan los datos del usuario");

        if (string.IsNullOrWhiteSpace(u.nombre)) return Bad("Se debe ingresar el nombre");
        if (string.IsNullOrWhiteSpace(u.apellido_paterno)) return Bad("Se debe ingresar el apellido_paterno");
        if (u.fecha_nacimiento == null) return Bad("Se debe ingresar la fecha de nacimiento");

        using var conn = OpenDb();
        if (!VerificaAcceso(conn, idUsuario, token)) throw new Exception("Acceso denegado");

        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd1 = new MySqlCommand(
                "UPDATE usuarios SET nombre=@n,apellido_paterno=@ap,apellido_materno=@am,fecha_nacimiento=@fn,telefono=@tel,genero=@gen " +
                "WHERE email=@e",
                conn, tx);

            cmd1.Parameters.AddWithValue("@n", u.nombre);
            cmd1.Parameters.AddWithValue("@ap", u.apellido_paterno);
            cmd1.Parameters.AddWithValue("@am", u.apellido_materno);
            cmd1.Parameters.AddWithValue("@fn", u.fecha_nacimiento);
            cmd1.Parameters.AddWithValue("@tel", u.telefono);
            cmd1.Parameters.AddWithValue("@gen", u.genero);
            cmd1.Parameters.AddWithValue("@e", email);
            cmd1.ExecuteNonQuery();

            if (!string.IsNullOrWhiteSpace(u.password))
            {
                using var cmd2 = new MySqlCommand("UPDATE usuarios SET password=@p WHERE email=@e", conn, tx);
                cmd2.Parameters.AddWithValue("@p", u.password);
                cmd2.Parameters.AddWithValue("@e", email);
                cmd2.ExecuteNonQuery();
            }

            using var cmd3 = new MySqlCommand(
                "DELETE FROM fotos_usuarios WHERE id_usuario=(SELECT id_usuario FROM usuarios WHERE email=@e)",
                conn, tx);
            cmd3.Parameters.AddWithValue("@e", email);
            cmd3.ExecuteNonQuery();

            if (!string.IsNullOrWhiteSpace(u.foto))
            {
                using var cmd4 = new MySqlCommand(
                    "INSERT INTO fotos_usuarios (foto,id_usuario) VALUES (@foto,(SELECT id_usuario FROM usuarios WHERE email=@e))",
                    conn, tx);
                cmd4.Parameters.AddWithValue("@foto", Convert.FromBase64String(u.foto));
                cmd4.Parameters.AddWithValue("@e", email);
                cmd4.ExecuteNonQuery();
            }

            tx.Commit();
            return Results.Ok(new { mensaje = "Se modificó el usuario" });
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

// 6) borra_usuario
app.MapDelete("/gu/borra_usuario", (HttpRequest req) =>
{
    try
    {
        string? email = req.Query["email"];
        if (string.IsNullOrWhiteSpace(email)) return Bad("Falta email");

        if (!int.TryParse(req.Query["id_usuario"], out var idUsuario)) return Bad("Falta id_usuario");
        string? token = req.Query["token"];
        if (string.IsNullOrWhiteSpace(token)) return Bad("Falta token");

        using var conn = OpenDb();
        if (!VerificaAcceso(conn, idUsuario, token)) throw new Exception("Acceso denegado");

        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd1 = new MySqlCommand(
                "DELETE FROM fotos_usuarios WHERE id_usuario=(SELECT id_usuario FROM usuarios WHERE email=@e)",
                conn, tx);
            cmd1.Parameters.AddWithValue("@e", email);
            cmd1.ExecuteNonQuery();

            using var cmd2 = new MySqlCommand("DELETE FROM usuarios WHERE email=@e", conn, tx);
            cmd2.Parameters.AddWithValue("@e", email);
            cmd2.ExecuteNonQuery();

            tx.Commit();
            return Results.Ok(new { mensaje = "Se borró el usuario" });
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

app.Run();


// -------------------- DTOs (AL FINAL para evitar CS8803) --------------------

class UsuarioAlta
{
    public string? email { get; set; }
    public string? password { get; set; } // hash
    public string? nombre { get; set; }
    public string? apellido_paterno { get; set; }
    public string? apellido_materno { get; set; }
    public DateTime? fecha_nacimiento { get; set; }
    public long? telefono { get; set; }
    public string? genero { get; set; } // "M"/"F"/null
    public string? foto { get; set; }   // base64 o null
}

class UsuarioModifica
{
    public string? password { get; set; } // si viene vacío/null, no se cambia
    public string? nombre { get; set; }
    public string? apellido_paterno { get; set; }
    public string? apellido_materno { get; set; }
    public DateTime? fecha_nacimiento { get; set; }
    public long? telefono { get; set; }
    public string? genero { get; set; }
    public string? foto { get; set; } // base64 o null
}

class UsuarioConsulta
{
    public int? id_usuario { get; set; }
    public string? email { get; set; }
    public string? nombre { get; set; }
    public string? apellido_paterno { get; set; }
    public string? apellido_materno { get; set; }
    public DateTime? fecha_nacimiento { get; set; }
    public long? telefono { get; set; }
    public string? genero { get; set; }
    public string? foto { get; set; } // base64
}
