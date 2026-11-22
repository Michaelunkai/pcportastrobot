// Harness v2: self-update file-replacement regression, one scenario per process.
// Compiled AS AstroHybrid.exe and run from an app dir, so InstallUpdateAsync replaces
// the RUNNING executable - the exact self-update condition. Usage: AstroHybrid.exe <scenario>
// Scenarios: A happy | B existing-old | C locked-old | D copy-failure+restore | F nested-parity
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using AstroHybrid.App.Models;
using AstroHybrid.App.Services;

public static class UpdateSelfReplaceTest
{
    static int fails = 0;
    static string selfPath = "";
    static string appDir = "";
    static byte[] selfBytes = new byte[0];
    static string root = "";

    static void Check(bool cond, string label, string detail)
    {
        Console.WriteLine((cond ? "ok   " : "FAIL ") + label + " (" + detail + ")");
        if (!cond) fails++;
    }

    static void CleanFixtures()
    {
        // Previous scenario's process has exited, so its locks are released.
        foreach (var p in Directory.GetFiles(appDir, "AstroHybrid.exe.old*")
                     .Concat(new[] { Path.Combine(appDir, "marker.dll"), Path.Combine(appDir, "marker.dll.old"), Path.Combine(appDir, "00first.dll"), Path.Combine(appDir, "00first.dll.old") }))
            try { File.Delete(p); } catch { }
        try { if (!File.ReadAllBytes(selfPath).SequenceEqual(selfBytes)) File.WriteAllBytes(selfPath, selfBytes); } catch { }
        var pkgDir = Path.Combine(appDir, "pkg");
        try { if (Directory.Exists(pkgDir)) Directory.Delete(pkgDir, true); } catch { }
        var subFile = Path.Combine(appDir, "sub");
        try { if (File.Exists(subFile)) File.Delete(subFile); } catch { }
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }

