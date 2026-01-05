/*
SimpleAPIGateway.java (LOCAL)
- HTTPS 8443 (recomendado local para no pelear con permisos del 443)
- Enrutamiento por path exacto
- Reescritura /api/... -> /gu/... /ga/... /gc/... /api/Get

Variables:
set keystore=keystore_servidor.jks
set password=1234567
*/

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.ServerSocket;
import java.net.Socket;
import javax.net.ssl.SSLServerSocketFactory;

class SimpleAPIGateway
{
  // { PATH_ENTRADA, HOST, PUERTO, PATH_SALIDA }
  // { PATH_ENTRADA, HOST, PUERTO, PATH_SALIDA }
static String[][] rutas =
{
  // SW (GET service)
  {"/api/Get", "t9-2023630402-sw-svc", "80", "/api/Get"},

  // GU
  {"/api/alta_usuario",     "t9-2023630402-gu-svc", "80", "/gu/alta_usuario"},
  {"/api/consulta_usuario", "t9-2023630402-gu-svc", "80", "/gu/consulta_usuario"},
  {"/api/modifica_usuario", "t9-2023630402-gu-svc", "80", "/gu/modifica_usuario"},
  {"/api/borra_usuario",    "t9-2023630402-gu-svc", "80", "/gu/borra_usuario"},
  {"/api/login",            "t9-2023630402-gu-svc", "80", "/gu/login"},
  {"/api/verifica_acceso",  "t9-2023630402-gu-svc", "80", "/gu/verifica_acceso"},

  // GA
  {"/api/alta_articulo",      "t9-2023630402-ga-svc", "80", "/ga/alta_articulo"},
  {"/api/consulta_articulos", "t9-2023630402-ga-svc", "80", "/ga/consulta_articulos"},
  {"/api/consulta_articulo",  "t9-2023630402-ga-svc", "80", "/ga/consulta_articulo"},

  // GC
  {"/api/alta_articulo_gc",                "t9-2023630402-gc-svc", "80", "/gc/alta_articulo"},
  {"/api/compra_articulo",                 "t9-2023630402-gc-svc", "80", "/gc/compra_articulo"},
  {"/api/consulta_carrito_compra",         "t9-2023630402-gc-svc", "80", "/gc/consulta_carrito_compra"},
  {"/api/elimina_articulo_carrito_compra", "t9-2023630402-gc-svc", "80", "/gc/elimina_articulo_carrito_compra"},
  {"/api/elimina_carrito_compra",          "t9-2023630402-gc-svc", "80", "/gc/elimina_carrito_compra"},
  {"/api/modifica_carrito_compra",         "t9-2023630402-gc-svc", "80", "/gc/modifica_carrito_compra"},

  // fallback "/" -> prueba.html (servido por SW)
  {"/", "t9-2023630402-sw-svc", "80", "/api/Get?nombre=/prueba.html"}
};


  static int TIMEOUT_READ = 5000;
  static Object obj = new Object();

  static class Worker_1 extends Thread
  {
    Socket cliente_1, cliente_2;

    Worker_1(Socket cliente_1) { this.cliente_1 = cliente_1; }

    String readLine(InputStream in) throws IOException
    {
      StringBuilder line = new StringBuilder();
      int ch;
      boolean gotCR = false;

      while ((ch = in.read()) != -1)
      {
        if (ch == '\r') { gotCR = true; continue; }
        if (ch == '\n') break;

        if (gotCR) { line.append('\r'); gotCR = false; }
        line.append((char) ch);
      }

      if (ch == -1 && line.length() == 0) return null;
      return line.toString();
    }

