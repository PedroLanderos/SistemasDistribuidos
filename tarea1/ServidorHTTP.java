/*
  ServidorHTTP.java
  Carlos Pineda G. 2025
  + Cache Last-Modified / If-Modified-Since
  + Servido de archivos .html
  + Endpoint /suma
  + Puerto por argumento (default 8080)
*/

import java.io.BufferedReader;
import java.io.File;
import java.io.FileInputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.io.PrintWriter;
import java.net.ServerSocket;
import java.net.Socket;
import java.net.URLDecoder;
import java.text.ParseException;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.TimeZone;

class ServidorHTTP {

  // ==== Utilidades de fecha HTTP (RFC 1123) ====
  private static final ThreadLocal<SimpleDateFormat> HTTP_DATE_FMT =
    ThreadLocal.withInitial(() -> {
      SimpleDateFormat f = new SimpleDateFormat("EEE, dd MMM yyyy HH:mm:ss 'GMT'", Locale.US);
      f.setTimeZone(TimeZone.getTimeZone("GMT"));
      return f;
    });

  static String formatHttpDate(long millis) {
    return HTTP_DATE_FMT.get().format(new Date(millis));
  }

  static Long parseHttpDate(String s) {
    if (s == null) return null;
    try {
      return HTTP_DATE_FMT.get().parse(s.trim()).getTime();
    } catch (ParseException e) {
      return null;
    }
  }

  static class Worker extends Thread {
    Socket conexion;
    Worker(Socket conexion) { this.conexion = conexion; }

    // Helper para /suma
    int valor(String parametros, String variable) throws Exception {
      String[] p = parametros.split("&");
      for (int i = 0; i < p.length; i++) {
        String[] s = p[i].split("=");
        if (s[0].equals(variable))
          return Integer.parseInt(s[1]);
      }
      throw new Exception("Se espera la variable: " + variable);
    }

    public void run() {
      try {
        BufferedReader entrada = new BufferedReader(new InputStreamReader(conexion.getInputStream(), "UTF-8"));
        OutputStream crudo = conexion.getOutputStream();
        PrintWriter salida = new PrintWriter(crudo);

        String req = entrada.readLine();
        if (req == null) return;
        System.out.println("Petición: " + req);

        // --- leer encabezados y capturar If-Modified-Since ---
        String ifModifiedSince = null;
        for (;;) {
          String encabezado = entrada.readLine();
          if (encabezado == null) return;
          if (encabezado.equals("")) break;
          // System.out.println("Encabezado: " + encabezado);
          int idx = encabezado.indexOf(':');
          if (idx > 0) {
            String nombre = encabezado.substring(0, idx).trim();
            String valor = encabezado.substring(idx + 1).trim();
            if ("If-Modified-Since".equalsIgnoreCase(nombre)) {
              ifModifiedSince = valor;
            }
          }
        }

        // --- Router simple ---
        if (req.startsWith("GET /suma?")) {
          // /suma?a=1&b=2&c=3
          String parametros = req.split(" ")[1].split("\\?")[1];
          String respuesta = String.valueOf(
              valor(parametros,"a") + valor(parametros,"b") + valor(parametros,"c")
          );
          byte[] body = respuesta.getBytes("UTF-8");

          String now = formatHttpDate(System.currentTimeMillis());
          salida.println("HTTP/1.1 200 OK");
          salida.println("Date: " + now);
          // Recurso dinámico: usamos la hora actual como Last-Modified
          salida.println("Last-Modified: " + now);
          salida.println("Access-Control-Allow-Origin: *");
          salida.println("Content-Type: text/plain; charset=utf-8");
          salida.println("Content-Length: " + body.length);
          salida.println("Connection: close");
          salida.println();
          salida.flush();
          crudo.write(body);
          crudo.flush();
        }
        else if (req.startsWith("GET /") && req.contains(" HTTP/")) {
          // --- Servido de archivos .html ---
          String ruta = req.split(" ")[1]; // ej: /index.html o /archivo.html
          ruta = URLDecoder.decode(ruta, "UTF-8");
          int q = ruta.indexOf('?');       // quitar querystring si lo hay
          if (q >= 0) ruta = ruta.substring(0, q);
          if (ruta.equals("/")) ruta = "/index.html"; // default opcional
          if (ruta.contains("..")) {
            enviar404(salida);
            return;
          }

          File archivo = new File("." + ruta);
          if (!archivo.exists() || !archivo.isFile()) {
            enviar404(salida);
            return;
          }

          long lastMod = archivo.lastModified();
          String lastModifiedHeader = formatHttpDate(lastMod);

          // If-Modified-Since -> 304 si no cambió
          boolean notModified = false;
          Long imsMillis = parseHttpDate(ifModifiedSince);
          if (imsMillis != null) {
            long lastModSecs = (lastMod / 1000L) * 1000L;
            long imsSecs     = (imsMillis / 1000L) * 1000L;
            if (lastModSecs <= imsSecs) {
              notModified = true;
            }
          }

          if (notModified) {
            salida.println("HTTP/1.1 304 Not Modified");
            salida.println("Date: " + formatHttpDate(System.currentTimeMillis()));
            salida.println("Last-Modified: " + lastModifiedHeader);
            salida.println("Connection: close");
            salida.println();
            salida.flush();
            return;
          }

          // Leer archivo y responder 200
          byte[] body = leerArchivo(archivo);

          salida.println("HTTP/1.1 200 OK");
          salida.println("Date: " + formatHttpDate(System.currentTimeMillis()));
          salida.println("Last-Modified: " + lastModifiedHeader);
          salida.println("Content-Type: text/html; charset=utf-8");
          salida.println("Content-Length: " + body.length);
          salida.println("Connection: close");
          salida.println();
          salida.flush();

          crudo.write(body);
          crudo.flush();
        }
        else {
          enviar404(salida);
        }

      } catch (Exception e) {
        System.err.println("Error en la conexión: " + e.getMessage());
      } finally {
        try { conexion.close(); }
        catch (Exception e) { System.err.println("Error en close: " + e.getMessage()); }
      }
    }

    private static void enviar404(PrintWriter salida) {
      String cuerpo = "<html><body><h1>404 File Not Found</h1></body></html>";
      byte[] body = cuerpo.getBytes();
      String now = formatHttpDate(System.currentTimeMillis());

      salida.println("HTTP/1.1 404 Not Found");
      salida.println("Date: " + now);
      salida.println("Last-Modified: " + now);
      salida.println("Content-Type: text/html; charset=utf-8");
      salida.println("Content-Length: " + body.length);
      salida.println("Connection: close");
      salida.println();
      salida.println(cuerpo);
      salida.flush();
    }

    private static byte[] leerArchivo(File f) throws Exception {
      long len = f.length();
      if (len > Integer.MAX_VALUE) throw new RuntimeException("Archivo demasiado grande");
      byte[] data = new byte[(int)len];
      try (FileInputStream in = new FileInputStream(f)) {
        int off = 0, r;
        while (off < data.length && (r = in.read(data, off, data.length - off)) != -1) {
          off += r;
        }
        if (off < data.length) throw new RuntimeException("No se pudo leer completo");
      }
      return data;
    }
  }

  public static void main(String[] args) throws Exception {
    int puerto = 8080; // default
    if (args.length >= 1) {
      try { puerto = Integer.parseInt(args[0]); }
      catch (Exception ignored) {}
    }

    ServerSocket servidor = new ServerSocket(puerto);
    System.out.println("Servidor escuchando en puerto " + puerto + "...");
    for (;;) {
      Socket conexion = servidor.accept();
      new Worker(conexion).start();
    }
  }
}
