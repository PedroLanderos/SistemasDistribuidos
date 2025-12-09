using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;

namespace servicio;

public class elimina_articulo_carrito_compra
{
    class HuboError
    {
        public string mensaje;
        public HuboError(string mensaje)
        {
            this.mensaje = mensaje;
        }
    }

    [Function("elimina_articulo_carrito_compra")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete")] HttpRequest req)
    {
        try
        {
            string? id_usuario_str = req.Query["id_usuario"];
            string? token = req.Query["token"];
            string? id_articulo_str = req.Query["id_articulo"];

            if (string.IsNullOrWhiteSpace(id_usuario_str)) throw new Exception("Falta id_usuario");
            if (string.IsNullOrWhiteSpace(token)) throw new Exception("Falta token");
            if (string.IsNullOrWhiteSpace(id_articulo_str)) throw new Exception("Falta id_articulo");

            int id_usuario = int.Parse(id_usuario_str);
            int id_articulo = int.Parse(id_articulo_str);

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
                if (!login.verifica_acceso(conexion, id_usuario, token!))
                    throw new Exception("Acceso denegado");

                // cantidad en el carrito
                var cmdCantidad = new MySqlCommand(
                    @"SELECT cantidad FROM carrito_compra
                      WHERE id_usuario=@id_usuario AND id_articulo=@id_articulo");
                cmdCantidad.Connection = conexion;
                cmdCantidad.Transaction = tx;
                cmdCantidad.Parameters.AddWithValue("@id_usuario", id_usuario);
                cmdCantidad.Parameters.AddWithValue("@id_articulo", id_articulo);
                object? res = cmdCantidad.ExecuteScalar();
                if (res == null)
                    throw new Exception("El artículo no existe en el carrito");

                int cantCarrito = Convert.ToInt32(res);

                // devolver al stock
                var cmdStock = new MySqlCommand(
                    @"UPDATE stock SET cantidad = cantidad + @cantidad
                      WHERE id_articulo=@id_articulo");
                cmdStock.Connection = conexion;
                cmdStock.Transaction = tx;
                cmdStock.Parameters.AddWithValue("@cantidad", cantCarrito);
                cmdStock.Parameters.AddWithValue("@id_articulo", id_articulo);
                cmdStock.ExecuteNonQuery();

                // borrar del carrito
                var cmdDel = new MySqlCommand(
                    @"DELETE FROM carrito_compra
                      WHERE id_usuario=@id_usuario AND id_articulo=@id_articulo");
                cmdDel.Connection = conexion;
                cmdDel.Transaction = tx;
                cmdDel.Parameters.AddWithValue("@id_usuario", id_usuario);
                cmdDel.Parameters.AddWithValue("@id_articulo", id_articulo);
                cmdDel.ExecuteNonQuery();

                tx.Commit();
                return new OkObjectResult("{\"mensaje\":\"Artículo eliminado del carrito\"}");
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
