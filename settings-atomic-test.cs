// Settings save regression harness: runs against the REAL compiled SettingsManager
// (astro-check.dll). Scenarios: S1 stale-instance save merge (deterministic),
// S2 gated 4-thread distinct-field saves (deterministic), S3 sustained writes with a
// concurrent raw reader (torn-read count is informational; deterministic checks only).
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using AstroHybrid.App.Models;
using AstroHybrid.App.Services;

class SettingsAtomicTest
{
    static int fails = 0;
    static void Check(bool cond, string name, string detail)
    {
        Console.WriteLine((cond ? "ok   " : "FAIL ") + name + " (" + detail + ")");
        if (!cond) fails++;
    }

    static string FreshDir(string root, string name)
    {
        var d = Path.Combine(root, name);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        return d;
    }

    static AppSettings ReadFileSettings(string root)
    {
        var path = Path.Combine(root, "Build", "astrohybrid-settings.json");
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
    }

    static int Main(string[] args)
    {
        var scenario = args.Length > 0 ? args[0] : "ALL";
        var root = Path.Combine(Path.GetDirectoryName(typeof(SettingsAtomicTest).Assembly.Location), "st-" + scenario);
        switch (scenario)
        {
            case "S1": // two instances; second saves from a stale cache -> first's change must survive
            {
                var dir = FreshDir(root, "s1");
                var a = new SettingsManager(dir);
                var b = new SettingsManager(dir);
                var sa = a.Load();
                var sb = b.Load();
                sa.Fps = 99;
                a.Save(sa);
                sb.Lighting = 7;   // b's baseline predates a's save
                b.Save(sb);
                var final = ReadFileSettings(dir);
                Check(final.Fps == 99, "S1a: instance A's saved Fps survives B's stale-cache save", "fps=" + final.Fps);
                Check(final.Lighting == 7, "S1b: instance B's own change saved", "lighting=" + final.Lighting);
                break;
            }
            case "S2": // 4 instances on 4 threads, distinct fields, gated so all baselines predate all saves
            {
                var dir = FreshDir(root, "s2");
                var mgrs = Enumerable.Range(0, 4).Select(_ => new SettingsManager(dir)).ToArray();
                var loaded = mgrs.Select(m => m.Load()).ToArray(); // all baselines = initial file
                var gate = new ManualResetEventSlim(false);
                var fields = new Action<AppSettings, int>[] {
                    (s, v) => s.Fps = v, (s, v) => s.Lighting = v, (s, v) => s.Speed = v, (s, v) => s.Overlay = v };
                var values = new int[] { 101, 202, 303, 404 };
                var saveErrors = 0;
                var threads = Enumerable.Range(0, 4).Select(i => new Thread(() =>
                {
                    fields[i](loaded[i], values[i]);
                    gate.Wait();
                    try { mgrs[i].Save(loaded[i]); }
                    catch (Exception) { Interlocked.Increment(ref saveErrors); }
                })).ToArray();
                foreach (var t in threads) t.Start();
                Thread.Sleep(200); // all threads parked at the gate, baselines taken
                gate.Set();
                foreach (var t in threads) t.Join();
                Console.WriteLine("info concurrent save exceptions=" + saveErrors + " (informational: unsynchronized writers may collide)");
                var final = ReadFileSettings(dir);
                Check(final.Fps == 101, "S2a: Fps from thread 0 survived concurrent saves", "fps=" + final.Fps);
                Check(final.Lighting == 202, "S2b: Lighting from thread 1 survived concurrent saves", "lighting=" + final.Lighting);
                Check(final.Speed == 303, "S2c: Speed from thread 2 survived concurrent saves", "speed=" + final.Speed);
                Check(final.Overlay == 404, "S2d: Overlay from thread 3 survived concurrent saves", "overlay=" + final.Overlay);
                break;
            }
            case "S3": // 500 saves while a reader parses raw bytes; torn reads are informational
            {
                var dir = FreshDir(root, "s3");
                var w = new SettingsManager(dir);
                var ws = w.Load();
                w.Save(ws); // file exists before readers start
                var stop = false;
                var torn = 0; var reads = 0;
                var path = Path.Combine(dir, "Build", "astrohybrid-settings.json");
                var reader = new Thread(() =>
                {
                    while (!Volatile.Read(ref stop))
                    {
                        try
                        {
                            var bytes = File.ReadAllBytes(path);
                            reads++;
                            using (var doc = JsonDocument.Parse(bytes)) { }
                        }
                        catch (JsonException) { torn++; }
                        catch (IOException) { /* file mid-replace; not torn content */ }
                    }
                });
                reader.Start();
                for (int i = 0; i < 500; i++) { ws.Fps = i; w.Save(ws); }
                Volatile.Write(ref stop, true);
                reader.Join();
                Console.WriteLine("info concurrent raw reads=" + reads + " torn=" + torn + " (torn count is informational, timing-dependent)");
                var finalOk = true;
                try { var f = ReadFileSettings(dir); finalOk = (f.Fps == 499); } catch { finalOk = false; }
                Check(finalOk, "S3a: final settings file valid with last value after sustained saves", "checked");
                var leftovers = Directory.GetFiles(Path.Combine(dir, "Build"), "*.tmp*");
                Check(leftovers.Length == 0, "S3b: no orphaned temp files after sustained saves", leftovers.Length + " left");
                break;
            }
        }
        Console.WriteLine(fails == 0 ? "scenario " + scenario + " passed" : "scenario " + scenario + ": " + fails + " check(s) failed");
        return fails;
    }
}
