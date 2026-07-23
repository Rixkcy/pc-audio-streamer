using System;
using System.Threading;
using NAudio.CoreAudioApi;

namespace Diag
{
    class Program
    {
        static void Main()
        {
            var enumerator = new MMDeviceEnumerator();

            Console.WriteLine("=== DEFAULT AUDIO DEVICES ===");
            try { var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console); Console.WriteLine($"Default (Console):        {d.FriendlyName}"); } catch { Console.WriteLine("Default (Console): NONE"); }
            try { var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); Console.WriteLine($"Default (Multimedia):     {d.FriendlyName}"); } catch { Console.WriteLine("Default (Multimedia): NONE"); }
            try { var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications); Console.WriteLine($"Default (Communications): {d.FriendlyName}"); } catch { Console.WriteLine("Default (Communications): NONE"); }

            Console.WriteLine("\n=== ALL ACTIVE RENDER ENDPOINTS ===");
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var dev in devices)
            {
                float peak = 0;
                float vol = 0;
                bool muted = false;
                try { peak = dev.AudioMeterInformation.MasterPeakValue; } catch { }
                try { vol = dev.AudioEndpointVolume.MasterVolumeLevelScalar; } catch { }
                try { muted = dev.AudioEndpointVolume.Mute; } catch { }
                Console.WriteLine($"  Device: {dev.FriendlyName}");
                Console.WriteLine($"    State: {dev.State} | Volume: {vol:P0} | Muted: {muted} | Peak: {peak:F4}");
            }

            Console.WriteLine("\nWaiting 2s to sample peak levels while audio plays...");
            Thread.Sleep(2000);

            Console.WriteLine("\n=== PEAK LEVELS AFTER 2s ===");
            foreach (var dev in devices)
            {
                float peak = 0;
                try { peak = dev.AudioMeterInformation.MasterPeakValue; } catch { }
                Console.WriteLine($"  {dev.FriendlyName}: Peak = {peak:F4}");
            }
        }
    }
}
