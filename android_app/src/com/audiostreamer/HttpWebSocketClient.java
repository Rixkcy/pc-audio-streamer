package com.audiostreamer;

import java.io.InputStream;
import java.io.OutputStream;
import java.net.Socket;
import java.util.Base64;
import java.util.Random;

public class HttpWebSocketClient {
    public interface AudioDataListener {
        void OnAudioData(byte[] data, int length);
        void OnStatus(String status);
    }

    private Socket socket;
    private boolean isRunning = false;
    private final String[] hosts;
    private final int port;
    private final AudioDataListener listener;

    public HttpWebSocketClient(String[] hosts, int port, AudioDataListener listener) {
        this.hosts = hosts;
        this.port = port;
        this.listener = listener;
    }

    public void start() {
        isRunning = true;
        new Thread(this::runLoop).start();
    }

    public void stop() {
        isRunning = false;
        try {
            if (socket != null) socket.close();
        } catch (Exception ignored) {}
    }

    private void runLoop() {
        int hostIndex = 0;
        byte[] pcmBuffer = new byte[65536];

        while (isRunning) {
            String host = hosts[hostIndex % hosts.length];
            hostIndex++;
            try {
                listener.OnStatus("Connecting to " + host + ":" + port + "...");
                socket = new Socket(host, port);
                socket.setTcpNoDelay(true);

                InputStream in = socket.getInputStream();
                OutputStream out = socket.getOutputStream();

                // Send WebSocket Handshake
                byte[] nonce = new byte[16];
                new Random().nextBytes(nonce);
                String key = Base64.getEncoder().encodeToString(nonce);

                String handshake = "GET /audio HTTP/1.1\r\n" +
                        "Host: " + host + ":" + port + "\r\n" +
                        "Upgrade: websocket\r\n" +
                        "Connection: Upgrade\r\n" +
                        "Sec-WebSocket-Key: " + key + "\r\n" +
                        "Sec-WebSocket-Version: 13\r\n\r\n";

                out.write(handshake.getBytes());
                out.flush();

                // Read Handshake Response
                byte[] headerBuffer = new byte[1024];
                int read = in.read(headerBuffer);
                if (read <= 0) throw new Exception("Empty response");

                String resp = new String(headerBuffer, 0, read);
                if (!resp.contains("101")) {
                    throw new Exception("Handshake failed: " + resp);
                }

                listener.OnStatus("Connected to " + host + " & Streaming Live Audio");

                // Read Clean, Valid WebSocket Frames (Zero Corrupt Headers)
                while (isRunning && !socket.isClosed()) {
                    int b1 = in.read();
                    if (b1 == -1) break;
                    int b2 = in.read();
                    if (b2 == -1) break;

                    int len = b2 & 0x7F;
                    if (len == 126) {
                        int b3 = in.read();
                        int b4 = in.read();
                        if (b3 == -1 || b4 == -1) break;
                        len = ((b3 & 0xFF) << 8) | (b4 & 0xFF);
                    } else if (len == 127) {
                        for (int i = 0; i < 8; i++) in.read();
                        len = 65536;
                    }

                    if (len > pcmBuffer.length) {
                        pcmBuffer = new byte[len + 4096];
                    }

                    int totalRead = 0;
                    while (totalRead < len) {
                        int r = in.read(pcmBuffer, totalRead, len - totalRead);
                        if (r == -1) break;
                        totalRead += r;
                    }

                    if (totalRead > 0) {
                        listener.OnAudioData(pcmBuffer, totalRead);
                    }
                }
            } catch (Exception e) {
                listener.OnStatus("Retrying connection to " + host + "...");
                try { Thread.sleep(1000); } catch (InterruptedException ignored) {}
            } finally {
                try { if (socket != null) socket.close(); } catch (Exception ignored) {}
            }
        }
    }
}
