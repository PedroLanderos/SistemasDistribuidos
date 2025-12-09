// (c) Carlos Pineda Guerrero / adaptado para artículos
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;

namespace servicio;

public class alta_articulo
{
    class Articulo
    {
        public int? id_usuario;
        public string? token;
        public string? nombre;
        public string? descripcion;
        public decimal? precio;
        public int? cantidad;
        public string? foto; // base64
    }

    class HuboError
    {
        public string mensaje;
        public HuboError(string mensaje)
        {
            this.mensaje = mensaje;
        }
    }

    [Function("alta_articulo")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            Articulo? art = JsonConvert.DeserializeObject<Articulo>(body);
            if (art == null) throw new Exception("Se esperan los datos del artículo");

            if (art.id_usuario == null) throw new Exception("Falta id_usuario");
            if (string.IsNullOrWhiteSpace(art.token)) throw new Exception("Falta token");
            if (string.IsNullOrWhiteSpace(art.nombre)) throw new Exception("Falta nombre");
            if (string.IsNullOrWhiteSpace(art.descripcion)) throw new Exception("Falta descripción");
            if (art.precio == null || art.precio <= 0) throw new Exception("Precio inválido");
            if (art.cantidad == null || art.cantidad <= 0) throw new Exception("Cantidad inválida");

            string? Server = Environment.GetEnvironmentVariable("Server");
            string? UserID = Environment.GetEnvironmentVariable("UserID");
            string? Password = Environment.GetEnvironmentVariable("Password");
            string? Database = Environment.GetEnvironmentVariable("Database");

            string cs = "Server=" + Server + ";UserID=" + UserID + ";Password=" + Password +
                        ";Database=" + Database + ";SslMode=Preferred;";

            var conexion = new MySqlConnection(cs);
            conexion.Open();
            MySqlTransaction transaccion = conexion.BeginTransaction();

            try
            {
                // Verificar acceso con id_usuario y token
                if (!login.verifica_acceso(conexion, art.id_usuario.Value, art.token!))
                    throw new Exception("Acceso denegado");

                var cmd1 = new MySqlCommand();
                cmd1.Connection = conexion;
                cmd1.Transaction = transaccion;
                cmd1.CommandText = @"INSERT INTO stock
                    (id_articulo, nombre, descripcion, precio, cantidad, id_usuario)
                    VALUES (0,@nombre,@descripcion,@precio,@cantidad,@id_usuario)";
                cmd1.Parameters.AddWithValue("@nombre", art.nombre);
                cmd1.Parameters.AddWithValue("@descripcion", art.descripcion);
                cmd1.Parameters.AddWithValue("@precio", art.precio);
                cmd1.Parameters.AddWithValue("@cantidad", art.cantidad);
                cmd1.Parameters.AddWithValue("@id_usuario", art.id_usuario);
                cmd1.ExecuteNonQuery();

                long id_articulo = cmd1.LastInsertedId;

                if (art.foto != null && art.foto != "")
                {
                    var cmd2 = new MySqlCommand();
                    cmd2.Connection = conexion;
                    cmd2.Transaction = transaccion;
                    cmd2.CommandText = @"INSERT INTO fotos_articulos (foto,id_articulo)
                                         VALUES (@foto,@id_articulo)";
                    cmd2.Parameters.AddWithValue("@foto", Convert.FromBase64String(art.foto));
                    cmd2.Parameters.AddWithValue("@id_articulo", id_articulo);
                    cmd2.ExecuteNonQuery();
                }

                transaccion.Commit();
                return new OkObjectResult("{\"mensaje\":\"Se dio de alta el artículo\"}");
            }
            catch (Exception e)
            {
                transaccion.Rollback();
                throw new Exception(e.Message);
            }
            finally
            {
                conexion.Close();
            }
        }
        catch (Exception e)
        {
            return new BadRequestObjectResult(JsonConvert.SerializeObject(new HuboError(e.Message)));
        }
    }
}
