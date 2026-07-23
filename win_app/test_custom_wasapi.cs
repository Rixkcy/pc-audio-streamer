using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CustomWasapiTest
{
    // Custom WASAPI Loopback Capture with 10ms Low-Latency Buffer Duration
    public class LowLatencyWasapiLoopbackCapture : WasapiCapture
    {
        public LowLatencyWasapiLoopbackCapture(MMDevice captureDevice)
            : base(captureDevice, true, 10) // 10ms buffer duration!
        {
        }

        protected override AudioClientStreamFlags GetAudioClientStreamFlags()
        {
            return AudioClientStreamFlags.Loopback;
        }
    }

    class Program
    {
        static void Main()
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                Console.WriteLine($"Testing 10ms WASAPI Loopback on: {dev.FriendlyName}");

                var capture = new LowLatencyWasapiLoopbackCapture(dev);

                Stopwatch sw = Stopwatch.StartNew();
                long lastMs = 0;
                int count = 0;

                capture.DataAvailable += (s, e) =>
                {
                    count++;
                    long currentMs = sw.ElapsedMilliseconds;
                    long delta = currentMs - lastMs;
                    lastMs = currentMs;

                    if (count <= 30)
                    {
                        Console.WriteLine($"[10ms WASAPI Callback #{count}] Delta: {delta}ms | Bytes: {e.BytesRecorded}");
                    }
                };

                capture.StartRecording();
                Thread.Sleep(3000);
                capture.StopRecording();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex}");
            }
        }
    }
}
