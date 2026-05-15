using System.Diagnostics;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var outputPath = Path.Combine(repoRoot, "diagnostics", "workstation-ui-dump.txt");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

var lines = new List<string>();
WriteLine($"TimestampUtc: {DateTime.UtcNow:O}");
WriteLine($"OutputPath: {outputPath}");

var process = Process
    .GetProcessesByName("RunBook.Workstation")
    .Where(p => !p.HasExited && p.MainWindowHandle != IntPtr.Zero)
    .OrderByDescending(p => p.StartTime)
    .FirstOrDefault();

if (process == null)
{
    WriteLine("Process: NOT FOUND");
    FlushAndExit(1);
    return;
}

WriteLine($"ProcessId: {process.Id}");
WriteLine($"ProcessPath: {Safe(() => process.MainModule?.FileName) ?? "<unknown>"}");
WriteLine($"MainWindowTitle: {process.MainWindowTitle}");

using var automation = new UIA3Automation();
var app = Application.Attach(process);
var window = app.GetMainWindow(automation);

if (window == null)
{
    WriteLine("MainWindow: NOT FOUND");
    FlushAndExit(2);
    return;
}

WriteLine("");
WriteLine("Main Window");
DumpElement(window, 0);

WriteLine("");
WriteLine("Tree (first 4 levels)");
DumpTree(window, 0, 4);

WriteLine("");
WriteLine("Interesting Matches");
var all = Enumerate(window, 6).ToList();
var matches = all.Where(IsInteresting).ToList();
if (matches.Count == 0)
{
    WriteLine("No matching elements found.");
}
else
{
    foreach (var match in matches)
    {
        WriteLine($"MatchDepth={match.Depth}");
        DumpElement(match.Element, match.Depth);
    }
}

WriteLine("");
WriteLine("Roster Candidates");
var rosterCandidates = all
    .Where(x => LooksLikeRosterContainer(x.Element))
    .OrderByDescending(x => SafeInt(() => x.Element.FindAllChildren().Length))
    .ToList();

if (rosterCandidates.Count == 0)
{
    WriteLine("No roster-like container found.");
}
else
{
    foreach (var candidate in rosterCandidates)
    {
        WriteLine($"RosterCandidateDepth={candidate.Depth}");
        DumpElement(candidate.Element, candidate.Depth);

        var children = SafeChildren(candidate.Element).Take(15).ToList();
        WriteLine($"GeneratedChildren={children.Count}");
        foreach (var child in children)
        {
            WriteLine($"  Child:");
            DumpElement(child, candidate.Depth + 1, "  ");
        }
    }
}

File.WriteAllLines(outputPath, lines, Encoding.UTF8);
Console.WriteLine(string.Join(Environment.NewLine, lines));
return;

void DumpTree(AutomationElement element, int depth, int maxDepth)
{
    DumpElement(element, depth);
    if (depth >= maxDepth)
        return;

    foreach (var child in SafeChildren(element))
    {
        DumpTree(child, depth + 1, maxDepth);
    }
}

IEnumerable<(AutomationElement Element, int Depth)> Enumerate(AutomationElement root, int maxDepth)
{
    var queue = new Queue<(AutomationElement Element, int Depth)>();
    queue.Enqueue((root, 0));

    while (queue.Count > 0)
    {
        var item = queue.Dequeue();
        yield return item;
        if (item.Depth >= maxDepth)
            continue;

        foreach (var child in SafeChildren(item.Element))
        {
            queue.Enqueue((child, item.Depth + 1));
        }
    }
}

void DumpElement(AutomationElement element, int depth, string prefix = "")
{
    var indent = new string(' ', depth * 2);
    var bounds = Safe(() => element.BoundingRectangle.ToString()) ?? "<no-bounds>";
    var controlType = Safe(() => element.ControlType.ToString()) ?? "<unknown>";
    var childCount = SafeInt(() => element.FindAllChildren().Length);
    var itemsSourceLike = Safe(() => element.Patterns.Scroll.PatternOrDefault == null ? "" : "ScrollPattern");

    WriteLine($"{prefix}{indent}Name={Safe(() => element.Name) ?? ""}");
    WriteLine($"{prefix}{indent}AutomationId={Safe(() => element.AutomationId) ?? ""}");
    WriteLine($"{prefix}{indent}ControlType={controlType}");
    WriteLine($"{prefix}{indent}ClassName={Safe(() => element.ClassName) ?? ""}");
    WriteLine($"{prefix}{indent}BoundingRectangle={bounds}");
    WriteLine($"{prefix}{indent}IsOffscreen={SafeBool(() => element.IsOffscreen)}");
    WriteLine($"{prefix}{indent}IsEnabled={SafeBool(() => element.IsEnabled)}");
    WriteLine($"{prefix}{indent}ChildCount={childCount}");
    if (!string.IsNullOrWhiteSpace(itemsSourceLike))
        WriteLine($"{prefix}{indent}PatternHint={itemsSourceLike}");
}

bool IsInteresting((AutomationElement Element, int Depth) item)
{
    var element = item.Element;
    var name = (Safe(() => element.Name) ?? "").ToLowerInvariant();
    var automationId = (Safe(() => element.AutomationId) ?? "").ToLowerInvariant();
    var className = (Safe(() => element.ClassName) ?? "").ToLowerInvariant();
    var controlType = (Safe(() => element.ControlType.ToString()) ?? "").ToLowerInvariant();

    return name.Contains("employee")
        || name.Contains("operator")
        || name.Contains("roster")
        || name.Contains("avatar")
        || automationId.Contains("roster")
        || className.Contains("itemscontrol")
        || className.Contains("listbox")
        || className.Contains("wrappanel")
        || controlType.Contains("list")
        || controlType.Contains("pane")
        || controlType.Contains("custom");
}

bool LooksLikeRosterContainer(AutomationElement element)
{
    var name = (Safe(() => element.Name) ?? "").ToLowerInvariant();
    var automationId = (Safe(() => element.AutomationId) ?? "").ToLowerInvariant();
    var className = (Safe(() => element.ClassName) ?? "").ToLowerInvariant();
    var controlType = (Safe(() => element.ControlType.ToString()) ?? "").ToLowerInvariant();
    var childCount = SafeInt(() => element.FindAllChildren().Length);

    return childCount > 0 && (
        name.Contains("employee")
        || name.Contains("roster")
        || automationId.Contains("roster")
        || className.Contains("itemscontrol")
        || className.Contains("wrappanel")
        || controlType.Contains("list"));
}

AutomationElement[] SafeChildren(AutomationElement element)
{
    try
    {
        return element.FindAllChildren();
    }
    catch
    {
        return Array.Empty<AutomationElement>();
    }
}

string FindRepoRoot(string start)
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

string? Safe(Func<string?> getter)
{
    try
    {
        return getter();
    }
    catch (Exception ex)
    {
        return $"<error:{ex.GetType().Name}>";
    }
}

bool SafeBool(Func<bool> getter)
{
    try
    {
        return getter();
    }
    catch
    {
        return false;
    }
}

int SafeInt(Func<int> getter)
{
    try
    {
        return getter();
    }
    catch
    {
        return -1;
    }
}

void WriteLine(string line) => lines.Add(line);

void FlushAndExit(int code)
{
    File.WriteAllLines(outputPath, lines, Encoding.UTF8);
    Console.WriteLine(string.Join(Environment.NewLine, lines));
    Environment.Exit(code);
}
