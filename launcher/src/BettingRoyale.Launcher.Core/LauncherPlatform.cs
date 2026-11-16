using System.Diagnostics;

namespace BettingRoyale.Launcher.Core;

/// <summary>
/// 운영체제마다 다른 설치 위치·파일 권한·게임 실행·실행 중 판정을 한곳에 모은다(S15P21A606-312).
/// Windows 분기는 이 클래스가 생기기 전의 동작을 그대로 옮긴 것이다.
/// </summary>
public static class LauncherPlatform
{
    public const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute; // 0755

    public const UnixFileMode RegularMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead; // 0644

    /// <summary>
    /// 사용자별 앱 데이터 루트. Windows 는 <c>%LOCALAPPDATA%</c>, macOS 는 <c>~/Library/Application Support</c> 다.
    /// </summary>
    public static string ApplicationDataRoot => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>제품 루트 아래 게임 폴더 이름. macOS 는 계약상 소문자 <c>game</c> 이다.</summary>
    public static string GameDirectoryName => OperatingSystem.IsMacOS() ? "game" : "Game";

    /// <summary>Unix 에서 manifest 의 executable 표시대로 0755/0644 를 준다. Windows 에서는 아무것도 하지 않는다.</summary>
    public static void ApplyFileMode(string path, bool executable)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, executable ? ExecutableMode : RegularMode);
    }

    /// <summary>Unix 에서 소유자 실행 권한이 있는지. Windows 는 항상 true 다.</summary>
    public static bool HasExecutePermission(string path)
    {
        if (OperatingSystem.IsWindows()) return true;
        return (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
    }

    /// <summary>
    /// 게임을 시작한다. Windows 는 기존처럼 ShellExecute 로, macOS 는 <c>.app</c> 안의 Mach-O 를 직접 실행한다 —
    /// <c>open</c> 은 같은 번들 ID 의 다른 사본이 떠 있으면 그쪽을 앞으로 가져올 뿐이라 실행 중 판정이 어긋난다.
    /// </summary>
    public static void StartGame(string executablePath, string installRoot)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = installRoot,
                UseShellExecute = true,
            });
            return;
        }

        if (!HasExecutePermission(executablePath))
            throw new UnauthorizedAccessException($"게임 실행 파일에 실행 권한이 없습니다: {executablePath}");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = installRoot,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("게임 프로세스를 시작하지 못했습니다.");
    }

    /// <summary>
    /// 이 설치 루트의 실행 파일이 실행 중인지. 같은 이름의 다른 설치본은 무시한다.
    /// 권한 때문에 경로를 읽을 수 없는 같은 이름 프로세스는 안전하게 실행 중으로 본다.
    /// </summary>
    public static bool IsRunning(string executablePath)
    {
        var name = Path.GetFileNameWithoutExtension(executablePath);
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    if (SamePath(process.MainModule?.FileName, executablePath)) return true;
                }
                catch (InvalidOperationException) { /* Process exited during the check. */ }
                catch (System.ComponentModel.Win32Exception) { return true; }
            }
        }

        return false;
    }

    private static bool SamePath(string? processPath, string target)
    {
        if (processPath is null) return false;
        if (string.Equals(processPath, target, StringComparison.OrdinalIgnoreCase)) return true;
        // macOS 는 /tmp·/var 가 /private 아래의 심볼릭 링크라 프로세스 경로가 실경로로 나온다.
        const string privatePrefix = "/private";
        return OperatingSystem.IsMacOS() &&
               processPath.StartsWith(privatePrefix + "/", StringComparison.Ordinal) &&
               string.Equals(processPath[privatePrefix.Length..], target, StringComparison.OrdinalIgnoreCase);
    }
}
