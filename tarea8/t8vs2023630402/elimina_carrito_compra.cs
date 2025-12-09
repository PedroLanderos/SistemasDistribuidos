using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;

namespace servicio;

public class elimina_carrito_compra
{
    class HuboError
    {
        public string mensaje;
        public HuboError(string mensaje)
        {
            this.mensaje = mensaje;
        }
    }

    [Function("elimina_carrito_compra")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete")] HttpRequest req)
    {
        try
        {
            string? id_usuario_str = req.Query["id_usuario"];
            string? token = req.Query["token"];

            if (string.IsNullOrWhiteSpace(id_usuario_str)) throw new Exception("Falta id_usuario");
            if (string.IsNullOrWhiteSpace(token)) throw new Exception("Falta token");

            int id_usuario = int.Parse(id_usuario_str);

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

                // leer todos los registros del carrito
                var cmdSel = new MySqlCommand(
                    @"SELECT id_articulo,cantidad
                      FROM carrito_compra
                      WHERE id_usuario=@id_usuario");
                cmdSel.Connection = conexion;
                cmdSel.Transaction = tx;
                cmdSel.Parameters.AddWithValue("@id_usuario", id_usuario);

                var items = new List<(int id_articulo, int cantidad)>();
                using (var r = cmdSel.ExecuteReader())
                {
                    while (r.Read())
                    {
                        items.Add((r.GetInt32(0), r.GetInt32(1)));
                    }
                }

                // regresar existencias
                foreach (var it in items)
                {
                    var cmdUp = new MySqlCommand(
                        @"UPDATE stock SET cantidad = cantidad + @cantidad
                          WHERE id_articulo=@id_articulo");
                    cmdUp.Connection = conexion;
                    cmdUp.Transaction = tx;
                    cmdUp.Parameters.AddWithValue("@cantidad", it.cantidad);
                    cmdUp.Parameters.AddWithValue("@id_articulo", it.id_articulo);
                    cmdUp.ExecuteNonQuery();
                }

                // borrar carrito del usuario
                var cmdDel = new MySqlCommand(
                    @"DELETE FROM carrito_compra WHERE id_usuario=@id_usuario");
                cmdDel.Connection = conexion;
                cmdDel.Transaction = tx;
                cmdDel.Parameters.AddWithValue("@id_usuario", id_usuario);
                cmdDel.ExecuteNonQuery();

                tx.Commit();
                return new OkObjectResult("{\"mensaje\":\"Se eliminó el carrito\"}");
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
