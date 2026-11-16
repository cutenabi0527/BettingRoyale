using System.Diagnostics;

namespace BettingRoyale.Launcher.Core;

/// <summary>macOS 런처를 응용 프로그램 폴더로 옮기는 계획. <see cref="Copy"/> 면 원본을 지우지 않는다(읽기 전용 디스크 이미지).</summary>
public sealed record MacInstallPlan(string SourceBundle, string TargetBundle, bool Copy);

/// <summary>
/// macOS 런처가 응용 프로그램 폴더 밖에서 돌 때 제자리로 옮긴다(S15P21A606-323, S-REL-FYDG67).
/// </summary>
/// <remarks>
/// <para>
/// 다운로드 폴더·바탕화면·디스크 이미지에서 바로 연 앱은 macOS 가 App Translocation 으로 읽기 전용 임시 위치에서 돌린다.
/// 그 자리에서는 런처가 자기 번들을 바꿀 수 없어 자체 갱신이 <c>BRL-B-INSTALL-ACCESS</c> 로 실패한다(2026-09-25 실사례).
/// 그래서 첫 실행에 옮길지 묻고, 옮기기는 <see cref="LauncherSelfUpdater"/> 의 교체와 같이 런처가 끝난 뒤 작은 셸
/// 스크립트가 한다 — 실행 중인 번들을 스스로 옮기지 않는다.
/// </para>
/// <para>
/// 옮긴 번들의 격리 속성(<c>com.apple.quarantine</c>)은 지운다. 남기면 코드로 옮긴 앱은 다시 번역된 위치에서 돈다
/// (Finder 로 옮긴 경우에만 macOS 가 번역을 풀어 준다). 이 앱은 공증·staple 된 우리 앱이고 사용자가 이미 한 번 열었으므로
/// Gatekeeper 확인을 건너뛰는 것이 아니다.
/// </para>
/// <para>
/// 위치 판정(<see cref="IsInstalled"/>·<see cref="IsTranslocated"/>·<see cref="Plan"/>)은 macOS 경로를 다루므로 어느 OS 에서 돌려도
/// POSIX 규칙(<c>/</c> 구분자, 드라이브 없음)을 쓴다. <see cref="Path"/> 는 돌고 있는 OS 의 규칙을 따라 Windows 에서는
/// <c>/Users/…</c> 를 <c>C:\Users\…</c> 로 바꾸고 <c>\</c> 로 잇는다 — Windows 런처 빌드의 공용 테스트가 그래서 깨졌다(Jenkins Live #39, S15P21A606-336).
/// </para>
/// </remarks>
public static class MacApplicationsInstaller
{
    public const string SystemApplications = "/Applications";

    /// <summary>번들이 이미 <c>/Applications</c> 또는 <c>~/Applications</c> 아래(하위 폴더 포함)에 있는가.</summary>
    public static bool IsInstalled(string bundlePath, string homeDirectory)
    {
        var full = FullPosixPath(bundlePath);
        return IsUnder(full, SystemApplications) || IsUnder(full, UserApplications(homeDirectory));
    }

    /// <summary>macOS 가 App Translocation 으로 옮겨 돌리고 있는 경로인가.</summary>
    public static bool IsTranslocated(string bundlePath) =>
        FullPosixPath(bundlePath).Contains("/AppTranslocation/", StringComparison.Ordinal);

    /// <summary>
    /// 원본 번들을 어디로 옮길지 정한다. <c>/Applications</c> 에 쓸 수 있으면 거기, 아니면 <c>~/Applications</c> 다.
    /// 다른 볼륨(<c>/Volumes/...</c> — 디스크 이미지·외장 디스크)에 있으면 복사하고 원본은 둔다.
    /// </summary>
    public static MacInstallPlan Plan(string originalBundle, string homeDirectory, Func<string, bool> canWrite)
    {
        var source = FullPosixPath(originalBundle);
        if (!source.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"옮길 대상은 .app 번들이어야 합니다: {originalBundle}", nameof(originalBundle));
        // 원본이 이미 제자리인데 격리 속성 때문에 번역된 위치에서 돌고 있으면(코드로 옮긴 경우) 옮기지 않고 제자리에서 정리만 한다.
        if (IsInstalled(source, homeDirectory)) return new MacInstallPlan(source, source, Copy: false);
        var directory = canWrite(SystemApplications) ? SystemApplications : UserApplications(homeDirectory);
        var copy = IsUnder(source, "/Volumes");
        return new MacInstallPlan(source, directory + "/" + source[(source.LastIndexOf('/') + 1)..], copy);
    }

    /// <summary>폴더에 파일을 만들 수 있는지 실제로 써 보고 지운다. 폴더가 없으면 false 다.</summary>
    public static bool CanWrite(string directory)
    {
        if (!Directory.Exists(directory)) return false;
        var probe = Path.Combine(directory, $".bettingroyale-write-probe-{Guid.NewGuid():N}");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string WriteMoveScript(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "install-to-applications.sh");
        File.WriteAllText(path, MoveScript.Replace("\r\n", "\n", StringComparison.Ordinal));
        LauncherPlatform.ApplyFileMode(path, executable: true);
        return path;
    }

