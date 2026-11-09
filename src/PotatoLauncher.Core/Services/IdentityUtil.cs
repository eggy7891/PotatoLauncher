using System.Security.Cryptography;
using System.Text;

namespace PotatoLauncher.Core.Services;

public static class IdentityUtil
{
    public static string OfflineUuid(string name)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(
            BitConverter.ToInt32(bytes, 0),
            BitConverter.ToInt16(bytes, 4),
            BitConverter.ToInt16(bytes, 6),
            bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15])
            .ToString("N");
    }
}
