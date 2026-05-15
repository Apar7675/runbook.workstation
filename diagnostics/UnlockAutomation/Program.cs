using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;

var workstationExe = Path.Combine(
    FindRepoRoot(AppContext.BaseDirectory),
    "src",
    "RunBook.Workstation",
    "bin",
    "Debug",
    "net8.0-windows10.0.19041.0",
    "RunBook.Workstation.exe");

const string employeeName = "Angelo";
const string passcode = "3865";

var process = Process.GetProcessesByName("RunBook.Workstation")
    .Where(candidate => !candidate.HasExited)
    .OrderByDescending(candidate => candidate.StartTime)
    .FirstOrDefault();

if (process == null)
{
    process = Process.Start(new ProcessStartInfo
    {
        FileName = workstationExe,
        UseShellExecute = true,
    }) ?? throw new InvalidOperationException("Could not start RunBook.Workstation.");
}

process.WaitForInputIdle(15000);
using var automation = new UIA3Automation();
var app = Application.Attach(process);
var window = Retry(() => app.GetMainWindow(automation), element => element != null, TimeSpan.FromSeconds(20))
    ?? throw new InvalidOperationException("Workstation main window did not appear.");

var logoutElement = FindElementByPrefix(window, "Log Out / Switch User");
if (logoutElement != null)
{
    ClickElement(logoutElement);
    Thread.Sleep(1000);
}

var employeeElement = Retry(
    () => FindElementByName(window, employeeName),
    element => element != null,
    TimeSpan.FromSeconds(20))
    ?? throw new InvalidOperationException($"Could not find employee tile '{employeeName}'.");

ClickElement(employeeElement);

Thread.Sleep(1000);
Keyboard.Type(passcode);
Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.RETURN);
Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.RETURN);

var homeElement = Retry(
    () => FindElementByName(window, "Home"),
    element => element != null && !element.Properties.IsOffscreen.ValueOrDefault,
    TimeSpan.FromSeconds(30));

Console.WriteLine(homeElement != null
    ? "Unlock automation completed."
    : "Unlock automation submitted input, but Home was not detected before timeout.");

static AutomationElement? FindElementByName(AutomationElement root, string name)
{
    return root.FindAllDescendants()
        .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
}

static AutomationElement? FindElementByPrefix(AutomationElement root, string prefix)
{
    return root.FindAllDescendants()
        .FirstOrDefault(candidate => candidate.Name?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true);
}

static void ClickElement(AutomationElement element)
{
    Mouse.MoveTo(element.GetClickablePoint());
    Mouse.Click();
}

static T? Retry<T>(Func<T?> action, Func<T?, bool> success, TimeSpan timeout)
{
    var stopwatch = Stopwatch.StartNew();
    while (stopwatch.Elapsed < timeout)
    {
        var result = action();
        if (success(result))
            return result;

        Thread.Sleep(250);
    }

    return action();
}

static string FindRepoRoot(string start)
{
    var current = new DirectoryInfo(start);
    while (current != null)
    {
        if (File.Exists(Path.Combine(current.FullName, "RunBook.Workstation.sln")))
            return current.FullName;

        current = current.Parent;
    }

    return Directory.GetCurrentDirectory();
}
