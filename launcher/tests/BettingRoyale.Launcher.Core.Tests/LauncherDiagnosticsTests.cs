using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Text.Json;
using BettingRoyale.Launcher.Core;

namespace BettingRoyale.Launcher.Core.Tests;

public sealed class LauncherDiagnosticsTests
{
    [Fact]
    public void MissingConfigurationCannotSilentlySelectProduction()
    {
        var root = Directory.CreateTempSubdirectory("BrConfig-");
        try { Assert.Throws<FileNotFoundException>(() => LauncherRuntimeConfiguration.Load(root.FullName)); }
        finally { root.Delete(true); }
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return [new HttpRequestException("private", null, HttpStatusCode.NotFound), "HTTP404"];
        yield return [new HttpRequestException("private", null, HttpStatusCode.Forbidden), "HTTPAUTH"];
        yield return [new HttpRequestException("private", null, HttpStatusCode.ServiceUnavailable), "HTTP5XX"];
        yield return [new HttpRequestException("private", null, HttpStatusCode.BadRequest), "HTTP"];
        yield return [new HttpRequestException("private"), "NETWORK"];
        yield return [new TaskCanceledException(), "TIMEOUT"];
        yield return [new UnauthorizedAccessException(), "ACCESS"];
        yield return [new FileNotFoundException(), "MISSING"];
        yield return [new PathTooLongException(), "PATH"];
        yield return [new IOException("private", unchecked((int)0x80070020)), "LOCKED"];
        yield return [new IOException("private", unchecked((int)0x80070070)), "DISKFULL"];
        yield return [new InvalidDataException(), "FORMAT"];
        yield return [new JsonException(), "FORMAT"];
        yield return [new UriFormatException(), "CONFIG"];
        yield return [new IOException(), "IO"];
        yield return [new Win32Exception(193), "PROCESS"];
        yield return [new NotSupportedException(), "UNSUPPORTED"];
        yield return [new InvalidOperationException(), "STATE"];
        yield return [new Exception("private"), "UNEXPECTED"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void EveryManagedFailureGetsAStableCode(Exception exception, string cause)
    {
        var failure = LauncherDiagnostics.Describe(exception, "L", "CHECK");
        Assert.Equal("BRL-L-CHECK-" + cause, failure.Code);
        Assert.Equal(12, failure.IncidentId.Length);
        Assert.DoesNotContain("private", failure.Display);
    }

    [Fact]
    public void DeepStageAndInvocationWrapperArePreserved()
    {
        var exception = LauncherDiagnostics.At(new InvalidDataException(), "VERIFY");
        LauncherDiagnostics.At(exception, "INSTALL");
        var failure = LauncherDiagnostics.Describe(new TargetInvocationException(exception), "B", "UI");
        Assert.Equal("BRL-B-VERIFY-INTEGRITY", failure.Code);
    }

    [Fact]
    public async Task LogFailureDoesNotReplaceOriginalFailure()
    {
        var root = Directory.CreateTempSubdirectory("BrDiagnostics-");
        try
        {
            var path = Path.Combine(root.FullName, "blocked.log");
            var log = new FileLauncherLog(path);
            Directory.CreateDirectory(path);
            var failure = await LauncherDiagnostics.ReportAsync(new HttpRequestException("test", null, HttpStatusCode.NotFound), "L", "MANIFEST", log);
            Assert.Equal("BRL-L-MANIFEST-HTTP404", failure.Code);
            Assert.Contains("BRL-L-LOG-", failure.Display);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void UnrecoveredBackupIsNeverDeleted()
    {
        var root = Directory.CreateTempSubdirectory("BrRecovery-");
        try
        {
            var backup = Path.Combine(root.FullName, ".bettingroyale-patch", "interrupted", "backup");
            Directory.CreateDirectory(backup);
            var original = Path.Combine(backup, "game.exe");
            File.WriteAllText(original, "previous working build");
            var exception = Assert.Throws<IOException>(() => FilePatchEngine.ClearAbandonedWork(root.FullName));
            Assert.Equal("BRL-L-ROLLBACK-IO", LauncherDiagnostics.Describe(exception, "L", "CHECK").Code);
            Assert.True(File.Exists(original));
        }
        finally { root.Delete(true); }
    }
}
