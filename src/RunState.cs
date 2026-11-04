using System;
using System.IO;
using System.Text;

namespace BeamPrank
{
    /// <summary>Per-user state: launch counter, crash log and minidump left next to the exe.</summary>
    static class RunState
    {
        /// <summary>Number of this launch (1 = first ever). Set once from Program.Main.</summary>
        public static int CurrentRun = 1;

        static readonly Random Rng = new Random();

        public static string DataDir
        {
            get
            {
                string d = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BeamNG.drive");
                try { Directory.CreateDirectory(d); }
                catch { }
                return d;
            }
        }

        static string CounterPath { get { return Path.Combine(DataDir, "launches.dat"); } }

        public static int Runs
        {
            get
            {
                try
                {
                    if (File.Exists(CounterPath))
                    {
                        int n;
                        if (int.TryParse(File.ReadAllText(CounterPath).Trim(), out n))
                            return n;
                    }
                }
                catch { }
                return 0;
            }
        }

        public static int NextRun()
        {
            int n = Runs + 1;
            try { File.WriteAllText(CounterPath, n.ToString()); }
            catch { }
            return n;
        }

        /// <summary>Windows 10/11 dialogs are flat; 7/8 still use the classic gradient chrome.</summary>
        public static bool IsModernWindows
        {
            get
            {
                try { return Environment.OSVersion.Version.Major >= 10; }
                catch { return true; }
            }
        }

        /// <summary>
        /// Varies the crash on repeat launches. The first run is always the classic
        /// "has stopped working" so a recording gets the recognisable dialog.
        /// </summary>
        public static CrashKind PickCrash(int run)
        {
            if (run <= 1) return CrashKind.StoppedWorking;
            if (run == 2) return CrashKind.NotResponding;

            CrashKind[] pool = { CrashKind.StoppedWorking, CrashKind.NotResponding, CrashKind.DisplayDriver };
            return pool[Rng.Next(pool.Length)];
        }

        /// <summary>On later launches sometimes the exe "cannot start" at all.</summary>
        public static bool ShouldFailStartup(int run)
        {
            return run >= 3 && Rng.NextDouble() < 0.28;
        }

        public static string ReasonFor(CrashKind kind)
        {
            switch (kind)
            {
                case CrashKind.NotResponding:
                    return "Application hang: main thread blocked for 30.0 s";
                case CrashKind.DisplayDriver:
                    return "DX11: device removed (0x887A0005)";
                case CrashKind.StartupError:
                    return "STATUS_INVALID_IMAGE_FORMAT (0xc000007b)";
                default:
                    return "Access violation reading location 0x00000000 (0xc0000005)";
            }
        }

        /// <summary>Folder that gets the log and the dump: the game folder when writable.</summary>
        static string OutputDir()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string probe = Path.Combine(exeDir, ".w");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return exeDir;
            }
            catch
            {
                return DataDir;
            }
        }

        /// <summary>Leaves a convincing beamng.log / beamng_dump.dmp / crash-info.txt behind.</summary>
        public static string WriteCrashArtifacts(string reason)
        {
            string dir = OutputDir();
            try { File.WriteAllText(Path.Combine(dir, "beamng.log"), BuildLog(reason), Encoding.UTF8); }
            catch { }

            try { File.WriteAllBytes(Path.Combine(dir, "beamng_dump.dmp"), BuildDump()); }
            catch { }

            try
            {
                File.WriteAllText(Path.Combine(dir, "crash-info.txt"),
                    "BeamNG.drive 0.36.4.0\r\n" +
                    "Crash: " + reason + "\r\n" +
                    "Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                    "OS: " + Environment.OSVersion.VersionString + "\r\n" +
                    "CPU: " + Environment.ProcessorCount + " logical cores\r\n" +
                    "Dump: beamng_dump.dmp\r\n");
            }
            catch { }

            return dir;
        }

        static string BuildLog(string reason)
        {
            DateTime t = DateTime.Now;
            StringBuilder b = new StringBuilder();
            b.AppendLine("BeamNG.drive 0.36.4.0 (build 18294) steam");
            b.AppendLine("Log started: " + t.ToString("yyyy-MM-dd HH:mm:ss"));
            b.AppendLine("OS: " + Environment.OSVersion.VersionString);
            b.AppendLine("CPU: " + Environment.ProcessorCount + " threads");
            b.AppendLine("Renderer: Direct3D 11 (feature level 11_0)");
            b.AppendLine();

            string[] lines =
            {
                "INFO  Torque3D: engine initialized",
                "INFO  Render: adapter \"" + AdapterName() + "\"",
                "INFO  Render: 1920x1080 fullscreen, vsync on",
                "INFO  PhysX: scene created, 1 substep",
                "INFO  Mount: content/jungle_rock_island.zip",
                "INFO  Mount: content/vehicles/cherrier.zip",
                "INFO  Prefab: loaded 271 objects",
                "INFO  Roads: loaded 56.7 km of roads, 75.4 km markings",
                "INFO  Materials: 1284 loaded, 0 failed",
                "INFO  Terrain: building collision mesh",
                "INFO  Vehicle: spawning \"Hiroshi Sunburst\"",
                "INFO  Vehicle: 412 nodes, 388 beams",
                "ERROR DX11: device removed (0x887A0005)",
                "ERROR Render: lost device, attempting reset",
                "ERROR Render: reset failed (0x887A0006)",
                "FATAL " + reason,
                "FATAL stack: RenderFrame -> Present -> D3D11DeviceContext::Present",
                "INFO  Crash handler: writing minidump",
                "INFO  Crash handler: beamng_dump.dmp written"
            };

            for (int i = 0; i < lines.Length; i++)
                b.AppendLine(t.AddSeconds(i * 0.37).ToString("HH:mm:ss.fff") + "  " + lines[i]);

            return b.ToString();
        }

        static string AdapterName()
        {
            try
            {
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                    return "Display adapter";
            }
            catch
            {
                return "Default adapter";
            }
        }

        static byte[] BuildDump()
        {
            // a small but plausible minidump: 'MDMP' header, then mostly zero pages
            const int size = 196608;
            byte[] d = new byte[size];
            d[0] = (byte)'M'; d[1] = (byte)'D'; d[2] = (byte)'M'; d[3] = (byte)'P';
            byte[] ver = { 0xA7, 0x93, 0x00, 0x00 };
            Array.Copy(ver, 0, d, 4, 4);

            Random r = new Random(0x36A4);
            for (int i = 32; i < size; i += 4096)
            {
                d[i] = (byte)r.Next(256);
                for (int j = 0; j < 24; j++)
                    d[i + 8 + j] = (byte)r.Next(256);
            }
            return d;
        }
    }
}