    public void run()
    {
      try
      {
        InputStream entrada_1 = cliente_1.getInputStream();
        StringBuilder headers = new StringBuilder();

        String linea = readLine(entrada_1);
        if (linea == null) return;

        String primera = linea;
        System.out.println(primera);

        String[] v = primera.split(" ");
        if (v.length < 3) return;

        String metodo = v[0];
        String url = v[1];
        String version = v[2];

        String pathOnly = url.split("\\?")[0];
        String query = (url.contains("?") ? url.substring(url.indexOf("?")) : "");

        int contentLength = 0;

        while ((linea = readLine(entrada_1)) != null)
        {
          String low = linea.toLowerCase();
          if (low.startsWith("content-length:"))
            contentLength = Integer.parseInt(linea.split(":")[1].trim());

          if (linea.equals("")) break;
          headers.append(linea).append("\r\n");
        }

        // Buscar ruta
        String host = null;
        int puerto = 0;
        String pathSalida = null;

        for (int i = 0; i < rutas.length; i++)
        {
          if (pathOnly.equals(rutas[i][0]) || (rutas[i][0].equals("/") && pathOnly.equals("/")))
          {
            host = rutas[i][1];
            puerto = Integer.parseInt(rutas[i][2]);
            pathSalida = rutas[i][3];
            break;
          }
        }

        if (host == null) return;

        // Reescribir URL
        String urlSalida;
        if (pathSalida.contains("?")) urlSalida = pathSalida;
        else urlSalida = pathSalida + query;

        String primeraSalida = metodo + " " + urlSalida + " " + version + "\r\n";

        StringBuilder peticion = new StringBuilder();
        peticion.append(primeraSalida);
        peticion.append(headers.toString());
        peticion.append("\r\n");

        // Conectar backend
        cliente_2 = new Socket(host, puerto);

        // Thread que regresa la respuesta al cliente
        new Worker_2(cliente_1, cliente_2).start();

        OutputStream salida_2 = cliente_2.getOutputStream();
        salida_2.write(peticion.toString().getBytes("ASCII"));
        salida_2.flush();

        while (contentLength > 0)
        {
          byte[] buffer = new byte[4096];
          int n = cliente_1.getInputStream().read(buffer);
          if (n <= 0) break;
          salida_2.write(buffer, 0, n);
          salida_2.flush();
          contentLength -= n;
        }

        synchronized (obj) { obj.wait(); }
      }
      catch (Exception e)
      {
        // ignore
      }
      finally
      {
        try { cliente_1.close(); } catch (Exception ignored) {}
      }
    }
  }

  static class Worker_2 extends Thread
  {
    Socket cliente_1, cliente_2;
    Worker_2(Socket c1, Socket c2) { cliente_1 = c1; cliente_2 = c2; }

    public void run()
    {
      try
      {
        cliente_2.setSoTimeout(TIMEOUT_READ);
        InputStream entrada_2 = cliente_2.getInputStream();
        OutputStream salida_1 = cliente_1.getOutputStream();

        byte[] buffer = new byte[4096];
        int n;
        while ((n = entrada_2.read(buffer)) != -1)
        {
          salida_1.write(buffer, 0, n);
          salida_1.flush();
        }
      }
      catch (IOException e)
      {
        // ignore
      }
      finally
      {
        try
        {
          cliente_2.close();
          synchronized (obj) { obj.notify(); }
        }
        catch (IOException ignored) {}
      }
    }
  }

  public static void main(String[] args) throws Exception
  {
    String keystore = System.getenv("keystore");
    String password = System.getenv("password");

    if (keystore == null || password == null)
    {
      System.err.println("No se definieron las variables de entorno keystore y password");
      System.exit(1);
    }

    System.setProperty("javax.net.ssl.keyStore", keystore);
    System.setProperty("javax.net.ssl.keyStorePassword", password);

    SSLServerSocketFactory sf = (SSLServerSocketFactory)SSLServerSocketFactory.getDefault();

    // LOCAL: 8443 (no 443)
    ServerSocket ss = sf.createServerSocket(8443);

    System.out.println("API Gateway escuchando en https://localhost:8443");

    for (;;)
    {
      Socket cliente_1 = ss.accept();
      new Worker_1(cliente_1).start();
    }
  }
}
