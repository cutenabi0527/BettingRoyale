using System.Text.Json;
using BettingRoyale.Launcher.Core;

namespace BettingRoyale.ManifestTool;

internal static class Program
{
    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdb",
        ".xml",
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Arguments.Parse(args);
            var root = Path.GetFullPath(options.Required("root"));
            var output = Path.GetFullPath(options.Required("output"));
            var platform = options.Optional("platform");
            if (platform is not null && !ReleasePlatform.IsKnown(platform))
            {
                throw new ArgumentException($"--platform 은 {ReleasePlatform.WindowsX64} 또는 {ReleasePlatform.MacOSArm64} 여야 합니다: {platform}");
            }

            var effectivePlatform = platform ?? ReleasePlatform.WindowsX64;
            var executable = SafePath.NormalizeRelative(options.Required("executable"));
            // --executable-glob(여러 번)은 Unix 실행 권한(0755)을 줄 파일 패턴이다(S15P21A606-312, Mac 계약 7절).
            // macOS manifest 는 최상위 실행 파일과 기본 규칙(*.app/Contents/MacOS/*)도 늘 실행 파일로 표시한다.
            var executablePatterns = options.All("executable-glob").Select(ExecutablePattern.Normalize).ToList();
            if (ReleasePlatform.UsesUnixFileModes(effectivePlatform))
            {
                executablePatterns.Add(executable);
                executablePatterns.Add(ExecutablePattern.MacBundleDefault);
            }
            else if (executablePatterns.Count > 0)
            {
                Console.Error.WriteLine($"경고: {effectivePlatform} manifest 는 파일 권한을 쓰지 않으므로 --executable-glob 을 무시합니다.");
            }

