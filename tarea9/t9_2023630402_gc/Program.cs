// Microservicio Gestión de Compras (GC) - t9_2023630402_gc
// ASP.NET Core Minimal API + MySQL
// Endpoints:
// POST   /gc/alta_articulo
// POST   /gc/compra_articulo
// GET    /gc/consulta_carrito_compra?id_usuario=...&token=...
// DELETE /gc/elimina_articulo_carrito_compra?id_usuario=...&id_articulo=...&token=...
// DELETE /gc/elimina_carrito_compra?id_usuario=...&token=...
// PUT    /gc/modifica_carrito_compra   (body: {id_usuario, token, id_articulo, delta})

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;

namespace t9_2023630402_gc
{
    public class Program
    {
        // -------------------- DTOs --------------------
        class AltaArticuloReq
        {
            public int? id_usuario { get; set; }
            public string? token { get; set; }
            public int? id_articulo { get; set; } // viene desde GA
            public int? cantidad { get; set; }    // existencia inicial
        }

        class CompraReq
        {
            public int? id_usuario { get; set; }
            public string? token { get; set; }
            public int? id_articulo { get; set; }
            public int? cantidad { get; set; }
        }

        class ModificaReq
        {
            public int? id_usuario { get; set; }
            public string? token { get; set; }
            public int? id_articulo { get; set; }
            public int? delta { get; set; } // +1 o -1
        }

        // Lo que queremos regresar al front
        class ItemCarritoResp
        {
            public int? id_articulo { get; set; }
            public string? nombre { get; set; }
            public string? descripcion { get; set; }
            public decimal? precio { get; set; }
            public int? cantidad { get; set; }
            public string? foto { get; set; } // base64 opcional
        }

        // Respuesta de GA
        class ArticuloGaResp
        {
            public int? id_articulo { get; set; }
            public string? nombre { get; set; }
            public string? descripcion { get; set; }
            public decimal? precio { get; set; }
            public string? foto { get; set; }
        }

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            // -------------------- Helpers --------------------
            IResult Bad(string msg) => Results.BadRequest(new { mensaje = msg });

            static string RequireEnv(string name)
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