    static UpdateService MakeService(out string tempDir)
    {
        root = Path.Combine(Path.GetTempPath(), "astro-upd-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var svc = new UpdateService(root);
        tempDir = Path.Combine(root, "Build", "launcher", "temp");
        Directory.CreateDirectory(tempDir);
        return svc;
    }

    static void WriteZip(string zipPath, params Tuple<string, byte[]>[] entries)
    {
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            foreach (var e in entries)
                using (var w = new BinaryWriter(zip.CreateEntry(e.Item1).Open())) w.Write(e.Item2);
    }

    public static int Main(string[] args)
    {
        selfPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        appDir = Path.GetDirectoryName(selfPath)!;
        selfBytes = File.ReadAllBytes(selfPath);
        var scenario = args.Length > 0 ? args[0] : "A";
        var newExe = new byte[] { 1, 2, 3, 4, 5 };
        var newExe2 = new byte[] { 6, 7, 8 };
        var newDll = new byte[] { 9, 9, 9 };
        CleanFixtures();
        string tempDir;

        try
        {
            switch (scenario)
            {
                case "A": // happy path: flat package replaces running exe + dll
                {
                    var svc = MakeService(out tempDir);
                    WriteZip(Path.Combine(tempDir, "update.zip"),
                        Tuple.Create("AstroHybrid.exe", newExe), Tuple.Create("marker.dll", newDll));
                    File.WriteAllBytes(Path.Combine(appDir, "marker.dll"), new byte[] { 7 });
                    bool ok = svc.InstallUpdateAsync(new UpdateInfo { FileName = "update.zip", Version = "9.9.9" }).GetAwaiter().GetResult();
                    Check(ok, "A1: install succeeds while running", "returned " + ok);
                    Check(File.ReadAllBytes(selfPath).SequenceEqual(newExe), "A2: exe replaced", "exe=" + (File.ReadAllBytes(selfPath).SequenceEqual(newExe) ? "update" : "other"));
                    Check(File.Exists(selfPath + ".old") && File.ReadAllBytes(selfPath + ".old").SequenceEqual(selfBytes), "A3: original preserved as .old", File.Exists(selfPath + ".old") ? ".old exists" : ".old missing");
                    Check(File.ReadAllBytes(Path.Combine(appDir, "marker.dll")).SequenceEqual(newDll), "A4: dll replaced", "marker=" + (File.ReadAllBytes(Path.Combine(appDir, "marker.dll")).SequenceEqual(newDll) ? "update" : "original"));
                    break;
                }
                case "B": // second update: stale unlocked .old is replaced, install still succeeds
                {
                    File.WriteAllBytes(selfPath + ".old", new byte[] { 42 }); // stale .old from a previous update
                    var svc = MakeService(out tempDir);
                    WriteZip(Path.Combine(tempDir, "update.zip"), Tuple.Create("AstroHybrid.exe", newExe2));
                    bool ok = svc.InstallUpdateAsync(new UpdateInfo { FileName = "update.zip", Version = "9.9.10" }).GetAwaiter().GetResult();
                    Check(ok, "B1: install succeeds with pre-existing .old", "returned " + ok);
                    Check(File.ReadAllBytes(selfPath).SequenceEqual(newExe2), "B2: exe replaced with second-update bytes", "exe=" + (File.ReadAllBytes(selfPath).SequenceEqual(newExe2) ? "update2" : "other"));
                    Check(File.ReadAllBytes(selfPath + ".old").SequenceEqual(selfBytes), "B3: .old now holds this run's original exe", ".old=" + (File.ReadAllBytes(selfPath + ".old").SequenceEqual(selfBytes) ? "original" : "stale"));
                    break;
                }
                case "C": // locked .old: unique-name fallback, install still succeeds
                {
                    File.WriteAllBytes(selfPath + ".old", new byte[] { 42 });
                    var svc = MakeService(out tempDir);
                    WriteZip(Path.Combine(tempDir, "update.zip"), Tuple.Create("AstroHybrid.exe", newExe));
                    using (var lockStream = new FileStream(selfPath + ".old", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        bool ok = svc.InstallUpdateAsync(new UpdateInfo { FileName = "update.zip", Version = "9.9.11" }).GetAwaiter().GetResult();
                        Check(ok, "C1: install succeeds with locked .old", "returned " + ok);
                        Check(File.ReadAllBytes(selfPath).SequenceEqual(newExe), "C2: exe replaced", "exe=" + (File.ReadAllBytes(selfPath).SequenceEqual(newExe) ? "update" : "other"));
                        var fallback = Directory.GetFiles(appDir, "AstroHybrid.exe.old.*");
                        Check(fallback.Length == 1 && File.ReadAllBytes(fallback[0]).SequenceEqual(selfBytes),
                            "C3: unique-name fallback holds original exe", fallback.Length + " fallback file(s)");
                    }
                    Check(File.ReadAllBytes(selfPath + ".old").SequenceEqual(new byte[] { 42 }), "C4: locked .old untouched", ".old=" + (File.ReadAllBytes(selfPath + ".old").SequenceEqual(new byte[] { 42 }) ? "stale-dummy" : "changed"));
                    break;
                }
                case "D": // copy failure after rename -> install fails, restore must bring the original back
                {
                    var svc = MakeService(out tempDir);
                    WriteZip(Path.Combine(tempDir, "update.zip"),
                        Tuple.Create("00first.dll", new byte[] { 3, 3 }),
                        Tuple.Create("AstroHybrid.exe", newExe),
                        Tuple.Create("marker.dll", newDll),
                        Tuple.Create("sub/nested.dll", new byte[] { 5 }));
                    File.WriteAllBytes(Path.Combine(appDir, "marker.dll"), new byte[] { 7 });
                    File.WriteAllBytes(Path.Combine(appDir, "sub"), new byte[] { 0 }); // "sub" exists as a FILE -> CreateDirectory fails mid-install
                    bool ok = svc.InstallUpdateAsync(new UpdateInfo { FileName = "update.zip", Version = "9.9.12" }).GetAwaiter().GetResult();
                    Check(!ok, "D1: failing package returns false", "returned " + ok);
                    var exeNow = File.ReadAllBytes(selfPath);
                    Check(exeNow.SequenceEqual(selfBytes), "E1: original exe preserved/restored after failure",
                        "exe=" + (exeNow.SequenceEqual(selfBytes) ? "original" : exeNow.SequenceEqual(newExe) ? "UPDATE (broken/mixed install)" : "other"));
                    var markerNow = File.ReadAllBytes(Path.Combine(appDir, "marker.dll"));
                    Check(markerNow.SequenceEqual(new byte[] { 7 }), "E2: marker.dll preserved/restored after failure",
                        "marker=" + (markerNow.SequenceEqual(new byte[] { 7 }) ? "original" : "UPDATE (mixed install)"));
                    var leftover = File.Exists(Path.Combine(appDir, "00first.dll"));
                    Check(leftover, "D2: newly-copied file survives a failed install (restore does not clean new files - parity limitation)",
                        leftover ? "00first.dll left behind" : "cleaned");
                    break;
                }
                case "F": // nested package layout: files land in subdir (pre-existing limitation, parity check)
                {
                    var svc = MakeService(out tempDir);
                    WriteZip(Path.Combine(tempDir, "update.zip"), Tuple.Create("pkg/AstroHybrid.exe", newExe));
                    bool ok = svc.InstallUpdateAsync(new UpdateInfo { FileName = "update.zip", Version = "9.9.13" }).GetAwaiter().GetResult();
                    var nested = Path.Combine(appDir, "pkg", "AstroHybrid.exe");
                    Console.WriteLine("info nested install returned " + ok + ", nested file exists: " + File.Exists(nested));
                    Check(File.ReadAllBytes(selfPath).SequenceEqual(selfBytes), "F1: nested package does NOT replace root exe (parity/limitation)", "root exe=" + (File.ReadAllBytes(selfPath).SequenceEqual(selfBytes) ? "original" : "changed"));
                    Check(File.Exists(nested), "F2: nested files land in subdir (parity/limitation)", File.Exists(nested) ? "nested exists" : "nested missing");
                    break;
                }
            }
            Console.WriteLine(fails == 0 ? "scenario " + scenario + " passed" : "scenario " + scenario + ": " + fails + " check(s) failed");
            return fails;
        }
        finally
        {
            // Best-effort restore so the same exe stays re-runnable (locks release at process exit).
            try { if (!File.ReadAllBytes(selfPath).SequenceEqual(selfBytes)) File.WriteAllBytes(selfPath, selfBytes); } catch { }
        }
    }
}
