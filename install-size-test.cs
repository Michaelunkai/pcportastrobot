// Install-size regression harness: runs against the REAL compiled InstallationManager
// from astro-check.dll (full-source Roslyn/net48 build of the checked-out branch).
// Deterministic checks, no timing thresholds:
//  A) caller-return behavior: pre-fix the whole method (incl. directory walk) completes
//     synchronously, so the returned Task is already completed when the call returns;
//     post-fix the walk sits behind await Task.Run, so the task is NOT completed at return.
//  B) returned state correctness: IsInstalled and InstallSizeBytes against a known fixture.
//  C) exception propagation: corrupt settings json must surface the same way before/after.
// Fixture lives under F:\InstinctTemp only; no user processes, no game files.
using System;
using System.IO;
using System.Threading.Tasks;
using AstroHybrid.App.Services;

static class InstallSizeTest
{
    static int failures;

    static void Check(string name, bool cond, string detail)
    {
        if (!cond) { failures++; Console.WriteLine("FAIL " + name + ": " + detail); }
        else Console.WriteLine("ok   " + name + " (" + detail + ")");
    }

    static long Walk(string path)
    {
        long size = 0;
        var info = new DirectoryInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return 0;
        foreach (var f in info.GetFiles()) size += f.Length;
        foreach (var d in info.GetDirectories()) size += Walk(d.FullName);
        return size;
    }

    static string MakeFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "install-size-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "Build", "RunHybrid");
        Directory.CreateDirectory(Path.Combine(install, "libs"));
        Directory.CreateDirectory(Path.Combine(install, "app0", "data"));
        foreach (var f in new[] { "AstroBot.exe", "kyty_gpu.dll", "astrohybrid_host.dll", "u59-preset.cmd" })
            File.WriteAllBytes(Path.Combine(install, f), new byte[] { 1 });
        foreach (var f in new[] { "libc.prx", "libkernel.prx", "libScePad.prx", "libSceAgc.prx", "libSceAgcDriver.prx", "libSceVideoOut.prx" })
            File.WriteAllBytes(Path.Combine(install, "libs", f), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(install, "app0", "data", "placeholder.bin"), new byte[] { 1 });
        // 40000 tiny files so the walk cannot plausibly finish before the caller-return check.
        var fill = Path.Combine(install, "fill");
        Directory.CreateDirectory(fill);
        var payload = new byte[] { 1, 2, 3 };
        for (int i = 0; i < 40000; i++)
            File.WriteAllBytes(Path.Combine(fill, "f" + i + ".bin"), payload);
        return root;
    }

    static int Main()
    {
        var root = MakeFixture();
        try
        {
            var install = Path.Combine(root, "Build", "RunHybrid");
            var expected = Walk(install);

            // A + B
            var mgr = new InstallationManager(root);
            var task = mgr.GetInstallationStateAsync();
            bool completedAtReturn = task.IsCompleted;
            var state = task.GetAwaiter().GetResult();
            Check("A: walk runs off the caller thread", !completedAtReturn,
                "task.IsCompleted at synchronous return = " + completedAtReturn + " (pre-fix = true)");
            Check("B1: state.IsInstalled", state.IsInstalled, "IsInstalled=" + state.IsInstalled);
            Check("B2: InstallSizeBytes exact", state.InstallSizeBytes == expected,
                state.InstallSizeBytes + " vs expected " + expected);

            // C: corrupt settings json -> same surface before/after (throw type or swallowed)
            File.WriteAllText(Path.Combine(root, "Build", "astrohybrid-settings.json"), "{ not json");
            string surface;
            try
            {
                var mgr2 = new InstallationManager(root);
                var s2 = mgr2.GetInstallationStateAsync().GetAwaiter().GetResult();
                surface = "no-throw IsInstalled=" + s2.IsInstalled;
            }
            catch (Exception ex)
            {
                surface = "throws " + ex.GetType().Name;
            }
            Console.WriteLine("info C: corrupt settings surface = " + surface + " (must match pre-fix build)");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine(failures == 0 ? "all install-size checks passed" : failures + " install-size check(s) failed");
        return failures == 0 ? 0 : 1;
    }
}
