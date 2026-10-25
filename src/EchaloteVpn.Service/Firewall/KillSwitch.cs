using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using EchaloteVpn.Core.Configuration;
using EchaloteVpn.Core.Firewall;
using EchaloteVpn.Service.Tor;
using EchaloteVpn.Service.Tunnel;
using static EchaloteVpn.Service.Firewall.WfpNative;
using NativeCondition = EchaloteVpn.Service.Firewall.WfpNative.FilterCondition;

namespace EchaloteVpn.Service.Firewall;

/// <summary>
/// Pose et retire les filtres WFP du kill switch, dans un fournisseur et une sous-couche EchaloteVpn.
/// Les filtres sont persistants : ils restent après un plantage du service, et même après un
/// redémarrage du PC, jusqu'à un arrêt normal ou « --reset-network ».
/// </summary>
public sealed unsafe class KillSwitch(TorOptions torOptions, TunnelOptions tunnelOptions, ILogger<KillSwitch> logger)
{
    private static readonly Guid ProviderKey = new("557bdfb1-e062-4c75-b5d6-d4e7500c6980");
    private static readonly Guid SubLayerKey = new("f91890bb-3964-4650-bf33-4d27f47ab9f8");

    // Clés fixes, une par filtre : le retrait n'a pas à énumérer les filtres de Windows.
    private const int MaxFilters = 64;

    public void Enable(EchaloteVpnSettings settings)
    {
        EnsureLayout();
        if (!File.Exists(torOptions.TorExePath))
        {
            throw new FileNotFoundException("tor.exe introuvable : lancer scripts/fetch-deps.ps1.", torOptions.TorExePath);
        }
        var filters = KillSwitchPlan.Build(settings, tunnelOptions.RoutePlan);
        if (filters.Count > MaxFilters)
        {
            throw new InvalidOperationException($"Trop de filtres ({filters.Count}, {MaxFilters} au plus).");
        }

        var needsTransport = filters.Any(f => f.Conditions.Any(c => c is TransportProgramCondition));
        if (needsTransport && !File.Exists(torOptions.LyrebirdPath))
        {
            throw new FileNotFoundException("lyrebird.exe introuvable dans le bundle de Tor.", torOptions.LyrebirdPath);
        }

        Check(FwpmGetAppIdFromFileName0(torOptions.TorExePath, out var torAppId), "identifiant de tor.exe");
        ByteBlob* transportAppId = null;
        try
        {
            if (needsTransport)
            {
                Check(FwpmGetAppIdFromFileName0(torOptions.LyrebirdPath, out transportAppId), "identifiant de lyrebird.exe");
            }
            var appIds = new AppIds(torAppId, transportAppId);
            WithTransaction(engine =>
            {
                RemoveAll(engine);
                AddProviderAndSubLayer(engine);
                for (var i = 0; i < filters.Count; i++)
                {
                    AddFilter(engine, FilterKey(i), filters[i], appIds);
                }
            });
        }
        finally
        {
            var memory = (void*)torAppId;
            FwpmFreeMemory0(ref memory);
            if (transportAppId is not null)
            {
                memory = transportAppId;
                FwpmFreeMemory0(ref memory);
            }
        }
        logger.LogInformation(
            "Kill switch actif : {Count} filtres WFP, réseau local {Lan}.",
            filters.Count, settings.AllowLocalNetwork ? "autorisé" : "bloqué");
    }

    /// <summary>Retire tous les filtres EchaloteVpn. Renvoie leur nombre (0 si rien n'était en place).</summary>
    public static int Disable()
    {
        EnsureLayout();
        var removed = 0;
        WithTransaction(engine => removed = RemoveAll(engine));
        return removed;
    }

