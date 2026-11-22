// Harness: PageTemplateSelector view-reuse regression.
// Compiles the REAL AstroHybrid.App/Views/MainWindow.xaml.cs together with these
// stubs (views are XAML code-behind, outside the csc scope) and checks view
// instance identity across repeated navigations. Deterministic: object identity,
// no timing.
using System;
using System.Globalization;
using System.Windows.Controls;
using AstroHybrid.App.Views;
using AstroHybrid.App.ViewModels;

namespace AstroHybrid.App.Views
{
    // Stubs for the XAML code-behind views (csc scope excludes .xaml.cs of views).
    public class HomeView : UserControl { }
    public class GraphicsSettingsView : UserControl { }
    public class ControllerSettingsView : UserControl { }
    public class AdvancedSettingsView : UserControl { }
    public class InstallationView : UserControl { }

    // The generated MainWindow.g.cs (InitializeComponent) is outside the csc scope.
    public partial class MainWindow
    {
        public void InitializeComponent() { }
    }
}

public static class PageViewTest
{
    [STAThread]
    public static int Main()
    {
        int fails = 0;
        var sel = new PageTemplateSelector();
        var c = CultureInfo.InvariantCulture;

        void Check(bool ok, string label, string detail)
        {
            Console.WriteLine((ok ? "ok   " : "FAIL ") + label + " (" + detail + ")");
            if (!ok) fails++;
        }

        // A: repeated navigation to the same page must return the same view instance.
        var h1 = sel.Convert(LauncherPage.Home, typeof(object), null, c);
        var g1 = sel.Convert(LauncherPage.GraphicsSettings, typeof(object), null, c);
        var h2 = sel.Convert(LauncherPage.Home, typeof(object), null, c);
        var g2 = sel.Convert(LauncherPage.GraphicsSettings, typeof(object), null, c);
        var h3 = sel.Convert(LauncherPage.Home, typeof(object), null, c);

        Check(ReferenceEquals(h1, h2) && ReferenceEquals(h2, h3),
            "A1: repeat navigation reuses the Home view instance",
            "h1==h2: " + ReferenceEquals(h1, h2) + ", h2==h3: " + ReferenceEquals(h2, h3));
        Check(ReferenceEquals(g1, g2),
            "A2: repeat navigation reuses the GraphicsSettings view instance",
            "g1==g2: " + ReferenceEquals(g1, g2));
        Check(!ReferenceEquals(h1, g1),
            "A3: different pages get different view instances",
            "h1!=g1: " + !ReferenceEquals(h1, g1));
        Check(h1 is HomeView && g1 is GraphicsSettingsView,
            "A4: returned views have the correct concrete types",
            h1.GetType().Name + ", " + g1.GetType().Name);

        // B: unknown/null values fall back to the Home view, also reused.
        var f1 = sel.Convert("bogus", typeof(object), null, c);
        var f2 = sel.Convert(null, typeof(object), null, c);
        Check(f1 is HomeView, "B1: unknown value falls back to a Home view", f1.GetType().Name);
        Check(ReferenceEquals(f1, f2),
            "B2: fallback view instance is reused",
            "f1==f2: " + ReferenceEquals(f1, f2));

        Console.WriteLine(fails == 0 ? "all page-view checks passed" : fails + " page-view check(s) failed");
        return fails;
    }
}
