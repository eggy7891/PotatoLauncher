using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.App.Services;

public sealed class TextureFileService
{
    private const long MaxBytes = 8 * 1024 * 1024;
    private readonly AppPaths _paths;

    public TextureFileService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<string> ImportAsync(string sourcePath, bool isCape, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("纹理文件不存在。", sourcePath);
        }
        if (info.Length > MaxBytes)
        {
            throw new InvalidDataException("图片不得超过 8 MB。");
        }

        var header = new byte[8];
        await using (var stream = File.OpenRead(sourcePath))
        {
            if (await stream.ReadAsync(header, cancellationToken) != header.Length ||
                !header.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            {
                throw new InvalidDataException("只支持 PNG 纹理文件。");
            }
        }

        using var imageStream = File.OpenRead(sourcePath);
        var decoder = BitmapDecoder.Create(
            imageStream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var validDimensions = isCape
            ? frame.PixelWidth == 64 && frame.PixelHeight == 32 ||
              frame.PixelWidth == 22 && frame.PixelHeight == 17
            : frame.PixelWidth == 64 && frame.PixelHeight is 32 or 64;
        if (!validDimensions)
        {
            throw new InvalidDataException(isCape
                ? "披风必须为 64×32 或 22×17 PNG。"
                : "皮肤必须为 64×64 或 64×32 PNG。");
        }

        var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var directory = Path.Combine(_paths.CacheDirectory, "textures");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, hash + ".png");
        if (!File.Exists(destination))
        {
            await File.WriteAllBytesAsync(destination, bytes, cancellationToken);
        }
        return destination;
    }
}
