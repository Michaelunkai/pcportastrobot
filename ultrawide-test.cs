// Harness: Home summary resolution-label regression for ultrawide Size indices 4/5.
// Exercises the REAL compiled HomeViewModel + MainWindowViewModel from astro-check.dll.
// Deterministic string checks on CurrentGraphicsConfig; no timing.
using System;
using System.IO;
using System.Linq;
using AstroHybrid.App.Models;
using AstroHybrid.App.ViewModels;

public static class UltrawideTest
{
    static readonly string[] Labels = { "1280x720", "1600x900", "1920x1080", "2560x1440", "2560x1080", "3440x1440" };

    public static int Main()
    {
        int fails = 0;
        var root = Path.Combine(Path.GetTempPath(), "astro-uw-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var vm = new MainWindowViewModel(root);
            // Wait for fire-and-forget InitializeAsync so Settings is no longer reassigned.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (vm.IsLoading && sw.Elapsed.TotalSeconds < 120) System.Threading.Thread.Sleep(250);
            if (vm.IsLoading)
            {
                Console.WriteLine("FAIL init: InitializeAsync did not complete within 120s");
                return 1;
            }
            Console.WriteLine("info init: completed in " + sw.Elapsed.TotalSeconds.ToString("0.0") + "s");

            var home = new HomeViewModel(vm);

            void SetSize(int size, bool ultrawide = false)
            {
                vm.Settings.Size = size;
                vm.Settings.Ultrawide = ultrawide;
            }

            void Check(bool ok, string label, string detail)
            {
                Console.WriteLine((ok ? "ok   " : "FAIL ") + label + " (" + detail + ")");
                if (!ok) fails++;
            }

            // A: ultrawide indices must show their resolution in the Home summary.
            SetSize(4);
            var c4 = home.CurrentGraphicsConfig;
            Console.WriteLine("cfg Size=4: " + c4);
            Check(c4.Contains("2560x1080"), "A1: Size=4 summary shows 2560x1080", c4);

            SetSize(5);
            var c5 = home.CurrentGraphicsConfig;
            Console.WriteLine("cfg Size=5: " + c5);
            Check(c5.Contains("3440x1440"), "A2: Size=5 summary shows 3440x1440", c5);

            // B: 16:9 indices unchanged (parity with pre-fix behavior).
            SetSize(2);
            var c2 = home.CurrentGraphicsConfig;
            Console.WriteLine("cfg Size=2: " + c2);
            Check(c2.Contains("1920x1080"), "B1: Size=2 summary still shows 1920x1080", c2);

            SetSize(0);
            var c0 = home.CurrentGraphicsConfig;
            Check(c0.Contains("1280x720"), "B2: Size=0 summary still shows 1280x720", c0);

            // C: out-of-range indices behave identically to pre-fix (no crash, no label).
            SetSize(99);
            var c99 = home.CurrentGraphicsConfig;
            Console.WriteLine("cfg Size=99: " + c99);
            Check(!Labels.Any(l => c99.Contains(l)), "C1: Size=99 shows no resolution label (unchanged)", c99);

            SetSize(-1);
            var cn = home.CurrentGraphicsConfig;
            Check(!Labels.Any(l => cn.Contains(l)), "C2: Size=-1 shows no resolution label (unchanged)", cn);

            // D: ultrawide flag label unchanged.
            SetSize(4, ultrawide: true);
            var cu = home.CurrentGraphicsConfig;
            Console.WriteLine("cfg Size=4 UW: " + cu);
            Check(cu.Contains("21:9 Ultrawide"), "D1: ultrawide flag still adds 21:9 label", cu);

            Console.WriteLine(fails == 0 ? "all ultrawide checks passed" : fails + " ultrawide check(s) failed");
            return fails;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
