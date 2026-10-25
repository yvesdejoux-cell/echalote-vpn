using System.Text.Json;
using EchaloteVpn.Core.Ipc;

namespace EchaloteVpn.Core.Configuration;

/// <summary>
/// Ce que le service garde entre deux démarrages (settings.json) : les réglages, et si la
/// connexion était voulue, pour reprendre après un plantage ou un redémarrage.
/// </summary>
public sealed record PersistedState(UserSettings Settings, bool ConnectionWanted)
{
    public static PersistedState Default { get; } = new(new UserSettings(), false);

    public string Serialize() => JsonSerializer.Serialize(this, new JsonSerializerOptions(IpcProtocol.JsonOptions) { WriteIndented = true });

    /// <summary>Un fichier abîmé ou aux réglages invalides donne les valeurs par défaut.</summary>
    public static PersistedState Parse(string json)
    {
        try
        {
            var state = JsonSerializer.Deserialize<PersistedState>(json, IpcProtocol.JsonOptions);
            return state?.Settings is null ? Default : state with { Settings = state.Settings.Normalize() };
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        {
            return Default;
        }
    }
}
