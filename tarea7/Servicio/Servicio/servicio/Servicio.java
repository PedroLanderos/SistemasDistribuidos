package servicio;

import javax.ws.rs.GET;
import javax.ws.rs.POST;
import javax.ws.rs.PUT;
import javax.ws.rs.DELETE;
import javax.ws.rs.Path;
import javax.ws.rs.Consumes;
import javax.ws.rs.Produces;
import javax.ws.rs.core.MediaType;
import javax.ws.rs.QueryParam;
import javax.ws.rs.core.Response;

import java.sql.*;
import javax.sql.DataSource;
import javax.naming.Context;
import javax.naming.InitialContext;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.security.SecureRandom;
import java.math.BigDecimal;

import com.fasterxml.jackson.databind.ObjectMapper;

/*
 La URL del servicio web es http://localhost:8080/Servicio/rest/ws
 Donde:
    "Servicio" es el dominio del servicio web (es decir, el nombre de archivo Servicio.war)
    "rest" se define en la etiqueta <url-pattern> de <servlet-mapping> en el archivo WEB-INF\web.xml
    "ws" se define en la siguiente anotación @Path de la clase Servicio
*/

@Path("ws")
public class Servicio 
{
    static DataSource pool = null;
    static
    {        
        try
        {
            Context ctx = new InitialContext();
            pool = (DataSource)ctx.lookup("java:comp/env/jdbc/datasource_Servicio");
        }
        catch(Exception e)
        {
            e.printStackTrace();
        }
    }

