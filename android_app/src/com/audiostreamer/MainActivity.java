package com.audiostreamer;

import android.app.Activity;
import android.content.Intent;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

public class MainActivity extends Activity implements View.OnClickListener, Runnable {
    private TextView statusTextView;
    private Button startBtn;
    private Handler handler = new Handler();

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(64, 64, 64, 64);
        layout.setBackgroundColor(0xFF0F172A);

        TextView title = new TextView(this);
        title.setText("🎧 PC Audio Receiver");
        title.setTextSize(24);
        title.setTextColor(0xFF38BDF8);
        title.setPadding(0, 0, 0, 32);

        statusTextView = new TextView(this);
        statusTextView.setText("Status: " + AudioStreamService.getLastStatus());
        statusTextView.setTextSize(16);
        statusTextView.setTextColor(0xFF94A3B8);
        statusTextView.setPadding(0, 0, 0, 64);

        startBtn = new Button(this);
        startBtn.setText("⚡ Reconnect / Stream Now");
        startBtn.setBackgroundColor(0xFF0284C7);
        startBtn.setTextColor(0xFFFFFFFF);
        startBtn.setOnClickListener(this);

        layout.addView(title);
        layout.addView(statusTextView);
        layout.addView(startBtn);

        setContentView(layout);

        startAudioService();
        handler.postDelayed(this, 500);
    }

    @Override
    public void onClick(View v) {
        startBtn.setText("Connecting...");
        Intent intent = new Intent(this, AudioStreamService.class);
        intent.setAction("ACTION_RECONNECT");
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startForegroundService(intent);
        } else {
            startService(intent);
        }
        handler.postDelayed(() -> startBtn.setText("⚡ Reconnect / Stream Now"), 1000);
    }

    @Override
    public void run() {
        if (statusTextView != null) {
            statusTextView.setText("Status: " + AudioStreamService.getLastStatus());
        }
        handler.postDelayed(this, 500);
    }

    private void startAudioService() {
        Intent intent = new Intent(this, AudioStreamService.class);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startForegroundService(intent);
        } else {
            startService(intent);
        }
    }
}
