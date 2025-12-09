using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;

namespace servicio;

public class modifica_carrito_compra
{
    class Peticion
    {
        public int? id_usuario;
        public string? token;
        public int? id_articulo;
        public int? delta; // +1 incrementa, -1 decrementa
    }

    class HuboError
    {
        public string mensaje;
        public HuboError(string mensaje)
        {
            this.mensaje = mensaje;
        }
    }

    [Function("modifica_carrito_compra")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put")] HttpRequest req)
    {
        try
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            Peticion? p = JsonConvert.DeserializeObject<Peticion>(body);
            if (p == null) throw new Exception("Se esperan los datos de la modificación");

            if (p.id_usuario == null) throw new Exception("Falta id_usuario");
            if (string.IsNullOrWhiteSpace(p.token)) throw new Exception("Falta token");
            if (p.id_articulo == null) throw new Exception("Falta id_articulo");
            if (p.delta == null || p.delta == 0) throw new Exception("Delta inválido");

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

                // cantidad actual en carrito
                var cmdCar = new MySqlCommand(
                    @"SELECT cantidad FROM carrito_compra
                      WHERE id_usuario=@id_usuario AND id_articulo=@id_articulo");
                cmdCar.Connection = conexion;
                cmdCar.Transaction = tx;
                cmdCar.Parameters.AddWithValue("@id_usuario", p.id_usuario);
                cmdCar.Parameters.AddWithValue("@id_articulo", p.id_articulo);

                object? resCar = cmdCar.ExecuteScalar();
                int cantActual = resCar == null ? 0 : Convert.ToInt32(resCar);

                if (p.delta < 0 && cantActual == 0)
                    throw new Exception("El artículo no existe en el carrito");

                if (p.delta > 0)
                {
                    // validar stock
                    var cmdStock = new MySqlCommand(
                        "SELECT cantidad FROM stock WHERE id_articulo=@id_articulo");
                    cmdStock.Connection = conexion;
                    cmdStock.Transaction = tx;
                    cmdStock.Parameters.AddWithValue("@id_articulo", p.id_articulo);
                    object? resStock = cmdStock.ExecuteScalar();
                    if (resStock == null)
                        throw new Exception("El artículo no existe");

                    int disponible = Convert.ToInt32(resStock);
                    if (p.delta.Value > disponible)
                        throw new Exception("Stock insuficiente");

                    // actualizar/insertar carrito
                    if (cantActual == 0)
                    {
                        var cmdIns = new MySqlCommand(
                            @"INSERT INTO carrito_compra(id_carrito,id_usuario,id_articulo,cantidad)
                              VALUES (0,@id_usuario,@id_articulo,@cantidad)");
                        cmdIns.Connection = conexion;
                        cmdIns.Transaction = tx;
                        cmdIns.Parameters.AddWithValue("@id_usuario", p.id_usuario);
                        cmdIns.Parameters.AddWithValue("@id_articulo", p.id_articulo);
                        cmdIns.Parameters.AddWithValue("@cantidad", p.delta);
                        cmdIns.ExecuteNonQuery();
                    }
                    else
                    {
                        var cmdUpd = new MySqlCommand(
                            @"UPDATE carrito_compra
                              SET cantidad = cantidad + @delta
                              WHERE id_usuario=@id_usuario AND id_articulo=@id_articulo");
                        cmdUpd.Connection = conexion;
                        cmdUpd.Transaction = tx;
                        cmdUpd.Parameters.AddWithValue("@delta", p.delta);
                        cmdUpd.Parameters.AddWithValue("@id_usuario", p.id_usuario);
                        cmdUpd.Parameters.AddWithValue("@id_articulo", p.id_articulo);
                        cmdUpd.ExecuteNonQuery();
                    }

                    // descontar del stock
                    var cmdUpdStock = new MySqlCommand(
                        @"UPDATE stock
                          SET cantidad = cantidad - @delta
                          WHERE id_articulo=@id_articulo");
                    cmdUpdStock.Connection = conexion;
                    cmdUpdStock.Transaction = tx;
                    cmdUpdStock.Parameters.AddWithValue("@delta", p.delta);
                    cmdUpdStock.Parameters.AddWithValue("@id_articulo", p.id_articulo);
                    cmdUpdStock.ExecuteNonQuery();
                }
                else // delta < 0
                {
                    int nuevaCantidad = cantActual + p.delta.Value; // delta es negativo
                    int devolver = -p.delta.Value;

                    // regresar al stock
                    var cmdUpdStock = new MySqlCommand(
                        @"UPDATE stock
                          SET cantidad = cantidad + @dev
                          WHERE id_articulo=@id_articulo");
                    cmdUpdStock.Connection = conexion;
                    cmdUpdStock.Transaction = tx;
                    cmdUpdStock.Parameters.AddWithValue("@dev", devolver);
                    cmdUpdStock.Parameters.AddWithValue("@id_articulo", p.id_articulo);
                    cmdUpdStock.ExecuteNonQuery();

                    if (nuevaCantidad <= 0)
                    {
                        var cmdDel = new MySqlCommand(
                            @"DELETE FROM carrito_compra
                              WHERE id_usuario=@id_usuario AND id_articulo=@id_articulo");
                        cmdDel.Connection = conexion;
                        cmdDel.Transaction = tx;
                        cmdDel.Parameters.AddWithValue("@id_usuario", p.id_usuario);
                        cmdDel.Parameters.AddWithValue("@id_articulo", p.id_articulo);
                        cmdDel.ExecuteNonQuery();
                    }
                    else
                    {
                        var cmdUpd = new MySqlCommand(
                            @"UPDATE carrito_compra
                              SET cantidad=@cant
                              WHERE id_usuario=@id_usuario AND id_articulo=@id_articulo");
                        cmdUpd.Connection = conexion;
                        cmdUpd.Transaction = tx;
                        cmdUpd.Parameters.AddWithValue("@cant", nuevaCantidad);
                        cmdUpd.Parameters.AddWithValue("@id_usuario", p.id_usuario);
                        cmdUpd.Parameters.AddWithValue("@id_articulo", p.id_articulo);
                        cmdUpd.ExecuteNonQuery();
                    }
                }

                tx.Commit();
                return new OkObjectResult("{\"mensaje\":\"Carrito modificado\"}");
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