            async Task<bool> VerificaAccesoAsync(int idUsuario, string token)
            {
                var guBase = RequireEnv("GU_BASEURL").TrimEnd('/');
                var url = $"{guBase}/gu/verifica_acceso?id_usuario={idUsuario}&token={Uri.EscapeDataString(token)}";

                try
                {
                    using var resp = await http.GetAsync(url);
                    if (!resp.IsSuccessStatusCode) return false;

                    var txt = (await resp.Content.ReadAsStringAsync()).Trim();

                    if (string.Equals(txt, "true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(txt, "false", StringComparison.OrdinalIgnoreCase)) return false;

                    return bool.TryParse(txt, out var ok) && ok;
                }
                catch
                {
                    return false;
                }
            }

            async Task<ArticuloGaResp?> GetArticuloFromGaAsync(int idArticulo, int idUsuario, string token)
            {
                var gaBase = RequireEnv("GA_BASEURL").TrimEnd('/');
                var url = $"{gaBase}/ga/consulta_articulo?id_articulo={idArticulo}&id_usuario={idUsuario}&token={Uri.EscapeDataString(token)}";

                using var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return null;

                var txt = await resp.Content.ReadAsStringAsync();

                // GA regresa JSON objeto
                try
                {
                    return JsonConvert.DeserializeObject<ArticuloGaResp>(txt);
                }
                catch
                {
                    return null;
                }
            }

            // -------------------- Endpoints --------------------
            app.MapGet("/health", () => Results.Ok(new { ok = true }));

            // 1) alta_articulo (GC): registra stock por id_articulo
            app.MapPost("/gc/alta_articulo", async (HttpRequest req) =>
            {
                try
                {
                    var body = await new StreamReader(req.Body).ReadToEndAsync();
                    var p = JsonConvert.DeserializeObject<AltaArticuloReq>(body);

                    if (p == null) return Bad("Se esperan los datos del artículo");
                    if (p.id_usuario == null) return Bad("Falta id_usuario");
                    if (string.IsNullOrWhiteSpace(p.token)) return Bad("Falta token");
                    if (p.id_articulo == null) return Bad("Falta id_articulo");
                    if (p.cantidad == null || p.cantidad <= 0) return Bad("Cantidad inválida");

                    if (!await VerificaAccesoAsync(p.id_usuario.Value, p.token!))
                        return Bad("Acceso denegado");

                    using var conn = OpenDb();
                    using var tx = conn.BeginTransaction();
                    try
                    {
                        using var cmd = new MySqlCommand(
                            @"INSERT INTO stock(id_articulo, cantidad)
                              VALUES (@id, @cant)
                              ON DUPLICATE KEY UPDATE cantidad=@cant",
                            conn, tx);

                        cmd.Parameters.AddWithValue("@id", p.id_articulo.Value);
                        cmd.Parameters.AddWithValue("@cant", p.cantidad.Value);
                        cmd.ExecuteNonQuery();

                        tx.Commit();
                        return Results.Ok(new { mensaje = "Stock registrado en GC" });
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

            // 2) compra_articulo (transacción)
            app.MapPost("/gc/compra_articulo", async (HttpRequest req) =>
            {
                try
                {
                    var body = await new StreamReader(req.Body).ReadToEndAsync();
                    var p = JsonConvert.DeserializeObject<CompraReq>(body);

                    if (p == null) return Bad("Se esperan los datos de la compra");
                    if (p.id_usuario == null) return Bad("Falta id_usuario");
                    if (string.IsNullOrWhiteSpace(p.token)) return Bad("Falta token");
                    if (p.id_articulo == null) return Bad("Falta id_articulo");
                    if (p.cantidad == null || p.cantidad <= 0) return Bad("Cantidad inválida");

                    if (!await VerificaAccesoAsync(p.id_usuario.Value, p.token!))
                        return Bad("Acceso denegado");

                    using var conn = OpenDb();
                    using var tx = conn.BeginTransaction();
                    try
                    {
                        using var cmdStock = new MySqlCommand(
                            "SELECT cantidad FROM stock WHERE id_articulo=@id FOR UPDATE",
                            conn, tx);

                        cmdStock.Parameters.AddWithValue("@id", p.id_articulo.Value);
                        object? res = cmdStock.ExecuteScalar();
                        if (res == null)
                            return Bad("El artículo no existe en stock (GC)");

                        int disponible = Convert.ToInt32(res);
                        if (p.cantidad.Value > disponible)
                        {
                            tx.Rollback();
                            return Results.BadRequest(new { mensaje = "No hay suficientes artículos en stock" });
                        }

                        using var cmdCar = new MySqlCommand(
                            @"INSERT INTO carrito_compra(id_usuario,id_articulo,cantidad)
                              VALUES (@u,@a,@c)
                              ON DUPLICATE KEY UPDATE cantidad = cantidad + @c",
                            conn, tx);

                        cmdCar.Parameters.AddWithValue("@u", p.id_usuario.Value);
                        cmdCar.Parameters.AddWithValue("@a", p.id_articulo.Value);
                        cmdCar.Parameters.AddWithValue("@c", p.cantidad.Value);
                        cmdCar.ExecuteNonQuery();

                        using var cmdUpd = new MySqlCommand(
                            @"UPDATE stock SET cantidad = cantidad - @c
                              WHERE id_articulo=@a",
                            conn, tx);

                        cmdUpd.Parameters.AddWithValue("@c", p.cantidad.Value);
                        cmdUpd.Parameters.AddWithValue("@a", p.id_articulo.Value);
                        cmdUpd.ExecuteNonQuery();

                        tx.Commit();
                        return Results.Ok(new { mensaje = "Artículo agregado al carrito" });
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

            // 3) ✅ consulta_carrito_compra (ahora llena datos consultando GA)
            app.MapGet("/gc/consulta_carrito_compra", async (HttpRequest req) =>
            {
                try
                {
                    if (!int.TryParse(req.Query["id_usuario"], out var idUsuario))
                        return Bad("Falta id_usuario");

                    string? token = req.Query["token"];
                    if (string.IsNullOrWhiteSpace(token))
                        return Bad("Falta token");

                    if (!await VerificaAccesoAsync(idUsuario, token!))
                        return Bad("Acceso denegado");

                    using var conn = OpenDb();

                    // 1) primero leemos el carrito (ids y cantidades)
                    var carrito = new List<(int id_articulo, int cantidad)>();
                    using (var cmd = new MySqlCommand(
                        @"SELECT id_articulo, cantidad
                          FROM carrito_compra
                          WHERE id_usuario=@u",
                        conn))
                    {
                        cmd.Parameters.AddWithValue("@u", idUsuario);
                        using var r = cmd.ExecuteReader();
                        while (r.Read())
                            carrito.Add((r.GetInt32(0), r.GetInt32(1)));
                    }

                    // 2) Por cada artículo, pedimos detalles a GA
                    var lista = new List<ItemCarritoResp>();
                    foreach (var it in carrito)
                    {
                        var art = await GetArticuloFromGaAsync(it.id_articulo, idUsuario, token!);

                        lista.Add(new ItemCarritoResp
                        {
                            id_articulo = it.id_articulo,
                            cantidad = it.cantidad,
                            nombre = art?.nombre,
                            descripcion = art?.descripcion,
                            precio = art?.precio,
                            foto = art?.foto
                        });
                    }

                    return Results.Ok(lista);
                }
                catch (Exception e)
                {
                    return Bad(e.Message);
                }
            });

            // 4) elimina_articulo_carrito_compra
            app.MapDelete("/gc/elimina_articulo_carrito_compra", async (HttpRequest req) =>
            {
                try
                {
                    if (!int.TryParse(req.Query["id_usuario"], out var idUsuario))
                        return Bad("Falta id_usuario");

                    if (!int.TryParse(req.Query["id_articulo"], out var idArticulo))
                        return Bad("Falta id_articulo");

                    string? token = req.Query["token"];
                    if (string.IsNullOrWhiteSpace(token))
                        return Bad("Falta token");

                    if (!await VerificaAccesoAsync(idUsuario, token!))
                        return Bad("Acceso denegado");

                    using var conn = OpenDb();
                    using var tx = conn.BeginTransaction();
                    try
                    {
                        using var cmdSel = new MySqlCommand(
                            @"SELECT cantidad FROM carrito_compra
                              WHERE id_usuario=@u AND id_articulo=@a FOR UPDATE",
                            conn, tx);

                        cmdSel.Parameters.AddWithValue("@u", idUsuario);
                        cmdSel.Parameters.AddWithValue("@a", idArticulo);

                        object? res = cmdSel.ExecuteScalar();
                        if (res == null)
                            return Bad("El artículo no existe en el carrito");

                        int cant = Convert.ToInt32(res);

                        using var cmdStock = new MySqlCommand(
                            @"UPDATE stock SET cantidad = cantidad + @c
                              WHERE id_articulo=@a",
                            conn, tx);

                        cmdStock.Parameters.AddWithValue("@c", cant);
                        cmdStock.Parameters.AddWithValue("@a", idArticulo);
                        cmdStock.ExecuteNonQuery();

                        using var cmdDel = new MySqlCommand(
                            @"DELETE FROM carrito_compra
                              WHERE id_usuario=@u AND id_articulo=@a",
                            conn, tx);

                        cmdDel.Parameters.AddWithValue("@u", idUsuario);
                        cmdDel.Parameters.AddWithValue("@a", idArticulo);
                        cmdDel.ExecuteNonQuery();

                        tx.Commit();
                        return Results.Ok(new { mensaje = "Artículo eliminado del carrito" });
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

            // 5) elimina_carrito_compra
            app.MapDelete("/gc/elimina_carrito_compra", async (HttpRequest req) =>
            {
                try
                {
                    if (!int.TryParse(req.Query["id_usuario"], out var idUsuario))
                        return Bad("Falta id_usuario");

                    string? token = req.Query["token"];
                    if (string.IsNullOrWhiteSpace(token))
                        return Bad("Falta token");

                    if (!await VerificaAccesoAsync(idUsuario, token!))
                        return Bad("Acceso denegado");

                    using var conn = OpenDb();
                    using var tx = conn.BeginTransaction();
                    try
                    {
                        using var cmdSel = new MySqlCommand(
                            @"SELECT id_articulo, cantidad
                              FROM carrito_compra
                              WHERE id_usuario=@u FOR UPDATE",
                            conn, tx);

                        cmdSel.Parameters.AddWithValue("@u", idUsuario);

                        var items = new List<(int id, int cant)>();
                        using (var r = cmdSel.ExecuteReader())
                        {
                            while (r.Read())
                                items.Add((r.GetInt32(0), r.GetInt32(1)));
                        }

                        foreach (var it in items)
                        {
                            using var cmdStock = new MySqlCommand(
                                @"UPDATE stock SET cantidad = cantidad + @c
                                  WHERE id_articulo=@a",
                                conn, tx);

                            cmdStock.Parameters.AddWithValue("@c", it.cant);
                            cmdStock.Parameters.AddWithValue("@a", it.id);
                            cmdStock.ExecuteNonQuery();
                        }

                        using var cmdDel = new MySqlCommand(
                            @"DELETE FROM carrito_compra WHERE id_usuario=@u",
                            conn, tx);

                        cmdDel.Parameters.AddWithValue("@u", idUsuario);
                        cmdDel.ExecuteNonQuery();

                        tx.Commit();
                        return Results.Ok(new { mensaje = "Se eliminó el carrito" });
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

            // 6) modifica_carrito_compra
            app.MapPut("/gc/modifica_carrito_compra", async (HttpRequest req) =>
            {
                try
                {
                    var body = await new StreamReader(req.Body).ReadToEndAsync();
                    var p = JsonConvert.DeserializeObject<ModificaReq>(body);

                    if (p == null) return Bad("Se esperan los datos de la modificación");
                    if (p.id_usuario == null) return Bad("Falta id_usuario");
                    if (string.IsNullOrWhiteSpace(p.token)) return Bad("Falta token");
                    if (p.id_articulo == null) return Bad("Falta id_articulo");
                    if (p.delta == null || p.delta == 0) return Bad("Delta inválido");

                    if (!await VerificaAccesoAsync(p.id_usuario.Value, p.token!))
                        return Bad("Acceso denegado");

                    using var conn = OpenDb();
                    using var tx = conn.BeginTransaction();
                    try
                    {
                        using var cmdCar = new MySqlCommand(
                            @"SELECT cantidad FROM carrito_compra
                              WHERE id_usuario=@u AND id_articulo=@a FOR UPDATE",
                            conn, tx);

                        cmdCar.Parameters.AddWithValue("@u", p.id_usuario.Value);
                        cmdCar.Parameters.AddWithValue("@a", p.id_articulo.Value);

                        object? resCar = cmdCar.ExecuteScalar();
                        int actual = resCar == null ? 0 : Convert.ToInt32(resCar);

                        if (p.delta.Value < 0 && actual <= 0)
                            return Results.BadRequest(new { mensaje = "No hay más artículos en el carrito" });

                        if (p.delta.Value > 0)
                        {
                            using var cmdStock = new MySqlCommand(
                                @"SELECT cantidad FROM stock WHERE id_articulo=@a FOR UPDATE",
                                conn, tx);

                            cmdStock.Parameters.AddWithValue("@a", p.id_articulo.Value);
                            object? resStock = cmdStock.ExecuteScalar();
                            if (resStock == null)
                                return Bad("El artículo no existe en stock (GC)");

                            int disponible = Convert.ToInt32(resStock);
                            if (disponible < p.delta.Value)
                            {
                                tx.Rollback();
                                return Results.BadRequest(new { mensaje = "No hay suficientes artículos en stock" });
                            }

                            if (actual == 0)
                            {
                                using var cmdIns = new MySqlCommand(
                                    @"INSERT INTO carrito_compra(id_usuario,id_articulo,cantidad)
                                      VALUES(@u,@a,@c)",
                                    conn, tx);

                                cmdIns.Parameters.AddWithValue("@u", p.id_usuario.Value);
                                cmdIns.Parameters.AddWithValue("@a", p.id_articulo.Value);
                                cmdIns.Parameters.AddWithValue("@c", p.delta.Value);
                                cmdIns.ExecuteNonQuery();
                            }
                            else
                            {
                                using var cmdUpd = new MySqlCommand(
                                    @"UPDATE carrito_compra
                                      SET cantidad = cantidad + @d
                                      WHERE id_usuario=@u AND id_articulo=@a",
                                    conn, tx);

                                cmdUpd.Parameters.AddWithValue("@d", p.delta.Value);
                                cmdUpd.Parameters.AddWithValue("@u", p.id_usuario.Value);
                                cmdUpd.Parameters.AddWithValue("@a", p.id_articulo.Value);
                                cmdUpd.ExecuteNonQuery();
                            }

                            using var cmdUpdStock = new MySqlCommand(
                                @"UPDATE stock
                                  SET cantidad = cantidad - @d
                                  WHERE id_articulo=@a",
                                conn, tx);

                            cmdUpdStock.Parameters.AddWithValue("@d", p.delta.Value);
                            cmdUpdStock.Parameters.AddWithValue("@a", p.id_articulo.Value);
                            cmdUpdStock.ExecuteNonQuery();
                        }
                        else
                        {
                            int nueva = actual + p.delta.Value;

                            using var cmdStock = new MySqlCommand(
                                @"UPDATE stock
                                  SET cantidad = cantidad + @dev
                                  WHERE id_articulo=@a",
                                conn, tx);

                            cmdStock.Parameters.AddWithValue("@dev", -p.delta.Value);
                            cmdStock.Parameters.AddWithValue("@a", p.id_articulo.Value);
                            cmdStock.ExecuteNonQuery();

                            if (nueva <= 0)
                            {
                                using var cmdDel = new MySqlCommand(
                                    @"DELETE FROM carrito_compra
                                      WHERE id_usuario=@u AND id_articulo=@a",
                                    conn, tx);

                                cmdDel.Parameters.AddWithValue("@u", p.id_usuario.Value);
                                cmdDel.Parameters.AddWithValue("@a", p.id_articulo.Value);
                                cmdDel.ExecuteNonQuery();
                            }
                            else
                            {
                                using var cmdUpd = new MySqlCommand(
                                    @"UPDATE carrito_compra
                                      SET cantidad=@c
                                      WHERE id_usuario=@u AND id_articulo=@a",
                                    conn, tx);

                                cmdUpd.Parameters.AddWithValue("@c", nueva);
                                cmdUpd.Parameters.AddWithValue("@u", p.id_usuario.Value);
                                cmdUpd.Parameters.AddWithValue("@a", p.id_articulo.Value);
                                cmdUpd.ExecuteNonQuery();
                            }
                        }

                        tx.Commit();
                        return Results.Ok(new { mensaje = "Carrito modificado" });
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
        }
    }
}
