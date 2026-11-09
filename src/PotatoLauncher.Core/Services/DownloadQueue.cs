using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.Core.Services;

public interface IDownloadQueue
{
    Task DownloadAsync(
        DownloadPlan plan,
        int concurrency,
        Func<MirrorDecision, Task<bool>> approveMirror,
        IProgress<DownloadProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default);
}

public sealed class DownloadQueue : IDownloadQueue
{
    private readonly HttpClient _httpClient;

    public DownloadQueue(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PotatoLauncher/1.0.1 (+https://github.com/eggy7891/PotatoLauncher)");
    }

    public async Task DownloadAsync(
        DownloadPlan plan,
        int concurrency,
        Func<MirrorDecision, Task<bool>> approveMirror,
        IProgress<DownloadProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var uniqueItems = plan.Items
            .GroupBy(item => Path.GetFullPath(item.DestinationPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var completed = 0;
        long bytesDownloaded = 0;
        var totalBytes = uniqueItems.Where(item => item.Size is > 0).Sum(item => item.Size ?? 0);
        var failures = new ConcurrentQueue<Exception>();
        var mirrorDecisions = new ConcurrentDictionary<string, Lazy<Task<bool>>>(StringComparer.OrdinalIgnoreCase);
        Task<bool> ApproveOnce(MirrorDecision decision)
        {
            return mirrorDecisions.GetOrAdd(
                "mirror",
                _ => new Lazy<Task<bool>>(() => approveMirror(decision))).Value;
        }
        using var gate = new SemaphoreSlim(Math.Clamp(concurrency, 1, 64));

        var tasks = uniqueItems.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var count = Interlocked.Increment(ref completed);
                progress?.Report(new DownloadProgress(
                    item.Name,
                    count,
                    uniqueItems.Length,
                    Interlocked.Read(ref bytesDownloaded),
                    totalBytes > 0 ? totalBytes : null,
                    uniqueItems.Length == 0 ? 100 : count * 100d / uniqueItems.Length));
                log?.Invoke($"下载 {item.Name}");
                var downloaded = await DownloadItemAsync(item, ApproveOnce, log, cancellationToken).ConfigureAwait(false);
                Interlocked.Add(ref bytesDownloaded, downloaded);
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        if (!failures.IsEmpty)
        {
            var message = string.Join(Environment.NewLine, failures.Select(error => error.Message).Distinct());
            throw new DownloadFailureException($"有 {failures.Count} 个文件下载失败：{Environment.NewLine}{message}");
        }

        progress?.Report(new DownloadProgress("下载完成", uniqueItems.Length, uniqueItems.Length,
            Interlocked.Read(ref bytesDownloaded), totalBytes > 0 ? totalBytes : null, 100));
    }

    private async Task<long> DownloadItemAsync(
        DownloadItem item,
        Func<MirrorDecision, Task<bool>> approveMirror,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(item.DestinationPath))!);
        if (await IsValidExistingFileAsync(item, cancellationToken).ConfigureAwait(false))
        {
            log?.Invoke($"已存在且校验通过：{item.Name}");
            return item.Size ?? new FileInfo(item.DestinationPath).Length;
        }

        Exception? finalError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await DownloadFromUriAsync(item, item.PrimaryUri, log, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < 3)
            {
                finalError = ex;
                log?.Invoke($"下载失败，第 {attempt} 次重试：{item.Name}（{ex.Message}）");
                await Task.Delay(TimeSpan.FromMilliseconds(350 * Math.Pow(2, attempt - 1)), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                finalError = ex;
                break;
            }
        }

        if (item.MirrorUri is not null)
        {
            var approved = await approveMirror(new MirrorDecision
            {
                Url = item.MirrorUri.ToString(),
                Reason = finalError?.Message ?? "官方源不可用"
            }).ConfigureAwait(false);
            if (approved)
            {
                log?.Invoke($"改用镜像：{item.Name}");
                return await DownloadFromUriAsync(item, item.MirrorUri, log, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new DownloadFailureException($"下载失败：{item.Name}（{finalError?.Message ?? "未知错误"}）", finalError);
    }

    private async Task<long> DownloadFromUriAsync(
        DownloadItem item,
        Uri uri,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var destination = Path.GetFullPath(item.DestinationPath);
        var partial = destination + ".part";
        var existingLength = File.Exists(partial) ? new FileInfo(partial).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existingLength > 0)
        {
            File.Delete(partial);
            existingLength = 0;
            return await DownloadFromUriAsync(item, uri, log, cancellationToken).ConfigureAwait(false);
        }

        response.EnsureSuccessStatusCode();
        var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = new FileStream(
                         partial,
                         append ? FileMode.Append : FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         128 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await input.CopyToAsync(output, 128 * 1024, cancellationToken).ConfigureAwait(false);
        }

        if (!await ValidateHashAsync(partial, item, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(partial);
            throw new InvalidDataException($"哈希校验失败：{item.Name}");
        }

        File.Move(partial, destination, overwrite: true);
        log?.Invoke($"完成：{item.Name}");
        return new FileInfo(destination).Length;
    }

    private static async Task<bool> IsValidExistingFileAsync(DownloadItem item, CancellationToken cancellationToken)
    {
        var destination = Path.GetFullPath(item.DestinationPath);
        if (!File.Exists(destination) || File.Exists(destination + ".part"))
        {
            return false;
        }

        if (item.Size is > 0 && new FileInfo(destination).Length != item.Size)
        {
            return false;
        }

        return await ValidateHashAsync(destination, item, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ValidateHashAsync(
        string path,
        DownloadItem item,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.Sha1) &&
            string.IsNullOrWhiteSpace(item.Sha256) &&
            string.IsNullOrWhiteSpace(item.Sha512))
        {
            return true;
        }

        await using var stream = File.OpenRead(path);
        byte[] actual;
        string expected;
        if (!string.IsNullOrWhiteSpace(item.Sha512))
        {
            actual = await SHA512.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            expected = item.Sha512;
        }
        else if (!string.IsNullOrWhiteSpace(item.Sha256))
        {
            actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            expected = item.Sha256;
        }
        else
        {
            actual = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            expected = item.Sha1!;
        }

        return Convert.ToHexString(actual).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRetryable(Exception exception) =>
        exception is HttpRequestException or IOException or TaskCanceledException ||
        exception is InvalidDataException;
}
