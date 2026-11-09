using System.Security.Cryptography;
using System.Text;

namespace PotatoLauncher.Core.Services;

public static class IdentityUtil
{
    public static string OfflineUuid(string name)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes).ToString("N");
    }
}
