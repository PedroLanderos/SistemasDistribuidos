using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;

namespace servicio;

public class compra_articulo
{
    class Peticion
    {
        public int? id_usuario;
        public string? token;
        public int? id_articulo;
        public int? cantidad;
    }

    class HuboError
    {
        public string mensaje;
        public HuboError(string mensaje)
        {
            this.mensaje = mensaje;
        }
    }

    [Function("compra_articulo")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            Peticion? p = JsonConvert.DeserializeObject<Peticion>(body);
            if (p == null) throw new Exception("Se esperan los datos de la compra");

            if (p.id_usuario == null) throw new Exception("Falta id_usuario");
            if (string.IsNullOrWhiteSpace(p.token)) throw new Exception("Falta token");
            if (p.id_articulo == null) throw new Exception("Falta id_articulo");
            if (p.cantidad == null || p.cantidad <= 0) throw new Exception("Cantidad inválida");

            string? Server = Environment.GetEnvironmentVariable("Server");
            string? UserID = Environment.GetEnvironmentVariable("UserID");
            string? Password = Environment.GetEnvironmentVariable("Password");
            string? Database = Environment.GetEnvironmentVariable("Database");

            string cs = "Server=" + Server + ";UserID=" + UserID + ";Password=" + Password +
                        ";Database=" + Database + ";SslMode=Preferred;";

            var conexion = new MySqlConnection(cs);
            conexion.Open();
            MySqlTransaction tx = conexion.BeginTransaction();

            try
            {
                if (!login.verifica_acceso(conexion, p.id_usuario.Value, p.token!))
                    throw new Exception("Acceso denegado");

                // 1) Verificar stock
                var cmdStock = new MySqlCommand(
                    "SELECT cantidad FROM stock WHERE id_articulo=@id_articulo");
                cmdStock.Connection = conexion;
                cmdStock.Transaction = tx;
                cmdStock.Parameters.AddWithValue("@id_articulo", p.id_articulo);
                object? res = cmdStock.ExecuteScalar();
                if (res == null)
                    throw new Exception("El artículo no existe");

                int disponible = Convert.ToInt32(res);
                if (p.cantidad.Value > disponible)
                    throw new Exception("Stock insuficiente");

                // 2) Insertar/actualizar carrito
                var cmdCarrito = new MySqlCommand(
                    @"INSERT INTO carrito_compra(id_carrito,id_usuario,id_articulo,cantidad)
                      VALUES (0,@id_usuario,@id_articulo,@cantidad)
                      ON DUPLICATE KEY UPDATE cantidad = cantidad + @cantidad");
                cmdCarrito.Connection = conexion;
                cmdCarrito.Transaction = tx;
                cmdCarrito.Parameters.AddWithValue("@id_usuario", p.id_usuario);
                cmdCarrito.Parameters.AddWithValue("@id_articulo", p.id_articulo);
                cmdCarrito.Parameters.AddWithValue("@cantidad", p.cantidad);
                cmdCarrito.ExecuteNonQuery();

                // 3) Actualizar stock
                var cmdUpdateStock = new MySqlCommand(
                    "UPDATE stock SET cantidad = cantidad - @cantidad WHERE id_articulo=@id_articulo");
                cmdUpdateStock.Connection = conexion;
                cmdUpdateStock.Transaction = tx;
                cmdUpdateStock.Parameters.AddWithValue("@cantidad", p.cantidad);
                cmdUpdateStock.Parameters.AddWithValue("@id_articulo", p.id_articulo);
                cmdUpdateStock.ExecuteNonQuery();

                tx.Commit();
                return new OkObjectResult("{\"mensaje\":\"Artículo agregado al carrito\"}");
            }
            catch (Exception e)
            {
                tx.Rollback();
                throw new Exception(e.Message);
            }
            finally
            {
                conexion.Close();
            }
        }
        catch (Exception e)
        {
            return new BadRequestObjectResult(
                JsonConvert.SerializeObject(new HuboError(e.Message)));
        }
    }
}
