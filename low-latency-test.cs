// Low-latency regression harness: runs against the REAL compiled LauncherService
// from astro-check.dll (full-source Roslyn/net48 build of the checked-out branch).
// Verifies ASTROHYBRID_LOW_LATENCY for setting off / on / stale process env.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AstroHybrid.App.Models;
using AstroHybrid.App.Services;

static class LowLatencyTest
{
    static int failures;

    static void Check(string name, string actual, string expected)
    {
        if (actual != expected) { failures++; Console.WriteLine("FAIL " + name + ": " + actual + " != " + expected); }
        else Console.WriteLine("ok   " + name + " = " + actual);
    }

    static Dictionary<string, string> BuildEnv(string root, bool lowLatency)
    {
        var service = new LauncherService(root);
        var method = typeof(LauncherService).GetMethod("BuildEnvironmentVariables", BindingFlags.NonPublic | BindingFlags.Instance);
        var settings = new AppSettings { LowLatency = lowLatency };
        return (Dictionary<string, string>)method.Invoke(service, new object[] { settings, null });
    }

    static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "low-latency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // 1. off must pass an explicit 0 (the DLSS bat turns low latency on when the var is unset)
            var off = BuildEnv(root, false);
            Check("off -> explicit 0", off.TryGetValue("ASTROHYBRID_LOW_LATENCY", out var offValue) ? offValue : "<missing>", "0");
            // 2. on passes 1
            var on = BuildEnv(root, true);
            Check("on -> 1", on.TryGetValue("ASTROHYBRID_LOW_LATENCY", out var onValue) ? onValue : "<missing>", "1");
            // 3. a stale process-level value must not leak through: the explicit dict entry overrides it
            Environment.SetEnvironmentVariable("ASTROHYBRID_LOW_LATENCY", "1");
            var stale = BuildEnv(root, false);
            Check("stale env + off -> explicit 0", stale.TryGetValue("ASTROHYBRID_LOW_LATENCY", out var staleValue) ? staleValue : "<missing>", "0");
            Environment.SetEnvironmentVariable("ASTROHYBRID_LOW_LATENCY", null);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine(failures == 0 ? "all low-latency checks passed" : failures + " low-latency check(s) failed");
        return failures == 0 ? 0 : 1;
    }
}
