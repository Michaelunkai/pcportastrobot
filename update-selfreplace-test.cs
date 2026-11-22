// Harness: self-update file-replacement regression.
// This harness is compiled AS AstroHybrid.exe and run from an app directory, so
// UpdateService.InstallUpdateAsync replaces the RUNNING executable - the exact
// self-update condition. Deterministic byte/result checks; restores its own exe
// afterwards so the same file can be re-run.
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using AstroHybrid.App.Models;
using AstroHybrid.App.Services;

public static class UpdateSelfReplaceTest
{
    public static int Main()
    {
        int fails = 0;
        var selfPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var appDir = Path.GetDirectoryName(selfPath)!;
        var selfBytes = File.ReadAllBytes(selfPath);
        var root = Path.Combine(Path.GetTempPath(), "astro-upd-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string lastStatus = "";
        try
        {
            var svc = new UpdateService(root);
            svc.StatusChanged += m => lastStatus = m;
            svc.StateChanged += s => { if (!string.IsNullOrEmpty(s.DownloadStatus)) lastStatus = s.DownloadStatus; };

            // Fixture: update package with a new launcher exe + a new dll (flat layout,
            // as the real release ZIPs).
            var tempDir = Path.Combine(root, "Build", "launcher", "temp");
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, "update.zip");
            var newExe = new byte[] { 1, 2, 3, 4, 5 };
            var newDll = new byte[] { 9, 9, 9 };
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                using (var w = new BinaryWriter(zip.CreateEntry("AstroHybrid.exe").Open())) w.Write(newExe);
                using (var w = new BinaryWriter(zip.CreateEntry("marker.dll").Open())) w.Write(newDll);
            }
            var markerPath = Path.Combine(appDir, "marker.dll");
            File.WriteAllBytes(markerPath, new byte[] { 7 });

            var info = new UpdateInfo { FileName = "update.zip", Version = "9.9.9" };
            bool ok = svc.InstallUpdateAsync(info).GetAwaiter().GetResult();
            Console.WriteLine("info InstallUpdateAsync returned: " + ok + " | last status: " + lastStatus);

            void Check(bool cond, string label, string detail)
            {
                Console.WriteLine((cond ? "ok   " : "FAIL ") + label + " (" + detail + ")");
                if (!cond) fails++;
            }

            Check(ok, "A: self-update install succeeds while the launcher is running", "returned " + ok);

            var onDisk = File.ReadAllBytes(selfPath);
            Check(onDisk.SequenceEqual(newExe), "B1: launcher exe replaced by the update bytes",
                "on-disk exe " + (onDisk.SequenceEqual(newExe) ? "= update" : onDisk.SequenceEqual(selfBytes) ? "= original" : "= unexpected"));

            var oldPath = selfPath + ".old";
            Check(File.Exists(oldPath) && File.ReadAllBytes(oldPath).SequenceEqual(selfBytes),
                "B2: running exe moved aside to .old with original bytes",
                ".old " + (File.Exists(oldPath) ? "exists" : "missing"));

            Check(File.ReadAllBytes(markerPath).SequenceEqual(newDll),
                "C1: marker.dll replaced by the update bytes",
                "marker " + (File.ReadAllBytes(markerPath).SequenceEqual(newDll) ? "= update" : "= original"));

            Console.WriteLine(fails == 0 ? "all self-update checks passed" : fails + " self-update check(s) failed");
            return fails;
        }
        finally
        {
            // Restore the harness exe and clean fixtures so the file can be re-run.
            try
            {
                if (!File.ReadAllBytes(selfPath).SequenceEqual(selfBytes))
                    File.WriteAllBytes(selfPath, selfBytes);
            }
            catch (Exception ex) { Console.WriteLine("warn restore exe: " + ex.Message); }
            foreach (var p in new[] { selfPath + ".old", Path.Combine(appDir, "marker.dll"), Path.Combine(appDir, "marker.dll.old") })
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
