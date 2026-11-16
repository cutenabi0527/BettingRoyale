using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace BettingRoyale.Launcher.Core;

public sealed record LauncherFailure(string Code, string IncidentId, string Message)
{
    public string Display => $"{Message}\n오류 코드: {Code} · 진단 ID: {IncidentId}";
}

public static class LauncherDiagnostics
{
    private const string StageKey = "BettingRoyale.FailureStage";
    public static Exception At(Exception exception, string stage)
    {
        if (!exception.Data.Contains(StageKey)) exception.Data[StageKey] = stage;
        return exception;
    }

    public static LauncherFailure Describe(Exception exception, string component, string stage)
    {
        while (exception is TargetInvocationException { InnerException: not null }) exception = exception.InnerException;
        stage = exception.Data[StageKey] as string ?? stage;
        var native = exception is Win32Exception win32 ? win32.NativeErrorCode : exception.HResult & 0xffff;
        var cause = exception switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "HTTP404",
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => "HTTPAUTH",
            HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } => "HTTP5XX",
            HttpRequestException { StatusCode: not null } => "HTTP",
            HttpRequestException => "NETWORK",
            OperationCanceledException => "TIMEOUT",
            UnauthorizedAccessException => "ACCESS",
            FileNotFoundException or DirectoryNotFoundException => "MISSING",
            PathTooLongException => "PATH",
            IOException or Win32Exception when native is 32 or 33 => "LOCKED",
            IOException or Win32Exception when native is 39 or 112 => "DISKFULL",
            Win32Exception when native == 5 => "ACCESS",
            Win32Exception when native is 2 or 3 => "MISSING",
            InvalidDataException when stage == "VERIFY" => "INTEGRITY",
            InvalidDataException or JsonException => "FORMAT",
            UriFormatException or ArgumentException => "CONFIG",
            IOException => "IO",
            Win32Exception => "PROCESS",
            NotSupportedException => "UNSUPPORTED",
            InvalidOperationException => "STATE",
            _ => "UNEXPECTED"
        };
        var message = cause switch
        {
            "HTTP404" => "배포 파일이 아직 게시되지 않았습니다. 잠시 후 다시 시도해 주세요.",
            "HTTPAUTH" => "배포 서버가 접근을 거부했습니다. 운영자에게 문의해 주세요.",
            "HTTP5XX" => "서버가 요청을 처리하지 못했습니다. 잠시 후 다시 시도해 주세요.",
            "HTTP" or "NETWORK" => "서버에 연결할 수 없습니다. 네트워크와 서버 상태를 확인해 주세요.",
            "TIMEOUT" => "요청 제한 시간을 초과했습니다. 다시 시도해 주세요.",
            "ACCESS" => "설치 또는 로그 폴더에 접근할 권한이 없습니다.",
            "MISSING" => "필요한 파일이나 폴더가 없습니다. 파일 검사를 실행해 주세요.",
            "LOCKED" => "파일이 사용 중입니다. 게임과 다른 런처를 닫고 다시 시도해 주세요.",
            "DISKFULL" => "저장 공간이 부족합니다. 여유 공간을 확보해 주세요.",
            "INTEGRITY" => "다운로드 파일의 크기 또는 SHA-256이 다릅니다. 다시 시도해 주세요.",
            "FORMAT" => "설정 또는 배포 정보가 올바르지 않습니다. 오류 코드를 운영자에게 전달해 주세요.",
            "CONFIG" or "PATH" => "설정 또는 설치 경로가 올바르지 않습니다.",
            "STATE" => "현재 상태에서는 작업할 수 없습니다. 게임 종료 후 새로 고침해 주세요.",
            "UNSUPPORTED" => "이 환경에서 지원하지 않는 작업입니다.",
            "PROCESS" => "프로그램을 실행하지 못했습니다. 파일 검사 후 다시 시도해 주세요.",
            "IO" => "파일 읽기 또는 쓰기에 실패했습니다. 설치 경로를 확인해 주세요.",
            _ => "예상하지 못한 오류입니다. 오류 코드와 진단 ID를 전달해 주세요."
        };
        if (stage == "RUNTIME" && cause is "PROCESS" or "STATE" or "UNSUPPORTED")
            message = "게임 실행 환경(.NET Desktop Runtime) 설치에 실패했습니다. 관리자 권한 요청을 허용했는지 확인하고 다시 시도해 주세요.";
        if (stage == "ROLLBACK") message = "이전 파일 복원에 실패했습니다. 설치 폴더의 백업을 보존했으니 운영자에게 문의해 주세요.";
        return new LauncherFailure($"BRL-{component}-{stage}-{cause}", Guid.NewGuid().ToString("N")[..12], message);
    }

    public static async Task<LauncherFailure> ReportAsync(Exception exception, string component, string stage,
        FileLauncherLog? log = null)
    {
        var failure = Describe(exception, component, stage);
        try
        {
            log ??= new FileLauncherLog(Path.Combine(LauncherPaths.LogsRoot, component == "B" ? "bootstrap.log" : "launcher.log"));
            await log.ErrorAsync($"{failure.Code} id={failure.IncidentId} env={LauncherPaths.EnvironmentKey}", exception).ConfigureAwait(false);
        }
        catch (Exception loggingFailure)
        {
            var logFailure = Describe(loggingFailure, component, "LOG");
            failure = failure with { Message = failure.Message + $"\n로그 저장 실패: {logFailure.Code}" };
            // Never let a second logging exception replace the original user-facing diagnostic.
            try
            {
                var fallback = Path.Combine(Path.GetTempPath(), "BettingRoyale-launcher-errors.log");
                await File.AppendAllTextAsync(fallback,
                    $"{DateTimeOffset.Now:O} {failure.Code} id={failure.IncidentId}\n{exception}\nLOG: {loggingFailure}\n").ConfigureAwait(false);
            }
            catch { /* The code and incident ID remain visible even if no disk can be written. */ }
        }
        return failure;
    }
}
