using System.Runtime.InteropServices;

namespace EchaloteVpn.Service.Firewall;

/// <summary>
/// Appels au moteur de filtrage de Windows (WFP, fwpuclnt.dll). Dispositions mémoire de
/// fwptypes.h et fwpmtypes.h pour Windows x64 seulement, décalages en octets.
/// </summary>
internal static unsafe partial class WfpNative
{
    private const string Library = "fwpuclnt.dll";

    public const uint RpcCAuthnWinNt = 10;

    public const uint FwpEFilterNotFound = 0x80320003;
    public const uint FwpEProviderNotFound = 0x80320005;
    public const uint FwpESubLayerNotFound = 0x80320007;

    public const uint ProviderFlagPersistent = 0x1;
    public const uint SubLayerFlagPersistent = 0x1;
    public const uint FilterFlagPersistent = 0x1;
    public const uint FilterFlagClearActionRight = 0x8;

    public const uint ActionBlock = 0x1001;
    public const uint ActionPermit = 0x1002;

    public const uint MatchEqual = 0;
    public const uint MatchFlagsAllSet = 6;

    public const uint ConditionFlagIsLoopback = 0x1;

    // FWP_DATA_TYPE
    public const uint TypeUInt8 = 1;
    public const uint TypeUInt16 = 2;
    public const uint TypeUInt32 = 3;
    public const uint TypeByteBlob = 12;
    public const uint TypeV4AddrMask = 0x100;
    public const uint TypeV6AddrMask = 0x101;

    // Couches ALE (fwpmu.h)
    public static readonly Guid LayerAleAuthConnectV4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
    public static readonly Guid LayerAleAuthConnectV6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    public static readonly Guid LayerAleAuthRecvAcceptV4 = new("e1cd9fe7-f4b5-4273-96c0-592e487b8650");
    public static readonly Guid LayerAleAuthRecvAcceptV6 = new("a3b42c97-9f04-4672-b87e-cee9c483257f");

    // Champs des conditions (fwpmu.h)
    public static readonly Guid ConditionFlags = new("632ce23b-5167-435c-86d7-e903684aa80c");
    public static readonly Guid ConditionAleAppId = new("d78e1e87-8644-4ea5-9437-d809ecefc971");
    public static readonly Guid ConditionIpLocalAddress = new("d9ee00de-c1ef-4617-bfe3-ffd8f5a08957");
    public static readonly Guid ConditionIpRemoteAddress = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
    public static readonly Guid ConditionIpProtocol = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");
    public static readonly Guid ConditionIpLocalPort = new("0c1ba1af-5765-453f-af22-a8f791ac775b");
    public static readonly Guid ConditionIpRemotePort = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct ByteBlob
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(8)] public byte* Data;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct DisplayData
    {
        [FieldOffset(0)] public char* Name;
        [FieldOffset(8)] public char* Description;
    }

    /// <summary>FWP_VALUE0 et FWP_CONDITION_VALUE0 : un type, puis une valeur ou un pointeur.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct Value
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public byte UInt8;
        [FieldOffset(8)] public ushort UInt16;
        [FieldOffset(8)] public uint UInt32;
        [FieldOffset(8)] public void* Pointer;
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct V4AddrAndMask
    {
        [FieldOffset(0)] public uint Address;
        [FieldOffset(4)] public uint Mask;
    }

    [StructLayout(LayoutKind.Sequential, Size = 17)]
    public struct V6AddrAndMask
    {
        public fixed byte Address[16];
        public byte PrefixLength;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct FilterCondition
    {
        [FieldOffset(0)] public Guid FieldKey;
        [FieldOffset(16)] public uint MatchType;
        [FieldOffset(24)] public Value ConditionValue;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct Provider
    {
        [FieldOffset(0)] public Guid ProviderKey;
        [FieldOffset(16)] public DisplayData DisplayData;
        [FieldOffset(32)] public uint Flags;
        [FieldOffset(40)] public ByteBlob ProviderData;
        [FieldOffset(56)] public char* ServiceName;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct SubLayer
    {
        [FieldOffset(0)] public Guid SubLayerKey;
        [FieldOffset(16)] public DisplayData DisplayData;
        [FieldOffset(32)] public uint Flags;
        [FieldOffset(40)] public Guid* ProviderKey;
        [FieldOffset(48)] public ByteBlob ProviderData;
        [FieldOffset(64)] public ushort Weight;
    }

    [StructLayout(LayoutKind.Explicit, Size = 200)]
    public struct Filter
    {
        [FieldOffset(0)] public Guid FilterKey;
        [FieldOffset(16)] public DisplayData DisplayData;
        [FieldOffset(32)] public uint Flags;
        [FieldOffset(40)] public Guid* ProviderKey;
        [FieldOffset(48)] public ByteBlob ProviderData;
        [FieldOffset(64)] public Guid LayerKey;
        [FieldOffset(80)] public Guid SubLayerKey;
        [FieldOffset(96)] public Value Weight;
        [FieldOffset(112)] public uint NumFilterConditions;
        [FieldOffset(120)] public FilterCondition* FilterConditions;
        [FieldOffset(128)] public uint ActionType;
        [FieldOffset(132)] public Guid ActionFilterType;
        [FieldOffset(152)] public ulong RawContext;
        [FieldOffset(168)] public Guid* Reserved;
        [FieldOffset(176)] public ulong FilterId;
        [FieldOffset(184)] public Value EffectiveWeight;
    }

    [LibraryImport(Library)]
    public static partial uint FwpmEngineOpen0(char* serverName, uint authnService, void* authIdentity, void* session, out nint engineHandle);

    [LibraryImport(Library)]
    public static partial uint FwpmEngineClose0(nint engineHandle);

    [LibraryImport(Library)]
    public static partial uint FwpmTransactionBegin0(nint engineHandle, uint flags);

    [LibraryImport(Library)]
    public static partial uint FwpmTransactionCommit0(nint engineHandle);

    [LibraryImport(Library)]
    public static partial uint FwpmTransactionAbort0(nint engineHandle);

    [LibraryImport(Library)]
    public static partial uint FwpmProviderAdd0(nint engineHandle, Provider* provider, void* securityDescriptor);

    [LibraryImport(Library)]
    public static partial uint FwpmProviderDeleteByKey0(nint engineHandle, in Guid key);

    [LibraryImport(Library)]
    public static partial uint FwpmSubLayerAdd0(nint engineHandle, SubLayer* subLayer, void* securityDescriptor);

    [LibraryImport(Library)]
    public static partial uint FwpmSubLayerDeleteByKey0(nint engineHandle, in Guid key);

    [LibraryImport(Library)]
    public static partial uint FwpmFilterAdd0(nint engineHandle, Filter* filter, void* securityDescriptor, out ulong id);

    [LibraryImport(Library)]
    public static partial uint FwpmFilterDeleteByKey0(nint engineHandle, in Guid key);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint FwpmGetAppIdFromFileName0(string fileName, out ByteBlob* appId);

    [LibraryImport(Library)]
    public static partial void FwpmFreeMemory0(ref void* memory);

    /// <summary>Les structures ci-dessus ne valent que pour Windows x64.</summary>
    public static void EnsureLayout()
    {
        if (!Environment.Is64BitProcess
            || sizeof(Filter) != 200 || sizeof(FilterCondition) != 40 || sizeof(SubLayer) != 72 || sizeof(Provider) != 64)
        {
            throw new PlatformNotSupportedException("Le kill switch WFP demande Windows x64.");
        }
    }
}