    private static int RemoveAll(nint engine)
    {
        var removed = 0;
        for (var i = 0; i < MaxFilters; i++)
        {
            var status = FwpmFilterDeleteByKey0(engine, FilterKey(i));
            if (status == 0)
            {
                removed++;
            }
            else if (status != FwpEFilterNotFound)
            {
                Check(status, $"retrait du filtre {i}");
            }
        }
        var subLayerStatus = FwpmSubLayerDeleteByKey0(engine, SubLayerKey);
        if (subLayerStatus != FwpESubLayerNotFound)
        {
            Check(subLayerStatus, "retrait de la sous-couche EchaloteVpn");
        }
        var providerStatus = FwpmProviderDeleteByKey0(engine, ProviderKey);
        if (providerStatus != FwpEProviderNotFound)
        {
            Check(providerStatus, "retrait du fournisseur EchaloteVpn");
        }
        return removed;
    }

    private static void AddProviderAndSubLayer(nint engine)
    {
        using var memory = new NativeArena();
        var provider = new Provider
        {
            ProviderKey = ProviderKey,
            DisplayData = memory.Display("EchaloteVpn", "Kill switch de EchaloteVpn"),
            Flags = ProviderFlagPersistent,
        };
        Check(FwpmProviderAdd0(engine, &provider, null), "ajout du fournisseur EchaloteVpn");

        var providerKey = ProviderKey;
        var subLayer = new SubLayer
        {
            SubLayerKey = SubLayerKey,
            DisplayData = memory.Display("EchaloteVpn", "Filtres du kill switch de EchaloteVpn"),
            Flags = SubLayerFlagPersistent,
            ProviderKey = &providerKey,
            Weight = ushort.MaxValue,
        };
        Check(FwpmSubLayerAdd0(engine, &subLayer, null), "ajout de la sous-couche EchaloteVpn");
    }

    /// <summary>Identifiants WFP des programmes autorisés : tor.exe, et lyrebird.exe avec des ponts.</summary>
    private readonly struct AppIds(ByteBlob* tor, ByteBlob* transport)
    {
        public ByteBlob* Tor { get; } = tor;

        public ByteBlob* Transport { get; } = transport;
    }

    private static void AddFilter(nint engine, Guid key, FilterSpec spec, AppIds appIds)
    {
        using var memory = new NativeArena();
        var conditions = memory.Alloc<NativeCondition>(Math.Max(spec.Conditions.Count, 1));
        for (var i = 0; i < spec.Conditions.Count; i++)
        {
            conditions[i] = ToNative(spec.Conditions[i], appIds, memory);
        }

        var providerKey = ProviderKey;
        var filter = new Filter
        {
            FilterKey = key,
            DisplayData = memory.Display($"EchaloteVpn : {spec.Name}", null),
            // Un blocage EchaloteVpn ne peut pas être levé par un autre logiciel.
            Flags = FilterFlagPersistent | (spec.Action == FilterAction.Block ? FilterFlagClearActionRight : 0),
            ProviderKey = &providerKey,
            LayerKey = spec.Layer switch
            {
                WfpLayer.ConnectV4 => LayerAleAuthConnectV4,
                WfpLayer.ConnectV6 => LayerAleAuthConnectV6,
                WfpLayer.AcceptV4 => LayerAleAuthRecvAcceptV4,
                _ => LayerAleAuthRecvAcceptV6,
            },
            SubLayerKey = SubLayerKey,
            Weight = new Value { Type = TypeUInt8, UInt8 = spec.Weight },
            NumFilterConditions = (uint)spec.Conditions.Count,
            FilterConditions = spec.Conditions.Count > 0 ? conditions : null,
            ActionType = spec.Action == FilterAction.Permit ? ActionPermit : ActionBlock,
        };
        Check(FwpmFilterAdd0(engine, &filter, null, out _), $"ajout du filtre « {spec.Name} » ({spec.Layer})");
    }

