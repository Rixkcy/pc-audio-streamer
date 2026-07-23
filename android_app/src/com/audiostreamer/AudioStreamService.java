package com.audiostreamer;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.media.AudioAttributes;
import android.media.AudioFocusRequest;
import android.media.AudioFormat;
import android.media.AudioManager;
import android.media.AudioTrack;
import android.os.Build;
import android.os.IBinder;
import android.util.Log;

import java.io.BufferedReader;
import java.io.FileReader;
import java.util.ArrayList;
import java.util.List;

public class AudioStreamService extends Service implements HttpWebSocketClient.AudioDataListener {
    private static final String CHANNEL_ID = "AudioStreamChannel";
    private AudioTrack audioTrack;
    private HttpWebSocketClient wsClient;
    private AudioManager audioManager;
    private AudioFocusRequest focusRequest;
    private static String lastStatus = "Initializing...";
    private int currentSampleRate = 48000;
    private long totalBytesWritten = 0;
    private long lastLogTime = 0;

    public static String getLastStatus() {
        return lastStatus;
    }

    @Override
    public void onCreate() {
        super.onCreate();
        createNotificationChannel();
        Notification notification = createNotification("PC Audio Stream Active");
        startForeground(1001, notification);

        audioManager = (AudioManager) getSystemService(Context.AUDIO_SERVICE);
        requestAudioFocus();
        initAudioTrack(48000);
        startStreaming();
    }

    private void requestAudioFocus() {
        if (audioManager == null) return;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            AudioAttributes attributes = new AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_GAME)
                    .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                    .setFlags(AudioAttributes.FLAG_LOW_LATENCY)
                    .build();
            focusRequest = new AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN)
                    .setAudioAttributes(attributes)
                    .setAcceptsDelayedFocusGain(true)
                    .setOnAudioFocusChangeListener(focus -> {})
                    .build();
            audioManager.requestAudioFocus(focusRequest);
        } else {
            audioManager.requestAudioFocus(null, AudioManager.STREAM_MUSIC, AudioManager.AUDIOFOCUS_GAIN);
        }
    }

    public synchronized void initAudioTrack(int sampleRate) {
        if (audioTrack != null && currentSampleRate == sampleRate) return;

        if (audioTrack != null) {
            try {
                audioTrack.stop();
                audioTrack.release();
            } catch (Exception ignored) {}
        }

        currentSampleRate = sampleRate;
        int minBufferSize = AudioTrack.getMinBufferSize(
                sampleRate,
                AudioFormat.CHANNEL_OUT_STEREO,
                AudioFormat.ENCODING_PCM_16BIT
        );

        AudioAttributes attributes = new AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_GAME)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                .setFlags(AudioAttributes.FLAG_LOW_LATENCY)
                .build();

        AudioFormat format = new AudioFormat.Builder()
                .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                .setSampleRate(sampleRate)
                .setChannelMask(AudioFormat.CHANNEL_OUT_STEREO)
                .build();

        audioTrack = new AudioTrack.Builder()
                .setAudioAttributes(attributes)
                .setAudioFormat(format)
                .setBufferSizeInBytes(minBufferSize)
                .setPerformanceMode(AudioTrack.PERFORMANCE_MODE_LOW_LATENCY)
                .setTransferMode(AudioTrack.MODE_STREAM)
                .build();

        // 60ms Buffer Cap (2880 stereo frames = 60ms) -> Perfect 100% Crystal Clear Cushion!
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            int targetFrames = sampleRate * 60 / 1000;
            audioTrack.setBufferSizeInFrames(targetFrames);
        }

        totalBytesWritten = 0;
        audioTrack.play();
    }

    private String getUsbGatewayIp() {
        try {
            BufferedReader br = new BufferedReader(new FileReader("/proc/net/route"));
            String line;
            while ((line = br.readLine()) != null) {
                String[] tokens = line.split("\\s+");
                if (tokens.length >= 3 && tokens[1].equals("00000000")) {
                    String hexIp = tokens[2];
                    if (hexIp.length() == 8) {
                        long ipLong = Long.parseLong(hexIp, 16);
                        return String.format("%d.%d.%d.%d",
                                (ipLong & 0xff),
                                ((ipLong >> 8) & 0xff),
                                ((ipLong >> 16) & 0xff),
                                ((ipLong >> 24) & 0xff));
                    }
                }
            }
        } catch (Exception ignored) {}
        return "10.39.227.158";
    }

    private void startStreaming() {
        List<String> hosts = new ArrayList<>();
        String gateway = getUsbGatewayIp();
        if (gateway != null && !gateway.isEmpty()) {
            hosts.add(gateway);
        }
        hosts.add("10.39.227.158");
        hosts.add("127.0.0.1");

        wsClient = new HttpWebSocketClient(hosts.toArray(new String[0]), 8080, this);
        wsClient.start();
    }

    public void reconnect() {
        requestAudioFocus();
        if (wsClient != null) {
            wsClient.stop();
        }
        if (audioTrack != null) {
            try {
                audioTrack.pause();
                audioTrack.flush();
                totalBytesWritten = 0;
                audioTrack.play();
            } catch (Exception ignored) {}
        }
        startStreaming();
    }

    @Override
    public synchronized void OnAudioData(byte[] data, int length) {
        int alignedLength = (length / 4) * 4;
        if (audioTrack != null && audioTrack.getPlayState() == AudioTrack.PLAYSTATE_PLAYING && alignedLength > 0) {
            long now = System.currentTimeMillis();
            long head = (audioTrack.getPlaybackHeadPosition() & 0xFFFFFFFFL) * 4;
            long pendingBytes = totalBytesWritten - head;
            long pendingMs = pendingBytes * 1000 / (48000 * 4);

            if (now - lastLogTime > 1000) {
                lastLogTime = now;
                Log.d("AudioDiag", "BytesWritten: " + totalBytesWritten + " | BytesPlayed: " + head + " | PendingMs: " + pendingMs + "ms | RecvLen: " + length);
            }

            int written = audioTrack.write(data, 0, alignedLength);
            if (written > 0) {
                totalBytesWritten += written;
            }
        }
    }

    @Override
    public void OnStatus(String status) {
        lastStatus = status;
        updateNotification(status);
    }

    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID,
                    "PC Audio Stream Channel",
                    NotificationManager.IMPORTANCE_LOW
            );
            NotificationManager manager = getSystemService(NotificationManager.class);
            if (manager != null) {
                manager.createNotificationChannel(channel);
            }
        }
    }

    private Notification createNotification(String text) {
        Notification.Builder builder;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            builder = new Notification.Builder(this, CHANNEL_ID);
        } else {
            builder = new Notification.Builder(this);
        }

        return builder.setContentTitle("PC Audio Receiver")
                .setContentText(text)
                .setSmallIcon(android.R.drawable.ic_btn_speak_now)
                .setOngoing(true)
                .build();
    }

    private void updateNotification(String text) {
        NotificationManager manager = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (manager != null) {
            manager.notify(1001, createNotification(text));
        }
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null && "ACTION_RECONNECT".equals(intent.getAction())) {
            reconnect();
        }
        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        if (wsClient != null) wsClient.stop();
        if (audioTrack != null) {
            audioTrack.stop();
            audioTrack.release();
        }
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }
}
