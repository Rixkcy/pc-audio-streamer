using System;
using System.Diagnostics;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WasapiLatencyTest
{
    class Program
    {
        static void Main()
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                Console.WriteLine($"Default Device: {dev.FriendlyName}");
                Console.WriteLine($"Initial Mute State: {dev.AudioEndpointVolume.Mute}");

                var capture = new WasapiLoopbackCapture(dev);
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
                        Console.WriteLine($"[Callback #{count}] Delta: {delta}ms | Bytes: {e.BytesRecorded} | Muted: {dev.AudioEndpointVolume.Mute}");
                    }
                };

                capture.StartRecording();
                Console.WriteLine("\n--- Recording for 3 seconds UNMUTED ---");
                Thread.Sleep(3000);

                Console.WriteLine("\n--- Muting Device now ---");
                dev.AudioEndpointVolume.Mute = true;
                count = 0;
                lastMs = sw.ElapsedMilliseconds;
                Thread.Sleep(3000);

                Console.WriteLine("\n--- Unmuting Device now ---");
                dev.AudioEndpointVolume.Mute = false;
                capture.StopRecording();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex}");
            }
        }
    }
}
