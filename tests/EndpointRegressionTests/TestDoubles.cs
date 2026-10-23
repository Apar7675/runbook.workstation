using RunBook.Workstation.Models;
using System;
using System.Net.Http;
namespace RunBook.Workstation.Services
{
    // These stand-ins prohibit external identity or machine-state IO in this isolated harness.
    internal static class WorkstationStorageService { public static WorkstationRegistrationSnapshot? LoadRegistration() => null; }
    internal static class ControlSessionService { public static bool HasSession() => false; public static string GetAccessToken(string _) => throw new InvalidOperationException("Unexpected Control request in Service routing test."); }
    internal static class DebugLogService { public static void Write(string _) { } }
    internal static class ControlCallDetectionService
    {
        public static bool IsEnabled => false;
        public static HttpMessageHandler CreateGuardHandler(HttpMessageHandler handler) => handler;
    }
}
