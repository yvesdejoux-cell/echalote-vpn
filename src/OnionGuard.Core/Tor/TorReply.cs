using System.Globalization;

namespace OnionGuard.Core.Tor;

/// <summary>Réponse du port de contrôle : un code (250 = OK) et ses lignes.</summary>
public sealed record TorReply(int StatusCode, IReadOnlyList<string> Lines)
{
    public bool IsSuccess => StatusCode == 250;
}

public sealed class TorControlException(string message) : Exception(message);

/// <summary>Lit une réponse du protocole de contrôle de Tor (control-spec, section 2.3).</summary>
public static class TorReplyReader
{
    public static async Task<TorReply> ReadAsync(TextReader reader, CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        while (true)
        {
            var line = await ReadLineAsync(reader, cancellationToken);
            if (line.Length < 4
                || !int.TryParse(line.AsSpan(0, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var code))
            {
                throw new TorControlException($"Réponse invalide du port de contrôle : {line}");
            }

            var content = line[4..];
            switch (line[3])
            {
                case ' ':
                    lines.Add(content);
                    return new TorReply(code, lines);
                case '-':
                    lines.Add(content);
                    break;
                case '+':
                    // Bloc de données terminé par une ligne « . ».
                    lines.Add(content);
                    while (await ReadLineAsync(reader, cancellationToken) is var data && data != ".")
                    {
                        lines.Add(data.StartsWith("..", StringComparison.Ordinal) ? data[1..] : data);
                    }
                    break;
                default:
                    throw new TorControlException($"Réponse invalide du port de contrôle : {line}");
            }
        }
    }

    private static async Task<string> ReadLineAsync(TextReader reader, CancellationToken cancellationToken) =>
        await reader.ReadLineAsync(cancellationToken)
            ?? throw new TorControlException("Connexion au port de contrôle fermée.");
}
