// (c) Carlos Pineda Guerrero. 2025
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;
using System.Security.Cryptography;
using System.Text;

namespace servicio;

public class login
{
    class HuboError
    {
        public string mensaje;
        public HuboError(string mensaje)
        {
            this.mensaje = mensaje;
        }
    }
    private static readonly string caracteres = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    public static string genera_token(int longitud)
    {
        StringBuilder sb = new StringBuilder(longitud);
        byte[] buffer = new byte[1];

        for (int i = 0; i < longitud; i++)
        {
            RandomNumberGenerator.Fill(buffer);
            int index = buffer[0] % caracteres.Length;
            sb.Append(caracteres[index]);
        }

        return sb.ToString();
    }
    public static bool verifica_acceso(MySqlConnection conexion,String email,String token)
    {
        var cmd = new MySqlCommand("SELECT 1 FROM usuarios WHERE email=@email and token=@token");
        cmd.Connection = conexion;
        cmd.Parameters.AddWithValue("@email", email);
        cmd.Parameters.AddWithValue("@token", token);
        MySqlDataReader r = cmd.ExecuteReader();
        try
        {
            return r.Read();
        }
        finally
        {
            r.Close();
        }
    }
    public static bool verifica_acceso(MySqlConnection conexion,int id_usuario,String token)
    {
        var cmd = new MySqlCommand("SELECT 1 FROM usuarios WHERE id_usuario=@id_usuario and token=@token");
        cmd.Connection = conexion;
        cmd.Parameters.AddWithValue("@id_usuario", id_usuario);
        cmd.Parameters.AddWithValue("@token", token);
        MySqlDataReader r = cmd.ExecuteReader();
        try
        {
            return r.Read();
        }
        finally
        {
            r.Close();
        }
    }
    [Function("login")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        try
        {
            string? email = req.Query["email"];
            string? password = req.Query["password"];
            string? Server = Environment.GetEnvironmentVariable("Server");
            string? UserID = Environment.GetEnvironmentVariable("UserID");
            string? Password = Environment.GetEnvironmentVariable("Password");
            string? Database = Environment.GetEnvironmentVariable("Database");
            string cs = "Server=" + Server + ";UserID=" + UserID + ";Password=" + Password + ";" + "Database=" + Database + ";SslMode=Preferred;";
            var conexion = new MySqlConnection(cs);
            conexion.Open();
            try
            {
                var cmd_1 = new MySqlCommand("SELECT 1 FROM usuarios WHERE email=@email and password=@password");
                cmd_1.Connection = conexion;
                cmd_1.Parameters.AddWithValue("@email", email);
                cmd_1.Parameters.AddWithValue("@password", password);
                MySqlDataReader r = cmd_1.ExecuteReader();
                bool existe = r.Read();
                r.Close(); // es necesario cerrar el DataReader antes de ejecutar otro comando
                if (existe)
                {
                    string token = genera_token(20);
                    var cmd_2 = new MySqlCommand();
                    cmd_2.Connection = conexion;
                    cmd_2.CommandText = "UPDATE usuarios SET token=@token WHERE email=@email";
                    cmd_2.Parameters.AddWithValue("@token", token);
                    cmd_2.Parameters.AddWithValue("@email", email);
                    cmd_2.ExecuteNonQuery();
                    return new OkObjectResult("{\"token\":\"" + token + "\"}");
                }
                throw new Exception("Acceso denegado");
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