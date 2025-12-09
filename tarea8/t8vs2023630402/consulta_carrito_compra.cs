// (c) Carlos Pineda Guerrero. 2025
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;

namespace servicio;

public class consulta_carrito_compra
{
    class HuboError
    {
        public string mensaje;
        public HuboError(string mensaje)
        {
            this.mensaje = mensaje;
        }
    }

    class ItemCarrito
    {
        public int? id_articulo;
        public string? nombre;
        public string? descripcion;
        public decimal? precio;
        public int? cantidad;
        public string? foto;
    }

    [Function("consulta_carrito_compra")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        try
        {
            // Puede venir id_usuario (colección) o email (front)
            string? idUsuarioStr = req.Query["id_usuario"];
            string? email       = req.Query["email"];
            string? token       = req.Query["token"];

            if (string.IsNullOrEmpty(token))
                throw new Exception("Se debe ingresar el token");

            if (string.IsNullOrEmpty(idUsuarioStr) && string.IsNullOrEmpty(email))
                throw new Exception("Se debe ingresar el id_usuario o el email");

            string? Server   = Environment.GetEnvironmentVariable("Server");
            string? UserID   = Environment.GetEnvironmentVariable("UserID");
            string? Password = Environment.GetEnvironmentVariable("Password");
            string? Database = Environment.GetEnvironmentVariable("Database");

            string cs = "Server=" + Server +
                        ";UserID=" + UserID +
                        ";Password=" + Password +
                        ";Database=" + Database +
                        ";SslMode=Preferred;";

            var conexion = new MySqlConnection(cs);
            conexion.Open();
            try
            {
                int idUsuario;

                if (!string.IsNullOrEmpty(idUsuarioStr))
                {
                    // Forma: id_usuario + token
                    if (!int.TryParse(idUsuarioStr, out idUsuario))
                        throw new Exception("El id_usuario debe ser numérico");

                    if (!login.verifica_acceso(conexion, idUsuario, token))
                        throw new Exception("Acceso denegado");
                }
                else
                {
                    // Forma: email + token (lo que usa el front)
                    if (string.IsNullOrEmpty(email))
                        throw new Exception("Se debe ingresar el email");

                    if (!login.verifica_acceso(conexion, email, token))
                        throw new Exception("Acceso denegado");

                    // Obtener id_usuario a partir del email + token
                    var cmdId = new MySqlCommand(
                        "SELECT id_usuario FROM usuarios WHERE email=@email AND token=@token",
                        conexion);
                    cmdId.Parameters.AddWithValue("@email", email);
                    cmdId.Parameters.AddWithValue("@token", token);

                    MySqlDataReader rId = cmdId.ExecuteReader();
                    try
                    {
                        if (!rId.Read())
                            throw new Exception("No se encontró el usuario");

                        idUsuario = rId.GetInt32(0);
                    }
                    finally
                    {
                        rId.Close();
                    }
                }

                // Consulta del carrito con join a stock y foto
                var cmd = new MySqlCommand(
                    "SELECT c.id_articulo, s.nombre, s.descripcion, s.precio, " +
                    "       c.cantidad, f.foto, LENGTH(f.foto) " +
                    "FROM carrito_compra c " +
                    "INNER JOIN stock s ON c.id_articulo = s.id_articulo " +
                    "LEFT OUTER JOIN fotos_articulos f ON s.id_articulo = f.id_articulo " +
                    "WHERE c.id_usuario = @id_usuario",
                    conexion);

                cmd.Parameters.AddWithValue("@id_usuario", idUsuario);

                MySqlDataReader r = cmd.ExecuteReader();
                try
                {
                    var lista = new List<ItemCarrito>();

                    while (r.Read())
                    {
                        var item = new ItemCarrito();
                        item.id_articulo = r.GetInt32(0);
                        item.nombre      = r.GetString(1);
                        item.descripcion = r.GetString(2);
                        item.precio      = r.GetDecimal(3);
                        item.cantidad    = r.GetInt32(4);

                        if (!r.IsDBNull(5))
                        {
                            int longitud = r.GetInt32(6);
                            byte[] fotoBytes = new byte[longitud];
                            r.GetBytes(5, 0, fotoBytes, 0, longitud);
                            item.foto = Convert.ToBase64String(fotoBytes);
                        }

                        lista.Add(item);
                    }

                    return new OkObjectResult(JsonConvert.SerializeObject(lista));
                }
                finally
                {
                    r.Close();
                }
            }
            finally
            {
                conexion.Close();
            }
        }
        catch (Exception e)
        {
            return new BadRequestObjectResult(
                JsonConvert.SerializeObject(new HuboError(e.Message))
            );
        }
    }
}
