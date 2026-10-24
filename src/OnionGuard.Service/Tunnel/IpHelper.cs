using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using OnionGuard.Core.Tunnel;

namespace OnionGuard.Service.Tunnel;

/// <summary>
/// Adresses et routes IPv4 par l'API réseau de Windows (iphlpapi.dll), sans netsh ni
/// PowerShell : pas de texte à analyser, pas de dépendance à la langue du système.
/// </summary>
internal static partial class IpHelper
{
    private const string Library = "iphlpapi.dll";
    private const ushort AfInet = 2;
    private const int IpDadStatePreferred = 4;
    private const uint ErrorNotFound = 1168;
    private const uint ErrorObjectAlreadyExists = 5010;

    /// <summary>Identifiant de la carte (LUID) d'après son nom, par exemple « OnionGuard ».</summary>
    public static ulong GetInterfaceLuid(string alias)
    {
        Check(ConvertInterfaceAliasToLuid(alias, out var luid), $"carte {alias} introuvable");
        return luid;
    }

    public static void AddAddress(ulong interfaceLuid, Ipv4Prefix address)
    {
        var row = UnicastRow(interfaceLuid, address);
        var status = CreateUnicastIpAddressEntry(in row);
        if (status != ErrorObjectAlreadyExists)
        {
            Check(status, $"ajout de l'adresse {address}");
        }
    }

    /// <summary>Retire l'adresse. Rien à faire si elle n'existe plus.</summary>
    public static void DeleteAddress(ulong interfaceLuid, Ipv4Prefix address)
    {
        var row = UnicastRow(interfaceLuid, address);
        var status = DeleteUnicastIpAddressEntry(in row);
        if (status != ErrorNotFound)
        {
            Check(status, $"retrait de l'adresse {address}");
        }
    }

    /// <summary>Route vers une passerelle, ou sans passerelle (null) : les paquets sont remis directement à la carte.</summary>
    public static void AddRoute(ulong interfaceLuid, Ipv4Prefix destination, uint metric, IPAddress? nextHop = null)
    {
        var row = RouteRow(interfaceLuid, destination, metric, nextHop);
        var status = CreateIpForwardEntry2(in row);
        if (status != ErrorObjectAlreadyExists)
        {
            Check(status, $"ajout de la route {destination}");
        }
    }

    /// <summary>Retire la route. Rien à faire si elle n'existe plus.</summary>
    public static void DeleteRoute(ulong interfaceLuid, Ipv4Prefix destination, uint metric, IPAddress? nextHop = null)
    {
        var row = RouteRow(interfaceLuid, destination, metric, nextHop);
        var status = DeleteIpForwardEntry2(in row);
        if (status != ErrorNotFound)
        {
            Check(status, $"retrait de la route {destination}");
        }
    }

    /// <summary>
    /// Carte et adresse source que Windows choisirait pour joindre <paramref name="destination"/>.
    /// Simple consultation de la table de routage : aucun paquet n'est envoyé.
    /// </summary>
    public static (ulong InterfaceLuid, uint InterfaceIndex, IPAddress SourceAddress, IPAddress NextHop) GetBestRoute(IPAddress destination)
    {
        var target = SockAddrInet.FromIpv4(destination);
        Check(GetBestRoute2(0, 0, 0, in target, 0, out var route, out var source), $"recherche d'une route vers {destination}");
        return (route.InterfaceLuid, route.InterfaceIndex, source.ToIpv4(), route.NextHop.ToIpv4());
    }

    /// <summary>Adresse source de la carte <paramref name="interfaceLuid"/> vers <paramref name="destination"/>, ses routes seules.</summary>
    public static IPAddress GetSourceAddress(ulong interfaceLuid, IPAddress destination)
    {
        var target = SockAddrInet.FromIpv4(destination);
        Check(
            GetBestRoute2OnInterface(in interfaceLuid, 0, 0, in target, 0, out _, out var source),
            $"recherche d'une route vers {destination}");
        return source.ToIpv4();
    }

    private static MibUnicastIpAddressRow UnicastRow(ulong interfaceLuid, Ipv4Prefix address)
    {
        InitializeUnicastIpAddressEntry(out var row);
        row.Address = SockAddrInet.FromIpv4(address.Address);
        row.InterfaceLuid = interfaceLuid;
        row.OnLinkPrefixLength = (byte)address.Length;
        // Pas de détection d'adresse en double sur une carte point à point.
        row.DadState = IpDadStatePreferred;
        return row;
    }

