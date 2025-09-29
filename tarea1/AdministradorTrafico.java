/*
  AdministradorTrafico.java
  Proxy inverso sencillo (flujo en paralelo)
  Parámetros:
    1) puerto_proxy
    2) ip_srv1
    3) puerto_srv1
    4) ip_srv2
    5) puerto_srv2

  Comportamiento:
    - Acepta conexión del navegador.
    - Lee request (línea + headers, sin body).
    - Reenvía la misma petición a Servidor-1 y Servidor-2.
    - Regresa al navegador la respuesta de Servidor-1.
    - La respuesta de Servidor-2 se consume y descarta.

  Limitaciones intencionales (para la práctica):
    - Maneja GET sin body.
    - No reescribe Host.
    - Cierra conexiones por solicitud (Connection: close).
*/

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.io.PrintWriter;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

public class AdministradorTrafico {

  static class Worker extends Thread {
    private final Socket navegador;
    private final String ip1, ip2;
    private final int port1, port2;

    Worker(Socket navegador, String ip1, int port1, String ip2, int port2) {
      this.navegador = navegador;
      this.ip1 = ip1; this.port1 = port1;
      this.ip2 = ip2; this.port2 = port2;
    }

    @Override
    public void run() {
      try (
        BufferedReader inBrowser = new BufferedReader(new InputStreamReader(navegador.getInputStream(), StandardCharsets.UTF_8));
        OutputStream outBrowser = navegador.getOutputStream();
      ) {
        // 1) Leer request del navegador (línea + headers)
        String requestLine = inBrowser.readLine();
        if (requestLine == null || requestLine.isEmpty()) return;

        // Solo aceptamos GET … HTTP/1.1 (o 1.0)
        if (!requestLine.startsWith("GET ")) {
          // respuesta mínima 405
          String body = "<html><body><h1>405 Method Not Allowed</h1></body></html>";
          byte[] b = body.getBytes(StandardCharsets.UTF_8);
          PrintWriter pw = new PrintWriter(outBrowser, true, StandardCharsets.UTF_8);
          pw.println("HTTP/1.1 405 Method Not Allowed");
          pw.println("Content-Type: text/html; charset=utf-8");
          pw.println("Content-Length: " + b.length);
          pw.println("Connection: close");
          pw.println();
          pw.flush();
          outBrowser.write(b);
          outBrowser.flush();
          return;
        }

        List<String> headers = new ArrayList<>();
        boolean hasConnectionHeader = false;
        for (;;) {
          String h = inBrowser.readLine();
          if (h == null) return;
          if (h.isEmpty()) break;
          headers.add(h);
          if (h.toLowerCase().startsWith("connection:")) hasConnectionHeader = true;
        }
        // Forzar cierre explícito (simplifica el forwarding)
        if (!hasConnectionHeader) headers.add("Connection: close");

        // Construir request crudo a reenviar a los backends
        StringBuilder rawReq = new StringBuilder();
        rawReq.append(requestLine).append("\r\n");
        for (String h : headers) rawReq.append(h).append("\r\n");
        rawReq.append("\r\n");
        byte[] rawReqBytes = rawReq.toString().getBytes(StandardCharsets.UTF_8);

        // 2) Conexión a Servidor-1 y Servidor-2
        Socket s1 = new Socket(ip1, port1);
        Socket s2 = new Socket(ip2, port2);

        // 3) Enviar request a ambos
        OutputStream out1 = s1.getOutputStream();
        OutputStream out2 = s2.getOutputStream();
        out1.write(rawReqBytes); out1.flush();
        out2.write(rawReqBytes); out2.flush();

        // 4) Consumir respuesta de Srv2 en un hilo aparte (descartar)
        Thread tSrv2 = new Thread(() -> {
          try (InputStream in2 = s2.getInputStream()) {
            byte[] buf = new byte[8192];
            while (true) {
              int n = in2.read(buf);
              if (n == -1) break;
              // DESCARTAR bytes (no se envían al navegador)
            }
          } catch (Exception ignored) {}
          try { s2.close(); } catch (Exception ignored) {}
        });
        tSrv2.start();

        // 5) Pasar respuesta de Srv1 al navegador (streaming)
        try (InputStream in1 = s1.getInputStream()) {
          byte[] buf = new byte[8192];
          int n;
          while ((n = in1.read(buf)) != -1) {
            outBrowser.write(buf, 0, n);
          }
          outBrowser.flush();
        }

        // 6) Cerrar sockets
        try { s1.close(); } catch (Exception ignored) {}
        try { tSrv2.join(2000); } catch (Exception ignored) {}
      } catch (Exception e) {
        System.err.println("Error en Worker: " + e.getMessage());
      } finally {
        try { navegador.close(); } catch (Exception ignored) {}
      }
    }
  }

  public static void main(String[] args) throws Exception {
    if (args.length != 5) {
      System.out.println("Uso:");
      System.out.println("  java AdministradorTrafico <puerto_proxy> <ip_srv1> <puerto_srv1> <ip_srv2> <puerto_srv2>");
      System.out.println("Ejemplo local:");
      System.out.println("  java AdministradorTrafico 9090 127.0.0.1 8081 127.0.0.1 8082");
      return;
    }

    int puertoProxy = Integer.parseInt(args[0]);
    String ip1 = args[1];  int port1 = Integer.parseInt(args[2]);
    String ip2 = args[3];  int port2 = Integer.parseInt(args[4]);

    try (ServerSocket proxy = new ServerSocket(puertoProxy)) {
      System.out.printf("Proxy inverso escuchando en puerto %d ...%n", puertoProxy);
      System.out.printf("Backend-1: %s:%d | Backend-2: %s:%d%n", ip1, port1, ip2, port2);
      for (;;) {
        Socket nav = proxy.accept();
        new Worker(nav, ip1, port1, ip2, port2).start();
      }
    }
  }
}
