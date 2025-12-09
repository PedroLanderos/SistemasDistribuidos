// (c) Carlos Pineda Guerrero / adaptado para artículos
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;

namespace servicio;

public class consulta_articulos
{
    class Articulo
    {
        public int? id_articulo;
        public string? nombre;
        public string? descripcion;
        public decimal? precio;
        public int? cantidad;
        public string? foto;
    }

    class HuboError
    {
        public string mensaje;
        public HuboError(string mensaje)
        {
            this.mensaje = mensaje;
        }
    }

    [Function("consulta_articulos")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        try
        {
            string? palabra = req.Query["palabra"];
            string? id_usuario_str = req.Query["id_usuario"];
            string? token = req.Query["token"];

            if (string.IsNullOrWhiteSpace(palabra))
                throw new Exception("Debe indicar una palabra clave");
            if (string.IsNullOrWhiteSpace(id_usuario_str))
                throw new Exception("Falta id_usuario");
            if (string.IsNullOrWhiteSpace(token))
                throw new Exception("Falta token");

            int id_usuario = int.Parse(id_usuario_str);

            string? Server = Environment.GetEnvironmentVariable("Server");
            string? UserID = Environment.GetEnvironmentVariable("UserID");
            string? Password = Environment.GetEnvironmentVariable("Password");
            string? Database = Environment.GetEnvironmentVariable("Database");

            string cs = "Server=" + Server + ";UserID=" + UserID + ";Password=" + Password +
                        ";Database=" + Database + ";SslMode=Preferred;";

            var conexion = new MySqlConnection(cs);
            conexion.Open();

            try
            {
                if (!login.verifica_acceso(conexion, id_usuario, token!))
                    throw new Exception("Acceso denegado");

                var lista = new List<Articulo>();

                var cmd = new MySqlCommand(
                    @"SELECT a.id_articulo,a.nombre,a.descripcion,
                             a.precio,a.cantidad,
                             b.foto,LENGTH(b.foto)
                      FROM stock a
                      LEFT OUTER JOIN fotos_articulos b
                        ON a.id_articulo = b.id_articulo
                      WHERE a.nombre LIKE @palabra
                         OR a.descripcion LIKE @palabra");

                cmd.Connection = conexion;
                cmd.Parameters.AddWithValue("@palabra", "%" + palabra + "%");

                MySqlDataReader r = cmd.ExecuteReader();
                try
                {
                    while (r.Read())
                    {
                        var art = new Articulo();
                        art.id_articulo = r.GetInt32(0);
                        art.nombre = r.GetString(1);
                        art.descripcion = r.GetString(2);
                        art.precio = r.GetDecimal(3);
                        art.cantidad = r.GetInt32(4);

                        if (!r.IsDBNull(5))
                        {
                            int len = r.GetInt32(6);
                            byte[] foto = new byte[len];
                            r.GetBytes(5, 0, foto, 0, len);
                            art.foto = Convert.ToBase64String(foto);
                        }

                        lista.Add(art);
                    }
                }
                finally
                {
                    r.Close();
                }

                return new OkObjectResult(JsonConvert.SerializeObject(lista));
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