    private static MibIpForwardRow2 RouteRow(ulong interfaceLuid, Ipv4Prefix destination, uint metric, IPAddress? nextHop = null)
    {
        InitializeIpForwardEntry(out var row);
        row.InterfaceLuid = interfaceLuid;
        row.DestinationPrefix = SockAddrInet.FromIpv4(destination.Address);
        row.DestinationPrefixLength = (byte)destination.Length;
        row.NextHop = SockAddrInet.FromIpv4(nextHop ?? IPAddress.Any);
        row.Metric = metric;
        return row;
    }

    private static void Check(uint status, string operation)
    {
        if (status != 0)
        {
            throw new Win32Exception((int)status, $"Échec : {operation} ({new Win32Exception((int)status).Message}).");
        }
    }

    // Dispositions mémoire de netioapi.h (Windows x64 et x86), décalages en octets.

    [StructLayout(LayoutKind.Explicit, Size = 28)]
    internal struct SockAddrInet
    {
        [FieldOffset(0)] public ushort Family;
        [FieldOffset(2)] public ushort Port;
        [FieldOffset(4)] public uint Ipv4;

        public static SockAddrInet FromIpv4(IPAddress address)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException("Adresse IPv4 attendue.", nameof(address));
            }
            // Les octets restent dans l'ordre du réseau.
            return new SockAddrInet { Family = AfInet, Ipv4 = BitConverter.ToUInt32(address.GetAddressBytes()) };
        }

        public readonly IPAddress ToIpv4() => new(BitConverter.GetBytes(Ipv4));
    }

    [StructLayout(LayoutKind.Explicit, Size = 80)]
    internal struct MibUnicastIpAddressRow
    {
        [FieldOffset(0)] public SockAddrInet Address;
        [FieldOffset(32)] public ulong InterfaceLuid;
        [FieldOffset(40)] public uint InterfaceIndex;
        [FieldOffset(52)] public uint ValidLifetime;
        [FieldOffset(56)] public uint PreferredLifetime;
        [FieldOffset(60)] public byte OnLinkPrefixLength;
        [FieldOffset(64)] public int DadState;
    }

    [StructLayout(LayoutKind.Explicit, Size = 104)]
    internal struct MibIpForwardRow2
    {
        [FieldOffset(0)] public ulong InterfaceLuid;
        [FieldOffset(8)] public uint InterfaceIndex;
        [FieldOffset(12)] public SockAddrInet DestinationPrefix;
        [FieldOffset(40)] public byte DestinationPrefixLength;
        [FieldOffset(44)] public SockAddrInet NextHop;
        [FieldOffset(76)] public uint ValidLifetime;
        [FieldOffset(80)] public uint PreferredLifetime;
        [FieldOffset(84)] public uint Metric;
    }

    [LibraryImport(Library)]
    internal static partial void InitializeUnicastIpAddressEntry(out MibUnicastIpAddressRow row);

    [LibraryImport(Library)]
    private static partial uint CreateUnicastIpAddressEntry(in MibUnicastIpAddressRow row);

    [LibraryImport(Library)]
    private static partial uint DeleteUnicastIpAddressEntry(in MibUnicastIpAddressRow row);

    [LibraryImport(Library)]
    internal static partial void InitializeIpForwardEntry(out MibIpForwardRow2 row);

    [LibraryImport(Library)]
    private static partial uint CreateIpForwardEntry2(in MibIpForwardRow2 row);

    [LibraryImport(Library)]
    private static partial uint DeleteIpForwardEntry2(in MibIpForwardRow2 row);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint ConvertInterfaceAliasToLuid(string interfaceAlias, out ulong interfaceLuid);

    [LibraryImport(Library)]
    private static partial uint GetBestRoute2(
        nint interfaceLuid,
        uint interfaceIndex,
        nint sourceAddress,
        in SockAddrInet destinationAddress,
        uint addressSortOptions,
        out MibIpForwardRow2 bestRoute,
        out SockAddrInet bestSourceAddress);

    [LibraryImport(Library, EntryPoint = "GetBestRoute2")]
    private static partial uint GetBestRoute2OnInterface(
        in ulong interfaceLuid,
        uint interfaceIndex,
        nint sourceAddress,
        in SockAddrInet destinationAddress,
        uint addressSortOptions,
        out MibIpForwardRow2 bestRoute,
        out SockAddrInet bestSourceAddress);
}
