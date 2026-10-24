using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OnionGuard.Core.Ipc;

/// <summary>
/// Format d'échange entre l'interface et le service : un objet JSON en UTF-8 par ligne.
/// </summary>
public static class IpcProtocol
{
    /// <summary>Taille maximale d'un message : un client défaillant ne peut pas faire grossir le service.</summary>
    public const int MaxMessageLength = 64 * 1024;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, JsonOptions);

    public static T? Deserialize<T>(string line) => JsonSerializer.Deserialize<T>(line, JsonOptions);

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(Serialize(message) + "\n");
        if (bytes.Length > MaxMessageLength)
        {
            throw new InvalidDataException("Message IPC trop long.");
        }
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>Lit un message ; renvoie null si l'autre côté a fermé sans rien envoyer.</summary>
    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        var buffer = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, cancellationToken) == 0)
            {
                return buffer.Count == 0 ? default : throw new EndOfStreamException("Message IPC tronqué.");
            }
            if (one[0] == (byte)'\n')
            {
                return Deserialize<T>(Encoding.UTF8.GetString(buffer.ToArray()));
            }
            if (buffer.Count >= MaxMessageLength)
            {
                throw new InvalidDataException("Message IPC trop long.");
            }
            buffer.Add(one[0]);
        }
    }
}
