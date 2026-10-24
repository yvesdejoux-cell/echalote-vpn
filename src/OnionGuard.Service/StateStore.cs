using System.Security.AccessControl;
using System.Security.Principal;
using OnionGuard.Core.Configuration;

namespace OnionGuard.Service;

/// <summary>
/// settings.json dans C:\ProgramData\OnionGuard. Le fichier n'est modifiable que par SYSTEM et les
/// administrateurs ; un fichier qui appartient à quelqu'un d'autre est ignoré, car un simple
/// utilisateur peut créer des fichiers dans ProgramData.
/// </summary>
public sealed class StateStore(ILogger<StateStore> logger)
{
    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdministratorsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier UsersSid = new(WellKnownSidType.BuiltinUsersSid, null);

    public string FilePath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OnionGuard", "settings.json");

    public PersistedState Load()
    {
        var file = new FileInfo(FilePath);
        if (!file.Exists)
        {
            return PersistedState.Default;
        }
        var owner = file.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        if (owner != SystemSid && owner != AdministratorsSid)
        {
            logger.LogWarning("{File} ignoré : il n'appartient ni à SYSTEM ni aux administrateurs.", FilePath);
            return PersistedState.Default;
        }
        return PersistedState.Parse(File.ReadAllText(FilePath));
    }

    public void Save(PersistedState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temporary, state.Serialize());

            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetOwner(AdministratorsSid);
            security.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(AdministratorsSid, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(UsersSid, FileSystemRights.Read, AccessControlType.Allow));
            new FileInfo(temporary).SetAccessControl(security);

            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
