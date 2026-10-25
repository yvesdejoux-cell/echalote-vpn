using System.ComponentModel;
using System.Runtime.InteropServices;

namespace EchaloteVpn.Service.Tor;

/// <summary>
/// Chemin court Windows (8.3) : Tor coupe la commande d'un transport aux espaces, ce qui casse
/// un chemin comme « C:\Program Files\… ».
/// </summary>
internal static partial class ShortPath
{
    public static string Of(string path)
    {
        if (!path.Any(char.IsWhiteSpace))
        {
            return path;
        }
        var buffer = new char[1024];
        var length = GetShortPathNameW(path, buffer, (uint)buffer.Length);
        if (length == 0 || length >= buffer.Length)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Chemin court introuvable pour {path}.");
        }
        return new string(buffer, 0, (int)length);
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetShortPathNameW(string longPath, [Out] char[] shortPath, uint bufferLength);
}