            var urlPrefix = options.Required("url-prefix").Trim('/');
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException($"입력 디렉터리가 없습니다: {root}");
            }

            var files = new List<ReleaseFile>();
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = SafePath.NormalizeRelative(Path.GetRelativePath(root, path));
                // .app 번들 안은 전부 코드 서명 봉인 대상이라 아무것도 빼지 않는다 — 하나라도 빠지면 설치본의 봉인이
                // 깨져 Gatekeeper 가 "손상됨" 으로 막는다(S15P21A606-318: .xml 규칙이 config.xml·link.xml 을 뺐다).
                // Unity 디버그 폴더는 먼저 뺀다 — 그 안에 번들 모양(`..._DoNotShip/BettingRoyale.app/...`)이 다시 있어서
                // 아래 번들 판정에 걸리면 게시물에 섞인다(Jenkins 빌드 217, S15P21A606-321). 서명 번들 안에는 이 폴더가 없다.
                var inDebugFolder = relative.Split('/').SkipLast(1).Any(part =>
                    part.EndsWith("_BackUpThisFolder_ButDontShipItWithYourGame", StringComparison.OrdinalIgnoreCase) ||
                    part.EndsWith("_BurstDebugInformation_DoNotShip", StringComparison.OrdinalIgnoreCase));
                var insideAppBundle = relative.Split('/').SkipLast(1)
                    .Any(part => part.EndsWith(".app", StringComparison.OrdinalIgnoreCase));
                if (inDebugFolder || (!insideAppBundle && (ExcludedExtensions.Contains(Path.GetExtension(relative)) ||
                    Path.GetFileName(relative).Equals("build.log", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(".bettingroyale-", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(relative).Equals(".DS_Store", StringComparison.Ordinal))))
                {
                    continue;
                }

                var urlPath = string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));
                var info = new FileInfo(path);
                files.Add(new ReleaseFile
                {
                    Path = relative,
                    Url = $"{urlPrefix}/{urlPath}",
                    Size = info.Length,
                    Sha256 = await FileIntegrity.ComputeSha256Async(path).ConfigureAwait(false),
                    // Windows manifest 에는 쓰지 않는다 — 기존 manifest 모양을 그대로 둔다.
                    Executable = ReleasePlatform.UsesUnixFileModes(effectivePlatform) &&
                                 executablePatterns.Any(pattern => ExecutablePattern.Matches(pattern, relative))
                        ? true
                        : null,
                });
            }

            var notes = options.Optional("notes-file") is { } notesFile && File.Exists(notesFile)
                ? File.ReadAllLines(notesFile).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToArray()
                : Array.Empty<string>();
            RuntimeRequirement? runtime = null;
            if (options.Optional("runtime-file") is { Length: > 0 } runtimeFile)
            {
                // 런타임 설치기는 release 파일 목록 밖에 있다. 게시기가 launcher/runtime/ 에 따로 놓는다 (D-REL-RW5QWY).
                var runtimeInfo = new FileInfo(runtimeFile);
                if (!runtimeInfo.Exists) throw new FileNotFoundException("런타임 설치 파일이 없습니다.", runtimeFile);
                runtime = new RuntimeRequirement
                {
                    Version = options.Required("runtime-version"),
                    Url = options.Required("runtime-url").Trim('/'),
                    Size = runtimeInfo.Length,
                    Sha256 = await FileIntegrity.ComputeSha256Async(runtimeInfo.FullName).ConfigureAwait(false),
                };
                if (!string.Equals(runtime.AsReleaseFile().Path, runtimeInfo.Name, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("--runtime-url 의 파일 이름이 --runtime-file 과 다릅니다.");
            }

            var manifest = new ReleaseManifest
            {
                Runtime = runtime,
                Platform = platform,
                Product = options.Required("product"),
                Version = options.Required("version"),
                Executable = executable,
                Channel = options.Optional("channel") ?? "direct",
                MinimumBootstrapVersion = options.Optional("minimum-bootstrap-version"),
                MinimumLauncherVersion = options.Optional("minimum-launcher-version"),
                PublishedAt = DateTimeOffset.UtcNow,
                PatchNotes = notes,
                Files = files,
            };
            manifest.Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var temporary = output + ".tmp";
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(manifest, LauncherJson.Options)).ConfigureAwait(false);
            File.Move(temporary, output, true);
            Console.WriteLine($"manifest 생성: {output}");
            Console.WriteLine($"플랫폼={manifest.EffectivePlatform} 버전={manifest.Version} 파일={manifest.Files.Count} 크기={manifest.TotalSize}"
                + (ReleasePlatform.UsesUnixFileModes(manifest.EffectivePlatform)
                    ? $" 실행권한={manifest.Files.Count(file => file.Executable == true)}"
                    : string.Empty)
                + (manifest.Runtime is null ? string.Empty : $" 런타임={manifest.Runtime.Name} {manifest.Runtime.Version} ({manifest.Runtime.Size} bytes)"));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"manifest 생성 실패: {exception.Message}");
            return 1;
        }
    }

    private sealed class Arguments
    {
        private static readonly HashSet<string> Repeatable = new(StringComparer.OrdinalIgnoreCase) { "executable-glob" };
        private readonly Dictionary<string, List<string>> values = new(StringComparer.OrdinalIgnoreCase);

        public static Arguments Parse(string[] args)
        {
            var result = new Arguments();
            for (var index = 0; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("인자는 --name value 쌍이어야 합니다.");
                }

                var name = args[index][2..];
                if (!result.values.TryGetValue(name, out var list))
                {
                    result.values[name] = list = [];
                }
                else if (!Repeatable.Contains(name))
                {
                    // 기존처럼 마지막 값이 이긴다. 여러 번 줄 수 있는 인자만 누적한다.
                    list.Clear();
                }

                list.Add(args[index + 1]);
            }

            return result;
        }

        public string Required(string name) => Optional(name) is { Length: > 0 } value
            ? value
            : throw new ArgumentException($"필수 인자가 없습니다: --{name}");

        public string? Optional(string name) => values.TryGetValue(name, out var list) ? list[^1] : null;

        public IReadOnlyList<string> All(string name) =>
            values.TryGetValue(name, out var list) ? list.Where(value => value.Length > 0).ToArray() : [];
    }
}
