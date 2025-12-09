// (c) Carlos Pineda Guerrero. 2025
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;

namespace servicio;
    public class alta_usuario
    {
        class Usuario
        {
            public string? email;
            public string? password;
            public string? nombre;
            public string? apellido_paterno;
            public string? apellido_materno;
            public DateTime? fecha_nacimiento;
            public long? telefono;
            public string? genero;
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
        [Function("alta_usuario")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
        {
            try
            {
                string body = await new StreamReader(req.Body).ReadToEndAsync();
                Usuario? usuario = JsonConvert.DeserializeObject<Usuario>(body);
                if (usuario == null) throw new Exception("Se esperan los datos del usuario");
                if (usuario.email == null || usuario.email == "") throw new Exception("Se debe ingresar el email");
                if (usuario.password == null || usuario.password == "") throw new Exception("Se debe ingresar el email");
                if (usuario.nombre == null || usuario.nombre == "") throw new Exception("Se debe ingresar el nombre");
                if (usuario.apellido_paterno == null || usuario.apellido_paterno == "") throw new Exception("Se debe ingresar el apellido_paterno");
                if (usuario.fecha_nacimiento == null) throw new Exception("Se debe ingresar la fecha de nacimiento");
                string? Server = Environment.GetEnvironmentVariable("Server");
                string? UserID = Environment.GetEnvironmentVariable("UserID");
                string? Password = Environment.GetEnvironmentVariable("Password");
                string? Database = Environment.GetEnvironmentVariable("Database");
                string cs = "Server=" + Server + ";UserID=" + UserID + ";Password=" + Password + ";" + "Database=" + Database + ";SslMode=Preferred;";
                var conexion = new MySqlConnection(cs);
                conexion.Open();
                MySqlTransaction transaccion = conexion.BeginTransaction();
                try
                {
                    var cmd_1 = new MySqlCommand();
                    cmd_1.Connection = conexion;
                    cmd_1.Transaction = transaccion;
                    cmd_1.CommandText = "INSERT INTO usuarios(id_usuario,email,password,nombre,apellido_paterno,apellido_materno,fecha_nacimiento,telefono,genero) VALUES (0,@email,@password,@nombre,@apellido_paterno,@apellido_materno,@fecha_nacimiento,@telefono,@genero)";
                    cmd_1.Parameters.AddWithValue("@email", usuario.email);
                    cmd_1.Parameters.AddWithValue("@password", usuario.password);
                    cmd_1.Parameters.AddWithValue("@nombre", usuario.nombre);
                    cmd_1.Parameters.AddWithValue("@apellido_paterno", usuario.apellido_paterno);
                    cmd_1.Parameters.AddWithValue("@apellido_materno", usuario.apellido_materno);
                    cmd_1.Parameters.AddWithValue("@fecha_nacimiento", usuario.fecha_nacimiento);
                    cmd_1.Parameters.AddWithValue("@telefono", usuario.telefono);
                    cmd_1.Parameters.AddWithValue("@genero", usuario.genero);
                    cmd_1.ExecuteNonQuery();
                    long id_usuario = cmd_1.LastInsertedId;
                    if (usuario.foto != null)
                    {
                        var cmd_2 = new MySqlCommand();
                        cmd_2.Connection = conexion;
                        cmd_2.Transaction = transaccion;
                        cmd_2.CommandText = "INSERT INTO fotos_usuarios (foto,id_usuario) VALUES (@foto,@id_usuario)";
                        cmd_2.Parameters.AddWithValue("@foto", Convert.FromBase64String(usuario.foto));
                        cmd_2.Parameters.AddWithValue("@id_usuario", id_usuario);
                        cmd_2.ExecuteNonQuery();
                    }
                    transaccion.Commit();
                    return new OkObjectResult("{\"mensaje\":\"Se dio de alta el usuario\"}");
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