    private static NativeCondition ToNative(Core.Firewall.FilterCondition condition, AppIds appIds, NativeArena memory)
    {
        switch (condition)
        {
            case LoopbackCondition:
                return Condition(ConditionFlags, MatchFlagsAllSet, new Value { Type = TypeUInt32, UInt32 = ConditionFlagIsLoopback });
            case TorProgramCondition:
                return Condition(ConditionAleAppId, MatchEqual, new Value { Type = TypeByteBlob, Pointer = appIds.Tor });
            case TransportProgramCondition:
                return Condition(ConditionAleAppId, MatchEqual, new Value { Type = TypeByteBlob, Pointer = appIds.Transport });
            case LocalAddressCondition local:
                return Condition(ConditionIpLocalAddress, MatchEqual, new Value { Type = TypeUInt32, UInt32 = HostOrder(local.Address) });
            case ProtocolCondition protocol:
                return Condition(ConditionIpProtocol, MatchEqual, new Value { Type = TypeUInt8, UInt8 = (byte)protocol.Protocol });
            case LocalPortCondition port:
                return Condition(ConditionIpLocalPort, MatchEqual, new Value { Type = TypeUInt16, UInt16 = port.Port });
            case RemotePortCondition port:
                return Condition(ConditionIpRemotePort, MatchEqual, new Value { Type = TypeUInt16, UInt16 = port.Port });
            case RemotePrefixCondition prefix when prefix.Network.AddressFamily == AddressFamily.InterNetwork:
                var v4 = memory.Alloc<V4AddrAndMask>(1);
                v4->Address = HostOrder(prefix.Network);
                v4->Mask = prefix.Length == 0 ? 0 : uint.MaxValue << (32 - prefix.Length);
                return Condition(ConditionIpRemoteAddress, MatchEqual, new Value { Type = TypeV4AddrMask, Pointer = v4 });
            case RemotePrefixCondition prefix:
                var v6 = memory.Alloc<V6AddrAndMask>(1);
                prefix.Network.GetAddressBytes().CopyTo(new Span<byte>(v6->Address, 16));
                v6->PrefixLength = (byte)prefix.Length;
                return Condition(ConditionIpRemoteAddress, MatchEqual, new Value { Type = TypeV6AddrMask, Pointer = v6 });
            default:
                throw new NotSupportedException($"Condition non gérée : {condition}");
        }
    }

    private static NativeCondition Condition(Guid field, uint match, Value value) =>
        new() { FieldKey = field, MatchType = match, ConditionValue = value };

    // WFP attend les adresses IPv4 dans l'ordre de la machine, pas celui du réseau.
    private static uint HostOrder(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

    private static Guid FilterKey(int index) =>
        new(0xf261027a, 0x50d6, 0x4090, 0xa5, 0xda, 0x65, 0xf2, 0xba, 0x2f, 0x5d, (byte)index);

    /// <summary>Ouvre le moteur WFP et fait tout dans une transaction : tout est appliqué, ou rien.</summary>
    private static void WithTransaction(Action<nint> action)
    {
        Check(FwpmEngineOpen0(null, RpcCAuthnWinNt, null, null, out var engine), "ouverture du moteur WFP (droits administrateur ?)");
        try
        {
            Check(FwpmTransactionBegin0(engine, 0), "début de transaction WFP");
            try
            {
                action(engine);
                Check(FwpmTransactionCommit0(engine), "validation de la transaction WFP");
            }
            catch
            {
                FwpmTransactionAbort0(engine);
                throw;
            }
        }
        finally
        {
            FwpmEngineClose0(engine);
        }
    }

    private static void Check(uint status, string operation)
    {
        if (status == 5)
        {
            throw new InvalidOperationException($"Échec WFP : {operation}, accès refusé (droits administrateur ?).");
        }
        if (status != 0)
        {
            throw new InvalidOperationException($"Échec WFP : {operation} (code 0x{status:X8}).");
        }
    }

    /// <summary>Mémoire non managée d'un appel WFP, libérée d'un coup.</summary>
    private sealed class NativeArena : IDisposable
    {
        private readonly List<nint> blocks = [];

        public T* Alloc<T>(int count) where T : unmanaged
        {
            var block = NativeMemory.AllocZeroed((nuint)(sizeof(T) * count));
            blocks.Add((nint)block);
            return (T*)block;
        }

        public DisplayData Display(string name, string? description) =>
            new() { Name = String(name), Description = description is null ? null : String(description) };

        private char* String(string value)
        {
            var text = Alloc<char>(value.Length + 1);
            value.AsSpan().CopyTo(new Span<char>(text, value.Length));
            return text;
        }

        public void Dispose()
        {
            foreach (var block in blocks)
            {
                NativeMemory.Free((void*)block);
            }
        }
    }
}
