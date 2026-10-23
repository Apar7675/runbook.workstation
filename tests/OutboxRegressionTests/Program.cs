using RunBook.Workstation.Models;
using RunBook.Workstation.Services;
using System.Text;

var count = 0;
void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    count++;
}
WorkstationSessionSnapshot Session(string employee) => new()
{
    ShopId = "shop-1", WorkstationId = "kiosk-1", Token = "session-" + employee,
    Employee = new() { EmployeeId = employee, EmployeeCode = "E" + employee, RemoteEmployeeId = "remote-" + employee }
};
WorkstationSyncQueueItem Item(string employee, string type = "punch") => new()
{
    ShopId = "shop-1", WorkstationId = "kiosk-1", DeviceId = "kiosk-1",
    EmployeeId = employee, EmployeeCode = "E" + employee, RemoteEmployeeId = "remote-" + employee,
    ItemType = type, SyncStatus = "pending", ClientEventId = Guid.NewGuid().ToString()
};
var employeeA = Session("11");
var employeeB = Session("22");
var aPunch = Item("11");
var aPto = Item("11", "time_off_request");
var bPunch = Item("22");
var outbox = new[] { aPunch, aPto, bPunch };
var forB = outbox.Where(item => WorkstationQueuePolicy.BelongsToSession(item, employeeB)).ToArray();
Assert(forB.Length == 1 && ReferenceEquals(forB[0], bPunch), "B must never replay A's punch or PTO");
Assert(outbox.Count(item => WorkstationQueuePolicy.BelongsToSession(item, employeeA)) == 2, "A events remain recoverable");
var changed = Item("11"); changed.ShopId = "shop-2";
Assert(!WorkstationQueuePolicy.BelongsToSession(changed, employeeA), "wrong shop rejected");
changed = Item("11"); changed.WorkstationId = "other";
Assert(!WorkstationQueuePolicy.BelongsToSession(changed, employeeA), "wrong workstation rejected");
changed = Item("11"); changed.DeviceId = "other";
Assert(!WorkstationQueuePolicy.BelongsToSession(changed, employeeA), "wrong device rejected");
changed = Item("11"); changed.RemoteEmployeeId = "remote-22";
Assert(!WorkstationQueuePolicy.BelongsToSession(changed, employeeA), "remote identity mismatch rejected");
changed = Item("11"); changed.EmployeeCode = "E22";
Assert(!WorkstationQueuePolicy.BelongsToSession(changed, employeeA), "employee code mismatch rejected");
changed = Item("11"); changed.EmployeeId = "";
Assert(!WorkstationQueuePolicy.BelongsToSession(changed, employeeA), "missing actor retained instead of reassigned");
changed = Item("11"); changed.RemoteEmployeeId = ""; changed.EmployeeCode = "";
Assert(WorkstationQueuePolicy.BelongsToSession(changed, employeeA), "local actor without remote provisioning remains supported");
Assert(!WorkstationQueuePolicy.MatchesDeviceScope(employeeA, "shop-2", "kiosk-1"), "late response cannot persist into another shop");

var folder = Path.Combine(Path.GetTempPath(), "RunBookOutboxRegression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var queue = Path.Combine(folder, "timeclock-queue.json");
    var evidence = Encoding.UTF8.GetBytes("[pending event bytes retained even when unreadable");
    File.WriteAllBytes(queue, evidence);
    File.WriteAllText(Path.Combine(folder, "workstation-session.dat"), "old session");
    File.WriteAllText(Path.Combine(folder, "unknown-preserved.json"), "keep");
    WorkstationQueuePolicy.ClearDisposableCache(folder);
    Assert(File.ReadAllBytes(queue).SequenceEqual(evidence), "shop cleanup preserves exact pending/corrupt outbox bytes");
    Assert(!File.Exists(Path.Combine(folder, "workstation-session.dat")), "old session credentials cleared");
    Assert(File.Exists(Path.Combine(folder, "unknown-preserved.json")), "unknown evidence retained");

    var unreadableRejected = false;
    try { WorkstationOutboxFile.Read(queue); }
    catch (InvalidDataException) { unreadableRejected = true; }
    Assert(unreadableRejected, "unreadable historical events are not treated as an empty outbox");
    var overwriteRejected = false;
    try { WorkstationOutboxFile.Write(queue, new[] { Item("11") }); }
    catch (InvalidDataException) { overwriteRejected = true; }
    Assert(overwriteRejected && File.ReadAllBytes(queue).SequenceEqual(evidence), "unreadable original cannot be overwritten with new events");
    var controlledFailure = WorkstationOutboxFile.TryWrite(queue, new[] { Item("11") }, out var recoveryMessage);
    Assert(!controlledFailure && recoveryMessage.Contains("preserved") && File.ReadAllBytes(queue).SequenceEqual(evidence), "UI publication receives a recovery result without an exception or original-byte loss");
    WorkstationAtomicFile.Write(queue, Encoding.UTF8.GetBytes("[]")); // Explicit synthetic recovery.
    WorkstationOutboxFile.Write(queue, new[] { aPunch, aPto });
    Assert(WorkstationOutboxFile.Read(queue).Select(item => item.ClientEventId).SequenceEqual(new[] { aPunch.ClientEventId, aPto.ClientEventId }), "valid recovered outbox retains exact event identities");

    WorkstationAtomicFile.Write(queue, Encoding.UTF8.GetBytes("old complete queue"));
    WorkstationAtomicFile.Write(queue, Encoding.UTF8.GetBytes("new complete queue"));
    Assert(File.ReadAllText(queue) == "new complete queue", "atomic replacement publishes full queue");
    using (var exclusive = new FileStream(queue, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        var rejected = false;
        try { WorkstationAtomicFile.Write(queue, Encoding.UTF8.GetBytes("must not replace locked destination")); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { rejected = true; }
        if (OperatingSystem.IsWindows()) Assert(rejected, "Windows lock must reject publication");
    }
    if (OperatingSystem.IsWindows())
        Assert(File.ReadAllText(queue) == "new complete queue", "failed publication preserves previously durable queue");
    Assert(!Directory.EnumerateFiles(folder, "*.tmp").Any(), "failed publication cleans temporary file only");
    WorkstationAtomicFile.Write(queue, Encoding.UTF8.GetBytes("[]")); // Reset only this synthetic fixture after raw-byte atomic tests.
    WorkstationOutboxFile.Write(queue, new[] { aPunch });
    var durableBytes = File.ReadAllBytes(queue);
    using (var locked = new FileStream(queue, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        var accepted = WorkstationOutboxFile.TryWrite(queue, new[] { aPunch, aPto }, out var error);
        if (OperatingSystem.IsWindows())
            Assert(!accepted && error.Contains("not recorded"), "failed durable append reports that the new event was not accepted");
    }
    if (OperatingSystem.IsWindows())
        Assert(File.ReadAllBytes(queue).SequenceEqual(durableBytes), "controlled append failure preserves the complete previous outbox");
    Assert(!Directory.EnumerateFiles(folder, "*.tmp").Any(), "controlled append failure leaves no temporary payload");
}
finally
{
    // Only this test's newly created synthetic directory is removed.
    Directory.Delete(folder, recursive: true);
}
Console.WriteLine($"OUTBOX_REGRESSION_RESULT passed={count}");