    /// <summary>
    /// 옮기기 스크립트를 런처와 분리해 시작한다. 호출한 뒤 런처는 곧바로 종료해야 한다 — 스크립트는 PID 가 사라질 때까지
    /// 기다린다(새 위치의 런처가 단일 실행 잠금에 막히지 않게).
    /// </summary>
    public static void StartMove(string scriptPath, int launcherPid, MacInstallPlan plan, string logPath,
        string relaunchCommand = "/usr/bin/open")
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("응용 프로그램 폴더 설치는 macOS 런처 전용입니다.");
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var argument in new[]
                 {
                     scriptPath, launcherPid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     plan.SourceBundle, plan.TargetBundle, plan.Copy ? "copy" : "move", logPath, relaunchCommand,
                 })
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("응용 프로그램 폴더 설치 스크립트를 시작하지 못했습니다.");
        }
        catch (Exception exception)
        {
            throw LauncherDiagnostics.At(exception, "INSTALL");
        }
    }

    private static string UserApplications(string homeDirectory) => FullPosixPath(homeDirectory).TrimEnd('/') + "/Applications";

    /// <summary>
    /// macOS 절대 경로로 정리한다 — 빈 칸·<c>.</c> 를 빼고 <c>..</c> 를 앞 칸과 지운다. 끝의 <c>/</c> 는 남기지 않는다.
    /// 상대 경로는 실제 macOS 런처에서만 들어오므로 그때만 현재 폴더 기준(<see cref="Path.GetFullPath(string)"/>)으로 푼다.
    /// </summary>
    private static string FullPosixPath(string path)
    {
        if (!path.StartsWith('/')) path = Path.GetFullPath(path);
        var parts = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(segment);
        }
        return "/" + string.Join('/', parts);
    }

    private static bool IsUnder(string fullPath, string directory)
    {
        var root = directory.TrimEnd('/') + "/";
        return fullPath.StartsWith(root, StringComparison.Ordinal);
    }

    // POSIX sh. 런처가 끝날 때까지(최대 60초) 기다린 뒤, 같은 이름의 옛 번들은 휴지통으로 보내고 원본을 옮긴다(디스크 이미지면 복사).
    // 실패하면 원래 번들을 다시 연다 — 사용자는 옮기기 전과 같은 상태로 계속한다.
    private const string MoveScript = """
        #!/bin/sh
        # BettingRoyale macOS launcher install-to-Applications (S15P21A606-323). Generated by MacApplicationsInstaller.
        pid="$1"; source="$2"; target="$3"; mode="$4"; log="$5"; relaunch="$6"
        note() { printf '%s [%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$1" "$2" >> "$log" 2>/dev/null; }
        waited=0
        while kill -0 "$pid" 2>/dev/null; do
          waited=$((waited + 1))
          if [ "$waited" -gt 600 ]; then note ERROR "BRL-B-INSTALL-STATE launcher did not exit"; exit 1; fi
          sleep 0.1
        done
        if [ "$source" = "$target" ]; then
          mode=inplace
        elif ! mkdir -p "$(dirname "$target")" 2>>"$log"; then
          note ERROR "BRL-B-INSTALL-ACCESS cannot create $(dirname "$target")"
          "$relaunch" "$source"; exit 1
        fi
        if [ "$mode" != inplace ] && [ -e "$target" ]; then
          trash="$HOME/.Trash/$(basename "$target" .app) $(date +%Y%m%d-%H%M%S).app"
          mkdir -p "$HOME/.Trash" 2>/dev/null
          if ! mv "$target" "$trash" 2>>"$log"; then
            note ERROR "BRL-B-INSTALL-ACCESS cannot move existing bundle to Trash: $target"
            "$relaunch" "$source"; exit 1
          fi
          note INFO "previous bundle moved to Trash: $trash"
        fi
        if [ "$mode" = inplace ]; then
          :
        elif [ "$mode" = copy ]; then
          if ! /usr/bin/ditto "$source" "$target" 2>>"$log"; then
            rm -rf "$target"
            note ERROR "BRL-B-INSTALL-IO cannot copy bundle to $target"
            "$relaunch" "$source"; exit 1
          fi
        elif ! mv "$source" "$target" 2>>"$log"; then
          note ERROR "BRL-B-INSTALL-ACCESS cannot move bundle to $target"
          "$relaunch" "$source"; exit 1
        fi
        if command -v xattr >/dev/null 2>&1; then
          xattr -dr com.apple.quarantine "$target" 2>/dev/null || note WARN "could not clear quarantine: $target"
        fi
        note INFO "launcher installed to Applications: $target ($mode)"
        "$relaunch" "$target"
        """;
}