    static ObjectMapper j = new ObjectMapper().setDateFormat(new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS"));

    static final String caracteres = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    static final SecureRandom random = new SecureRandom();

    static String generarToken(int longitud)
    {
        StringBuilder sb = new StringBuilder(longitud);
        for (int i = 0; i < longitud; i++)
        {
            int index = random.nextInt(caracteres.length());
            sb.append(caracteres.charAt(index));
        }
        return sb.toString();
    }

    boolean verifica_acceso(Connection conexion,String email,String token) throws Exception
    {
        PreparedStatement stmt_1 = conexion.prepareStatement("SELECT 1 FROM usuarios WHERE email=? and token=?");
        try
        {
            stmt_1.setString(1,email);
            stmt_1.setString(2,token);

            ResultSet rs = stmt_1.executeQuery();
            try
            {
                return rs.next();
            }
            finally
            {
                rs.close();
            }
        }
        finally
        {
            stmt_1.close();
        }
    }

    boolean verifica_acceso(Connection conexion,int id_usuario,String token) throws Exception
    {
        PreparedStatement stmt_1 = conexion.prepareStatement("SELECT 1 FROM usuarios WHERE id_usuario=? and token=?");
        try
        {
            stmt_1.setInt(1,id_usuario);
            stmt_1.setString(2,token);

            ResultSet rs = stmt_1.executeQuery();
            try
            {
                return rs.next();
            }
            finally
            {
                rs.close();
            }
        }
        finally
        {
            stmt_1.close();
        }
    }

    // ==========================================
    //  LOGIN  (regresa token + id_usuario)
    // ==========================================

    @GET
    @Path("login")
    @Produces(MediaType.APPLICATION_JSON)
    public Response login(@QueryParam("email") String email,@QueryParam("password") String password) throws Exception
    {
        Connection conexion= pool.getConnection();

        try
        {
            PreparedStatement stmt_1 = conexion.prepareStatement("SELECT id_usuario FROM usuarios WHERE email=? and password=?");
            try
            {
                stmt_1.setString(1,email);
                stmt_1.setString(2,password);

                ResultSet rs = stmt_1.executeQuery();
                try
                {
                    if (rs.next())
                    {
                        int id_usuario = rs.getInt(1);
                        String token = generarToken(20);

                        PreparedStatement stmt_2 = conexion.prepareStatement("UPDATE usuarios SET token=? WHERE id_usuario=?");
                        try
                        {
                            stmt_2.setString(1,token);
                            stmt_2.setInt(2,id_usuario);
                            stmt_2.executeUpdate();
                        }
                        finally
                        {
                            stmt_2.close();
                        }

                        String respuesta = "{\"id_usuario\":" + id_usuario + ",\"token\":\"" + token + "\"}";
                        return Response.ok(respuesta).build();
                    }
                    return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();
                }
                finally
                {
                    rs.close();
                }
            }
            finally
            {
                stmt_1.close();
            }
        }
        catch (Exception e)
        {
            return Response.status(400).entity(j.writeValueAsString(new HuboError(e.getMessage()))).build();
        }
        finally
        {
            conexion.close();
        }
    }

    // ==========================================
    //  ALTA USUARIO
    // ==========================================

    @POST
    @Path("alta_usuario")
    @Consumes(MediaType.APPLICATION_JSON)
    @Produces(MediaType.APPLICATION_JSON)
    public Response alta(Usuario usuario) throws Exception
    {
        Connection conexion = pool.getConnection();
        int id_usuario = 0;

        if (usuario.email == null || usuario.email.equals(""))
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Se debe ingresar el email"))).build();

        if (usuario.password == null || usuario.password.equals(""))
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Se debe ingresar la contraseña"))).build();

        if (usuario.nombre == null || usuario.nombre.equals(""))
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Se debe ingresar el nombre"))).build();

        if (usuario.apellido_paterno == null || usuario.apellido_paterno.equals(""))
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Se debe ingresar el apellido paterno"))).build();

        if (usuario.fecha_nacimiento == null)
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Se debe ingresar la fecha de nacimiento"))).build();

        try
        {
            conexion.setAutoCommit(false);

            PreparedStatement stmt_1 = conexion.prepareStatement(
                "INSERT INTO usuarios(id_usuario,email,password,nombre,apellido_paterno,apellido_materno,fecha_nacimiento,telefono,genero) VALUES (0,?,?,?,?,?,?,?,?)",
                Statement.RETURN_GENERATED_KEYS
            );

            try
            {
                stmt_1.setString(1,usuario.email);
                stmt_1.setString(2,usuario.password);
                stmt_1.setString(3,usuario.nombre);
                stmt_1.setString(4,usuario.apellido_paterno);

                if (usuario.apellido_materno != null)
                    stmt_1.setString(5,usuario.apellido_materno);
                else
                    stmt_1.setNull(5,Types.VARCHAR);

                stmt_1.setTimestamp(6,usuario.fecha_nacimiento);

                if (usuario.telefono != null)
                    stmt_1.setLong(7,usuario.telefono);
                else
                    stmt_1.setNull(7,Types.BIGINT);

                if (usuario.genero != null)
                    stmt_1.setString(8,usuario.genero);
                else
                    stmt_1.setNull(8,Types.CHAR);

                stmt_1.executeUpdate();
                
                ResultSet rs = stmt_1.getGeneratedKeys();
                try
                {
                    if (rs.next())
                        id_usuario = rs.getInt(1);
                }
                finally
                {
                    rs.close();
                }

                if (id_usuario == 0)
                    return Response.status(400).entity(j.writeValueAsString(new HuboError("No se pudo obtener el ID del usuario"))).build();
            }
            finally
            {
                stmt_1.close();
            }

            if (usuario.foto != null)
            {
                PreparedStatement stmt_2 = conexion.prepareStatement("INSERT INTO fotos_usuarios(id_foto,foto,id_usuario) VALUES (0,?,?)");
                try
                {
                    stmt_2.setBytes(1,usuario.foto);
                    stmt_2.setInt(2,id_usuario);
                    stmt_2.executeUpdate();
                }
                finally
                {
                    stmt_2.close();
                }
            }
            conexion.commit();
        }
        catch (Exception e)
        {
            conexion.rollback();
            return Response.status(400).entity(j.writeValueAsString(new HuboError(e.getMessage()))).build();
        }
        finally
        {
            conexion.setAutoCommit(true);
            conexion.close();
        }
        return Response.ok("{\"mensaje\":\"Se dio de alta el usuario\"}").build();
    }

    // ==========================================
    //  CONSULTA USUARIO
    // ==========================================

    @GET
    @Path("consulta_usuario")
    @Produces(MediaType.APPLICATION_JSON)
    public Response consulta(@QueryParam("email") String email,@QueryParam("token") String token) throws Exception
    {
        Connection conexion= pool.getConnection();

        if (!verifica_acceso(conexion,email,token))
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();

        try
        {
            PreparedStatement stmt_1 = conexion.prepareStatement(
                "SELECT a.email,a.nombre,a.apellido_paterno,a.apellido_materno,a.fecha_nacimiento,a.telefono,a.genero,b.foto " +
                "FROM usuarios a LEFT OUTER JOIN fotos_usuarios b ON a.id_usuario=b.id_usuario WHERE email=?"
            );
            try
            {
                stmt_1.setString(1,email);

                ResultSet rs = stmt_1.executeQuery();
                try
                {
                    if (rs.next())
                    {
                        Usuario r = new Usuario();
                        r.email = rs.getString(1);
                        r.nombre = rs.getString(2);
                        r.apellido_paterno = rs.getString(3);
                        r.apellido_materno = rs.getString(4);
                        r.fecha_nacimiento = rs.getTimestamp(5);
                        r.telefono = rs.getObject(6) != null ? rs.getLong(6) : null;
                        r.genero = rs.getString(7);
                        r.foto = rs.getBytes(8);
                        return Response.ok().entity(j.writeValueAsString(r)).build();
                    }
                    return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();
                }
                finally
                {
                    rs.close();
                }
            }
            finally
            {
                stmt_1.close();
            }
        }
        catch (Exception e)
        {
            return Response.status(400).entity(j.writeValueAsString(new HuboError(e.getMessage()))).build();
        }
        finally
        {
            conexion.close();
        }
    }

    // ==========================================
    //  MODIFICA USUARIO
    // ==========================================

    @PUT
    @Path("modifica_usuario")
    @Consumes(MediaType.APPLICATION_JSON)
    @Produces(MediaType.APPLICATION_JSON)
    public Response modifica(@QueryParam("email") String email,@QueryParam("token") String token,Usuario usuario) throws Exception
    {
        Connection conexion= pool.getConnection();

        if (!verifica_acceso(conexion,email,token))
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();

        if (usuario.nombre == null || usuario.nombre.equals(""))
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Se debe ingresar el nombre"))).build();

        if (usuario.apellido_paterno == null || usuario.apellido_paterno.equals(""))
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Se debe ingresar el apellido paterno"))).build();

        if (usuario.fecha_nacimiento == null)
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Se debe ingresar la fecha de nacimiento"))).build();

        conexion.setAutoCommit(false);
        try
        {
            PreparedStatement stmt_1 = conexion.prepareStatement(
                "UPDATE usuarios SET nombre=?,apellido_paterno=?,apellido_materno=?,fecha_nacimiento=?,telefono=?,genero=? WHERE email=?"
            );
            try
            {
                stmt_1.setString(1,usuario.nombre);
                stmt_1.setString(2,usuario.apellido_paterno);

                if (usuario.apellido_materno != null)
                    stmt_1.setString(3,usuario.apellido_materno);
                else
                    stmt_1.setNull(3,Types.VARCHAR);

                stmt_1.setTimestamp(4,usuario.fecha_nacimiento);

                if (usuario.telefono != null)
                    stmt_1.setLong(5,usuario.telefono);
                else
                    stmt_1.setNull(5,Types.BIGINT);

                if (usuario.genero != null)
                    stmt_1.setString(6,usuario.genero);
                else
                    stmt_1.setNull(6,Types.CHAR);

                stmt_1.setString(7,email);

                stmt_1.executeUpdate();
            }
            finally
            {
                stmt_1.close();
            }

            if (!usuario.password.equals(""))
            {
                PreparedStatement stmt_2 = conexion.prepareStatement("UPDATE usuarios SET password=? WHERE email=?");
                try
                {
                    stmt_2.setString(1,usuario.password);
                    stmt_2.setString(2,email);
                    stmt_2.executeUpdate();
                }
                finally
                {
                    stmt_2.close();
                }
            }

            PreparedStatement stmt_3 = conexion.prepareStatement("DELETE FROM fotos_usuarios WHERE id_usuario=(SELECT id_usuario FROM usuarios WHERE email=?)");
            try
            {
                stmt_3.setString(1,email);
                stmt_3.executeUpdate();
            }
            finally
            {
                stmt_3.close();
            }

            if (usuario.foto != null)
            {
                PreparedStatement stmt_4 = conexion.prepareStatement(
                    "INSERT INTO fotos_usuarios(id_foto,foto,id_usuario) VALUES (0,?,(SELECT id_usuario FROM usuarios WHERE email=?))"
                );
                try
                {
                    stmt_4.setBytes(1,usuario.foto);
                    stmt_4.setString(2,email);
                    stmt_4.executeUpdate();
                }
                finally
                {
                    stmt_4.close();
                }
            }

            conexion.commit();
            return Response.ok("{\"mensaje\":\"Se modificó el usuario\"}").build();      
        }
        catch (Exception e)
        {
            conexion.rollback();
            return Response.status(400).entity(j.writeValueAsString(new HuboError(e.getMessage()))).build();
        }
        finally
        {
            conexion.setAutoCommit(true);
            conexion.close();
        }
    }

    // ==========================================
    //  BORRA USUARIO
    // ==========================================

    @DELETE
    @Path("borra_usuario")
    @Produces(MediaType.APPLICATION_JSON)
    public Response borra(@QueryParam("email") String email,@QueryParam("token") String token) throws Exception
    {
        Connection conexion= pool.getConnection();

        if (!verifica_acceso(conexion,email,token))
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();

        try
        {
            PreparedStatement stmt_1 = conexion.prepareStatement("SELECT 1 FROM usuarios WHERE email=?");
            try
            {
                stmt_1.setString(1,email);

                ResultSet rs = stmt_1.executeQuery();
                try
                {
                    if (!rs.next())
                        return Response.status(400).entity(j.writeValueAsString(new HuboError("El email no existe"))).build();
                }
                finally
                {
                    rs.close();
                }
            }
            finally
            {
                stmt_1.close();
            }
            conexion.setAutoCommit(false);
            PreparedStatement stmt_2 = conexion.prepareStatement(
                "DELETE FROM fotos_usuarios WHERE id_usuario=(SELECT id_usuario FROM usuarios WHERE email=?)"
            );
            try
            {
                stmt_2.setString(1,email);
                stmt_2.executeUpdate();
            }
            finally
            {
                stmt_2.close();
            }

            PreparedStatement stmt_3 = conexion.prepareStatement("DELETE FROM usuarios WHERE email=?");
            try
            {
                stmt_3.setString(1,email);
                stmt_3.executeUpdate();
            }
            finally
            {
                stmt_3.close();
            }
            conexion.commit();
        }
        catch (Exception e)
        {
            conexion.rollback();
            return Response.status(400).entity(j.writeValueAsString(new HuboError(e.getMessage()))).build();
        }
        finally
        {
            conexion.setAutoCommit(true);
            conexion.close();
        }
        return Response.ok("{\"mensaje\":\"Se borró el usuario\"}").build();
    }

    // =====================================================
    //   ***  MÉTODOS NUEVOS PARA EL SISTEMA E-COMMERCE  ***
    // =====================================================

    // 1) alta_articulo
    @POST
    @Path("alta_articulo")
    @Consumes(MediaType.APPLICATION_JSON)
    @Produces(MediaType.APPLICATION_JSON)
    public Response altaArticulo(AltaArticuloRequest req) throws Exception
    {
        if (req == null)
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Solicitud inválida"))).build();

        if (req.nombre == null || req.nombre.equals(""))
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Se debe ingresar el nombre del artículo"))).build();

        if (req.precio == null)
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Se debe ingresar el precio del artículo"))).build();

        if (req.cantidad == null || req.cantidad <= 0)
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Se debe ingresar una cantidad válida en existencia"))).build();

        if (req.id_usuario == null || req.token == null)
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();

        Connection conexion = pool.getConnection();

        if (!verifica_acceso(conexion, req.id_usuario, req.token))
        {
            conexion.close();
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();
        }

        int id_articulo = 0;

        try
        {
            conexion.setAutoCommit(false);

            PreparedStatement stmt_1 = conexion.prepareStatement(
                "INSERT INTO stock(id_articulo,nombre,descripcion,precio,cantidad) VALUES (0,?,?,?,?)",
                Statement.RETURN_GENERATED_KEYS
            );
            try
            {
                stmt_1.setString(1, req.nombre);
                if (req.descripcion != null)
                    stmt_1.setString(2, req.descripcion);
                else
                    stmt_1.setNull(2, Types.VARCHAR);

                stmt_1.setBigDecimal(3, BigDecimal.valueOf(req.precio));
                stmt_1.setInt(4, req.cantidad);

                stmt_1.executeUpdate();

                ResultSet rs = stmt_1.getGeneratedKeys();
                try
                {
                    if (rs.next())
                        id_articulo = rs.getInt(1);
                }
                finally
                {
                    rs.close();
                }
            }
            finally
            {
                stmt_1.close();
            }

            if (id_articulo == 0)
                throw new Exception("No se pudo obtener el ID del artículo");

            if (req.foto != null)
            {
                PreparedStatement stmt_2 = conexion.prepareStatement(
                    "INSERT INTO fotos_articulos(id_foto,foto,id_articulo) VALUES (0,?,?)"
                );
                try
                {
                    stmt_2.setBytes(1, req.foto);
                    stmt_2.setInt(2, id_articulo);
                    stmt_2.executeUpdate();
                }
                finally
                {
                    stmt_2.close();
                }
            }

            conexion.commit();
            return Response.ok("{\"mensaje\":\"Se dio de alta el artículo\"}").build();
        }
        catch (Exception e)
        {
            conexion.rollback();
            return Response.status(400).entity(j.writeValueAsString(new HuboError(e.getMessage()))).build();
        }
        finally
        {
            conexion.setAutoCommit(true);
            conexion.close();
        }
    }

    // 2) consulta_articulos
    @GET
    @Path("consulta_articulos")
    @Produces(MediaType.APPLICATION_JSON)
    public Response consultaArticulos(
        @QueryParam("palabra") String palabra,
        @QueryParam("id_usuario") int id_usuario,
        @QueryParam("token") String token) throws Exception
    {
        Connection conexion = pool.getConnection();

        if (!verifica_acceso(conexion, id_usuario, token))
        {
            conexion.close();
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();
        }

        if (palabra == null)
            palabra = "";
        String like = "%" + palabra + "%";

        try
        {
            PreparedStatement stmt = conexion.prepareStatement(
                "SELECT s.id_articulo,s.nombre,s.descripcion,s.precio,f.foto " +
                "FROM stock s LEFT OUTER JOIN fotos_articulos f ON s.id_articulo=f.id_articulo " +
                "WHERE s.nombre LIKE ? OR s.descripcion LIKE ?"
            );
            try
            {
                stmt.setString(1, like);
                stmt.setString(2, like);

                ResultSet rs = stmt.executeQuery();
                try
                {
                    ArrayList<Articulo> lista = new ArrayList<Articulo>();
                    while (rs.next())
                    {
                        Articulo a = new Articulo();
                        a.id_articulo = rs.getInt(1);
                        a.nombre = rs.getString(2);
                        a.descripcion = rs.getString(3);
                        a.precio = rs.getDouble(4);
                        a.cantidad = null; // no exponemos existencia aquí
                        a.foto = rs.getBytes(5);
                        lista.add(a);
                    }
                    return Response.ok(j.writeValueAsString(lista)).build();
                }
                finally
                {
                    rs.close();
                }
            }
            finally
            {
                stmt.close();
            }
        }
        catch (Exception e)
        {
            return Response.status(400).entity(j.writeValueAsString(new HuboError(e.getMessage()))).build();
        }
        finally
        {
            conexion.close();
        }
    }

    // 3) compra_articulo
    @POST
    @Path("compra_articulo")
    @Consumes(MediaType.APPLICATION_JSON)
    @Produces(MediaType.APPLICATION_JSON)
    public Response compraArticulo(CompraArticuloRequest req) throws Exception
    {
        if (req == null)
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Solicitud inválida"))).build();

        if (req.id_usuario == null || req.token == null)
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();

        if (req.id_articulo == null || req.cantidad == null || req.cantidad <= 0)
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Cantidad inválida"))).build();

        Connection conexion = pool.getConnection();

        if (!verifica_acceso(conexion, req.id_usuario, req.token))
        {
            conexion.close();
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();
        }

        try
        {
            conexion.setAutoCommit(false);

            // Consultar stock
            PreparedStatement stmtStock = conexion.prepareStatement(
                "SELECT cantidad FROM stock WHERE id_articulo=? FOR UPDATE"
            );
            int cantidadActual = 0;
            try
            {
                stmtStock.setInt(1, req.id_articulo);
                ResultSet rs = stmtStock.executeQuery();
                try
                {
                    if (!rs.next())
                        throw new Exception("El artículo no existe");

                    cantidadActual = rs.getInt(1);
                }
                finally
                {
                    rs.close();
                }
            }
            finally
            {
                stmtStock.close();
            }

            if (req.cantidad > cantidadActual)
            {
                conexion.rollback();
                return Response.status(400).entity(j.writeValueAsString(new HuboError("No hay suficientes artículos en stock"))).build();
            }

            // Ver si ya existe en carrito
            int cantidadEnCarrito = 0;
            boolean existe = false;

            PreparedStatement stmtCarritoSel = conexion.prepareStatement(
                "SELECT cantidad FROM carrito_compra WHERE id_usuario=? AND id_articulo=? FOR UPDATE"
            );
            try
            {
                stmtCarritoSel.setInt(1, req.id_usuario);
                stmtCarritoSel.setInt(2, req.id_articulo);
                ResultSet rs = stmtCarritoSel.executeQuery();
                try
                {
                    if (rs.next())
                    {
                        existe = true;
                        cantidadEnCarrito = rs.getInt(1);
                    }
                }
                finally
                {
                    rs.close();
                }
            }
            finally
            {
                stmtCarritoSel.close();
            }

            if (existe)
            {
                PreparedStatement stmtUpdateCar = conexion.prepareStatement(
                    "UPDATE carrito_compra SET cantidad=? WHERE id_usuario=? AND id_articulo=?"
                );
                try
                {
                    stmtUpdateCar.setInt(1, cantidadEnCarrito + req.cantidad);
                    stmtUpdateCar.setInt(2, req.id_usuario);
                    stmtUpdateCar.setInt(3, req.id_articulo);
                    stmtUpdateCar.executeUpdate();
                }
                finally
                {
                    stmtUpdateCar.close();
                }
            }
            else
            {
                PreparedStatement stmtInsertCar = conexion.prepareStatement(
                    "INSERT INTO carrito_compra(id_usuario,id_articulo,cantidad) VALUES (?,?,?)"
                );
                try
                {
                    stmtInsertCar.setInt(1, req.id_usuario);
                    stmtInsertCar.setInt(2, req.id_articulo);
                    stmtInsertCar.setInt(3, req.cantidad);
                    stmtInsertCar.executeUpdate();
                }
                finally
                {
                    stmtInsertCar.close();
                }
            }

            // Actualizar stock
            PreparedStatement stmtUpdateStock = conexion.prepareStatement(
                "UPDATE stock SET cantidad = cantidad - ? WHERE id_articulo=?"
            );
            try
            {
                stmtUpdateStock.setInt(1, req.cantidad);
                stmtUpdateStock.setInt(2, req.id_articulo);
                stmtUpdateStock.executeUpdate();
            }
            finally
            {
                stmtUpdateStock.close();
            }

            conexion.commit();
            return Response.ok("{\"mensaje\":\"Compra realizada\"}").build();
        }
        catch (Exception e)
        {
            conexion.rollback();
            return Response.status(400).entity(j.writeValueAsString(new HuboError(e.getMessage()))).build();
        }
        finally
        {
            conexion.setAutoCommit(true);
            conexion.close();
        }
    }

    // 4) elimina_articulo_carrito_compra
    @DELETE
    @Path("elimina_articulo_carrito_compra")
    @Produces(MediaType.APPLICATION_JSON)
    public Response eliminaArticuloCarrito(
        @QueryParam("id_usuario") int id_usuario,
        @QueryParam("id_articulo") int id_articulo,
        @QueryParam("token") String token) throws Exception
    {
        Connection conexion = pool.getConnection();

        if (!verifica_acceso(conexion, id_usuario, token))
        {
            conexion.close();
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();
        }

        try
        {
            conexion.setAutoCommit(false);

            int cantidadCarrito = 0;

            PreparedStatement stmtSel = conexion.prepareStatement(
                "SELECT cantidad FROM carrito_compra WHERE id_usuario=? AND id_articulo=? FOR UPDATE"
            );
            try
            {
                stmtSel.setInt(1, id_usuario);
                stmtSel.setInt(2, id_articulo);

                ResultSet rs = stmtSel.executeQuery();
                try
                {
                    if (!rs.next())
                        throw new Exception("El artículo no está en el carrito");

                    cantidadCarrito = rs.getInt(1);
                }
                finally
                {
                    rs.close();
                }
            }
            finally
            {
                stmtSel.close();
            }

            // Regresar al stock
            PreparedStatement stmtUpdateStock = conexion.prepareStatement(
                "UPDATE stock SET cantidad = cantidad + ? WHERE id_articulo=?"
            );
            try
            {
                stmtUpdateStock.setInt(1, cantidadCarrito);
                stmtUpdateStock.setInt(2, id_articulo);
                stmtUpdateStock.executeUpdate();
            }
            finally
            {
                stmtUpdateStock.close();
            }

            // Borrar del carrito
            PreparedStatement stmtDel = conexion.prepareStatement(
                "DELETE FROM carrito_compra WHERE id_usuario=? AND id_articulo=?"
            );
            try
            {
                stmtDel.setInt(1, id_usuario);
                stmtDel.setInt(2, id_articulo);
                stmtDel.executeUpdate();
            }
            finally
            {
                stmtDel.close();
            }

            conexion.commit();
            return Response.ok("{\"mensaje\":\"Se eliminó el artículo del carrito\"}").build();
        }
        catch (Exception e)
        {
            conexion.rollback();
            return Response.status(400).entity(j.writeValueAsString(new HuboError(e.getMessage()))).build();
        }
        finally
        {
            conexion.setAutoCommit(true);
            conexion.close();
        }
    }

    // 5) elimina_carrito_compra
    @DELETE
    @Path("elimina_carrito_compra")
    @Produces(MediaType.APPLICATION_JSON)
    public Response eliminaCarrito(
        @QueryParam("id_usuario") int id_usuario,
        @QueryParam("token") String token) throws Exception
    {
        Connection conexion = pool.getConnection();

        if (!verifica_acceso(conexion, id_usuario, token))
        {
            conexion.close();
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();
        }

        try
        {
            conexion.setAutoCommit(false);

            // Obtener artículos del carrito
            PreparedStatement stmtSel = conexion.prepareStatement(
                "SELECT id_articulo,cantidad FROM carrito_compra WHERE id_usuario=? FOR UPDATE"
            );
            ArrayList<int[]> lista = new ArrayList<int[]>();
            try
            {
                stmtSel.setInt(1, id_usuario);
                ResultSet rs = stmtSel.executeQuery();
                try
                {
                    while (rs.next())
                    {
                        int[] par = new int[2];
                        par[0] = rs.getInt(1); // id_articulo
                        par[1] = rs.getInt(2); // cantidad
                        lista.add(par);
                    }
                }
                finally
                {
                    rs.close();
                }
            }
            finally
            {
                stmtSel.close();
            }

            // Regresar cantidades al stock
            for (int[] par : lista)
            {
                PreparedStatement stmtUpdate = conexion.prepareStatement(
                    "UPDATE stock SET cantidad = cantidad + ? WHERE id_articulo=?"
                );
                try
                {
                    stmtUpdate.setInt(1, par[1]);
                    stmtUpdate.setInt(2, par[0]);
                    stmtUpdate.executeUpdate();
                }
                finally
                {
                    stmtUpdate.close();
                }
            }

            // Borrar carrito
            PreparedStatement stmtDel = conexion.prepareStatement(
                "DELETE FROM carrito_compra WHERE id_usuario=?"
            );
            try
            {
                stmtDel.setInt(1, id_usuario);
                stmtDel.executeUpdate();
            }
            finally
            {
                stmtDel.close();
            }

            conexion.commit();
            return Response.ok("{\"mensaje\":\"Se eliminó el carrito de compra\"}").build();
        }
        catch (Exception e)
        {
            conexion.rollback();
            return Response.status(400).entity(j.writeValueAsString(new HuboError(e.getMessage()))).build();
        }
        finally
        {
            conexion.setAutoCommit(true);
            conexion.close();
        }
    }

    // 6) modifica_carrito_compra
    @PUT
    @Path("modifica_carrito_compra")
    @Produces(MediaType.APPLICATION_JSON)
    public Response modificaCarrito(
        @QueryParam("id_usuario") int id_usuario,
        @QueryParam("id_articulo") int id_articulo,
        @QueryParam("incremento") int incremento,
        @QueryParam("token") String token) throws Exception
    {
        Connection conexion = pool.getConnection();

        if (!verifica_acceso(conexion, id_usuario, token))
        {
            conexion.close();
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();
        }

        if (incremento != 1 && incremento != -1)
        {
            conexion.close();
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Incremento inválido"))).build();
        }

        try
        {
            conexion.setAutoCommit(false);

            // Cantidad actual en carrito
            int cantidadCarrito = 0;

            PreparedStatement stmtCarrito = conexion.prepareStatement(
                "SELECT cantidad FROM carrito_compra WHERE id_usuario=? AND id_articulo=? FOR UPDATE"
            );
            try
            {
                stmtCarrito.setInt(1, id_usuario);
                stmtCarrito.setInt(2, id_articulo);

                ResultSet rs = stmtCarrito.executeQuery();
                try
                {
                    if (!rs.next())
                        throw new Exception("El artículo no está en el carrito");

                    cantidadCarrito = rs.getInt(1);
                }
                finally
                {
                    rs.close();
                }
            }
            finally
            {
                stmtCarrito.close();
            }

            if (incremento == 1)
            {
                // Verificar stock
                int cantidadStock = 0;
                PreparedStatement stmtStock = conexion.prepareStatement(
                    "SELECT cantidad FROM stock WHERE id_articulo=? FOR UPDATE"
                );
                try
                {
                    stmtStock.setInt(1, id_articulo);
                    ResultSet rs = stmtStock.executeQuery();
                    try
                    {
                        if (!rs.next())
                            throw new Exception("El artículo no existe");

                        cantidadStock = rs.getInt(1);
                    }
                    finally
                    {
                        rs.close();
                    }
                }
                finally
                {
                    stmtStock.close();
                }

                if (cantidadStock <= 0)
                {
                    conexion.rollback();
                    return Response.status(400).entity(j.writeValueAsString(new HuboError("No hay suficientes artículos en stock"))).build();
                }

                // Incrementar carrito
                PreparedStatement stmtUpdateCar = conexion.prepareStatement(
                    "UPDATE carrito_compra SET cantidad = cantidad + 1 WHERE id_usuario=? AND id_articulo=?"
                );
                try
                {
                    stmtUpdateCar.setInt(1, id_usuario);
                    stmtUpdateCar.setInt(2, id_articulo);
                    stmtUpdateCar.executeUpdate();
                }
                finally
                {
                    stmtUpdateCar.close();
                }

                // Decrementar stock
                PreparedStatement stmtUpdateStock = conexion.prepareStatement(
                    "UPDATE stock SET cantidad = cantidad - 1 WHERE id_articulo=?"
                );
                try
                {
                    stmtUpdateStock.setInt(1, id_articulo);
                    stmtUpdateStock.executeUpdate();
                }
                finally
                {
                    stmtUpdateStock.close();
                }
            }
            else if (incremento == -1)
            {
                if (cantidadCarrito <= 0)
                {
                    conexion.rollback();
                    return Response.status(400).entity(j.writeValueAsString(new HuboError("No hay más artículos en el carrito"))).build();
                }

                // Disminuir carrito
                if (cantidadCarrito == 1)
                {
                    // Borrar fila
                    PreparedStatement stmtDel = conexion.prepareStatement(
                        "DELETE FROM carrito_compra WHERE id_usuario=? AND id_articulo=?"
                    );
                    try
                    {
                        stmtDel.setInt(1, id_usuario);
                        stmtDel.setInt(2, id_articulo);
                        stmtDel.executeUpdate();
                    }
                    finally
                    {
                        stmtDel.close();
                    }
                }
                else
                {
                    PreparedStatement stmtUpdateCar = conexion.prepareStatement(
                        "UPDATE carrito_compra SET cantidad = cantidad - 1 WHERE id_usuario=? AND id_articulo=?"
                    );
                    try
                    {
                        stmtUpdateCar.setInt(1, id_usuario);
                        stmtUpdateCar.setInt(2, id_articulo);
                        stmtUpdateCar.executeUpdate();
                    }
                    finally
                    {
                        stmtUpdateCar.close();
                    }
                }

                // Regresar uno al stock
                PreparedStatement stmtUpdateStock = conexion.prepareStatement(
                    "UPDATE stock SET cantidad = cantidad + 1 WHERE id_articulo=?"
                );
                try
                {
                    stmtUpdateStock.setInt(1, id_articulo);
                    stmtUpdateStock.executeUpdate();
                }
                finally
                {
                    stmtUpdateStock.close();
                }
            }

            conexion.commit();
            return Response.ok("{\"mensaje\":\"Se modificó el carrito\"}").build();
        }
        catch (Exception e)
        {
            conexion.rollback();
            return Response.status(400).entity(j.writeValueAsString(new HuboError(e.getMessage()))).build();
        }
        finally
        {
            conexion.setAutoCommit(true);
            conexion.close();
        }
    }

    // 7) consulta_carrito_compra  (para el front)
    @GET
    @Path("consulta_carrito_compra")
    @Produces(MediaType.APPLICATION_JSON)
    public Response consultaCarrito(
        @QueryParam("id_usuario") int id_usuario,
        @QueryParam("token") String token) throws Exception
    {
        Connection conexion = pool.getConnection();

        if (!verifica_acceso(conexion, id_usuario, token))
        {
            conexion.close();
            return Response.status(400).entity(j.writeValueAsString(new HuboError("Acceso denegado"))).build();
        }

        try
        {
            PreparedStatement stmt = conexion.prepareStatement(
                "SELECT c.id_articulo, s.nombre, s.descripcion, s.precio, c.cantidad, f.foto " +
                "FROM carrito_compra c " +
                "JOIN stock s ON c.id_articulo = s.id_articulo " +
                "LEFT OUTER JOIN fotos_articulos f ON s.id_articulo = f.id_articulo " +
                "WHERE c.id_usuario=?"
            );
            try
            {
                stmt.setInt(1, id_usuario);
                ResultSet rs = stmt.executeQuery();
                try
                {
                    ArrayList<ItemCarrito> lista = new ArrayList<ItemCarrito>();
                    while (rs.next())
                    {
                        ItemCarrito item = new ItemCarrito();
                        item.id_articulo = rs.getInt(1);
                        item.nombre = rs.getString(2);
                        item.descripcion = rs.getString(3);
                        item.precio = rs.getDouble(4);
                        item.cantidad = rs.getInt(5);
                        item.costo = item.precio * item.cantidad;
                        item.foto = rs.getBytes(6);
                        lista.add(item);
                    }
                    return Response.ok(j.writeValueAsString(lista)).build();
                }
                finally
                {
                    rs.close();
                }
            }
            finally
            {
                stmt.close();
            }
        }
        catch (Exception e)
        {
            return Response.status(400).entity(j.writeValueAsString(new HuboError(e.getMessage()))).build();
        }
        finally
        {
            conexion.close();
        }
    }
}
