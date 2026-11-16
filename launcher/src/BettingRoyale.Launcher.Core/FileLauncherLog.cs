namespace BettingRoyale.Launcher.Core;

public sealed class FileLauncherLog
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;

    public FileLauncherLog(string path)
    {
        this.path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public async Task InfoAsync(string message) => await WriteAsync("INFO", message).ConfigureAwait(false);

    public async Task ErrorAsync(string message, Exception exception) =>
        await WriteAsync("ERROR", $"{message} | {exception}").ConfigureAwait(false);

    private async Task WriteAsync(string level, string message)
    {
        var safeMessage = message.Replace('\r', ' ').Replace('\n', ' ');
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(
                path,
                $"{DateTimeOffset.Now:O} [{level}] {safeMessage}{Environment.NewLine}").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LauncherDiagnostics.At(exception, "LOG");
            throw;
        }
        finally
        {
            gate.Release();
        }
    }
}
