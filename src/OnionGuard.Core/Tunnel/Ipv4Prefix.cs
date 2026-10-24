using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace OnionGuard.Core.Tunnel;

/// <summary>Adresse IPv4 et longueur de préfixe, par exemple 128.0.0.0/1.</summary>
public readonly record struct Ipv4Prefix
{
    public Ipv4Prefix(IPAddress address, int length)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Adresse IPv4 attendue.", nameof(address));
        }
        if (length is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Longueur de préfixe entre 0 et 32.");
        }
        Address = address;
        Length = length;
    }

    public IPAddress Address { get; }

    public int Length { get; }

    /// <summary>Adresse du réseau : les bits d'hôte à zéro.</summary>
    public IPAddress Network => FromUInt32(ToUInt32(Address) & Mask);

    /// <summary>Adresse de diffusion : les bits d'hôte à un.</summary>
    public IPAddress Broadcast => FromUInt32(ToUInt32(Address) | ~Mask);

    public bool Contains(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork
        && (ToUInt32(address) & Mask) == (ToUInt32(Address) & Mask);

    /// <summary>Lit « a.b.c.d/n ». Toute autre forme lève <see cref="FormatException"/>.</summary>
    public static Ipv4Prefix Parse(string text)
    {
        var parts = text.Split('/');
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var address)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var length))
        {
            throw new FormatException($"Préfixe IPv4 invalide : {text}");
        }
        try
        {
            return new Ipv4Prefix(address, length);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Préfixe IPv4 invalide : {text}", ex);
        }
    }

    public override string ToString() => $"{Address}/{Length}";

    private uint Mask => Length == 0 ? 0 : uint.MaxValue << (32 - Length);

    private static uint ToUInt32(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

    private static IPAddress FromUInt32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }
}
