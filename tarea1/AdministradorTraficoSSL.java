/*
  AdministradorTraficoSSL.java
  Proxy inverso sencillo con SSL (flujo en paralelo)
  Parámetros:
    1) puerto_proxy_ssl
    2) ip_srv1
    3) puerto_srv1
    4) ip_srv2
    5) puerto_srv2

  Requiere:
    - keystore_servidor.jks en el directorio actual
    - password: changeit (ajustar si cambiaste)
*/

import java.io.*;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.ArrayList;
import java.util.List;
import javax.net.ssl.KeyManagerFactory;
import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLServerSocket;
import javax.net.ssl.SSLSocket;

public class AdministradorTraficoSSL {

  static class Worker extends Thread {
    private final SSLSocket navegador;
    private final String ip1, ip2;
    private final int port1, port2;

    Worker(SSLSocket navegador, String ip1, int port1, String ip2, int port2) {
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
        String requestLine = inBrowser.readLine();
        if (requestLine == null || requestLine.isEmpty()) return;

        if (!requestLine.startsWith("GET ")) {
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
        if (!hasConnectionHeader) headers.add("Connection: close");

        StringBuilder rawReq = new StringBuilder();
        rawReq.append(requestLine).append("\r\n");
        for (String h : headers) rawReq.append(h).append("\r\n");
        rawReq.append("\r\n");
        byte[] rawReqBytes = rawReq.toString().getBytes(StandardCharsets.UTF_8);

        Socket s1 = new Socket(ip1, port1);
        Socket s2 = new Socket(ip2, port2);

        OutputStream out1 = s1.getOutputStream();
        OutputStream out2 = s2.getOutputStream();
        out1.write(rawReqBytes); out1.flush();
        out2.write(rawReqBytes); out2.flush();

        Thread tSrv2 = new Thread(() -> {
          try (InputStream in2 = s2.getInputStream()) {
            byte[] buf = new byte[8192];
            while (true) {
              int n = in2.read(buf);
              if (n == -1) break;
            }
          } catch (Exception ignored) {}
          try { s2.close(); } catch (Exception ignored) {}
        });
        tSrv2.start();

        try (InputStream in1 = s1.getInputStream()) {
          byte[] buf = new byte[8192];
          int n;
          while ((n = in1.read(buf)) != -1) {
            outBrowser.write(buf, 0, n);
          }
          outBrowser.flush();
        }

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
      System.out.println("  java AdministradorTraficoSSL <puerto_proxy_ssl> <ip_srv1> <puerto_srv1> <ip_srv2> <puerto_srv2>");
      return;
    }

    int puertoProxy = Integer.parseInt(args[0]);
    String ip1 = args[1];  int port1 = Integer.parseInt(args[2]);
    String ip2 = args[3];  int port2 = Integer.parseInt(args[4]);

    // Configurar SSL
    char[] pass = "changeit".toCharArray();
    KeyStore ks = KeyStore.getInstance("JKS");
    try (FileInputStream fis = new FileInputStream("keystore_servidor.jks")) {
      ks.load(fis, pass);
    }
    KeyManagerFactory kmf = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm());
    kmf.init(ks, pass);

    SSLContext ctx = SSLContext.getInstance("TLS");
    ctx.init(kmf.getKeyManagers(), null, null);

    SSLServerSocket server = (SSLServerSocket) ctx.getServerSocketFactory().createServerSocket(puertoProxy);
    System.out.printf("Proxy SSL escuchando en puerto %d ...%n", puertoProxy);
    System.out.printf("Backend-1: %s:%d | Backend-2: %s:%d%n", ip1, port1, ip2, port2);

    for (;;) {
      SSLSocket nav = (SSLSocket) server.accept();
      new Worker(nav, ip1, port1, ip2, port2).start();
    }
  }
}
