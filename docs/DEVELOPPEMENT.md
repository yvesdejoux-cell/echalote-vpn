# Échalote VPN — documentation technique

Pour l'utilisation, voir le [README](../README.md).

## Architecture

```
┌───────────────────────┐  IPC (pipe nommé)  ┌──────────────────────────────┐
│ Interface (session)   │ ◄────────────────► │ Service Windows (SYSTEM)     │
│ EchaloteVpn.App (WPF) │                    │ EchaloteVpn.Service          │
│ - Connecter           │                    │  ├─ lance et surveille tor   │
│ - État, pays          │                    │  ├─ carte Wintun             │
│ - Nouvelle identité   │                    │  ├─ tun2socks                │
│ - Ponts               │                    │  ├─ routes + DNS             │
└───────────────────────┘                    │  └─ kill switch (pare-feu)   │
                                             └──────────────────────────────┘
Trafic Windows → Wintun → tun2socks → SOCKS5 Tor (9050) → réseau Tor
Requêtes DNS   → règle NRPT → DNSPort Tor (127.66.0.1:53)
UDP / IPv6     → bloqués
```

| Projet | Rôle |
| --- | --- |
| `src/EchaloteVpn.Core` | Réglages et contrats IPC partagés |
| `src/EchaloteVpn.Service` | Service Windows avec les droits administrateur |
| `src/EchaloteVpn.App` | Interface WPF et icône près de l'horloge |
| `tests/EchaloteVpn.Core.Tests` | Tests unitaires (xUnit) |

## Compiler

Prérequis : Windows et le SDK .NET 10.

```powershell
./scripts/fetch-deps.ps1   # télécharge et vérifie Tor, Wintun et tun2socks (PowerShell 7, gpg de Git for Windows)
dotnet build EchaloteVpn.slnx
dotnet test EchaloteVpn.slnx
./scripts/publish.ps1      # versions autonomes (.NET embarqué) dans publish/EchaloteVpn
iscc installer/EchaloteVpn.iss  # installateur dans publish/installer (Inno Setup 6)
```

Chaque PR produit aussi un zip `EchaloteVpn-win-x64` (page de la PR, onglet **Checks**, puis
**Artifacts**) avec `service\EchaloteVpn.Service.exe` (avec Tor) et `app\EchaloteVpn.exe`.

## Lancer sans installateur

1. Déconnecter tout autre VPN (Mullvad : désactiver aussi le « lockdown mode »).
2. Ouvrir PowerShell **en administrateur** dans le dossier `service`, puis taper
   `.\EchaloteVpn.Service.exe`. Le service attend l'interface ; laisser cette fenêtre ouverte.
3. Lancer `app\EchaloteVpn.exe`, sans droits administrateur, puis cliquer sur **Connecter**.
4. `Ctrl+C` dans la fenêtre du service l'arrête, ainsi que Tor et le tunnel.

Sans droits administrateur, Tor démarre quand même, et le journal signale toutes les 30 s que le
tunnel est indisponible.

## Installateur

L'installateur crée le service Windows **EchaloteVpn** (compte SYSTEM, démarrage automatique,
redémarré s'il s'arrête) dans `C:\Program Files\EchaloteVpn` et, au choix, lance l'icône à
l'ouverture de session. Il désinstalle d'abord les versions nommées TorVpn ou OnionGuard. La
désinstallation arrête le service, retire filtres et règle DNS (`--reset-network`), supprime le
service et, si on le demande, les réglages. Le journal du service est dans l'observateur
d'événements (Application, source EchaloteVpn).

Les réglages sont dans `C:\ProgramData\EchaloteVpn\settings.json` (modifiable seulement par les
administrateurs ; l'interface passe par le service). Si le service s'arrête pendant une
connexion, il la reprend au démarrage suivant ; le kill switch bloque le trafic en attendant.

## Vérifier le tunnel

1. Connecter, puis attendre « Connecté via Tor » (`Tunnel actif` dans le journal du service).
2. `curl.exe -4 https://check.torproject.org/api/ip` doit répondre `"IsTor":true`. Le `-4` évite
   d'attendre l'échec de l'IPv6, que le kill switch bloque.
3. `Get-DnsClientNrptRule` montre une règle `.` vers `127.66.0.1`, et `Resolve-DnsName example.com`
   répond.
4. Déconnecter, puis `route print -4` : les routes `0.0.0.0/1` et `128.0.0.0/1` ont disparu, la
   carte `EchaloteVpn` aussi, et `Get-DnsClientNrptRule` ne montre plus de règle EchaloteVpn.

## Fonctionnement

**Tunnel** : la carte `EchaloteVpn` reçoit l'adresse `10.66.0.1/30` et les routes `0.0.0.0/1` et
`128.0.0.0/1`, plus précises que la route par défaut de Windows, qui reste en place. `tor.exe`
se lie à l'adresse de la carte physique (`OutboundBindAddress`) : ses propres connexions sortent
par elle et ne repassent pas dans le tunnel. `tun2socks` tourne dans un Job Object : s'il meurt
avec le service, Windows supprime la carte et ses routes.

**DNS** : Tor écoute en `127.66.0.1:53`, et une règle NRPT pour le domaine `.` (tous les noms) y
envoie toutes les requêtes ; Windows n'interroge plus la box. Limites du DNS de Tor : requêtes A,
AAAA et PTR seulement, en UDP. Windows refuse de résoudre les noms en `.onion`.

**Kill switch** : des filtres WFP (le moteur du pare-feu de Windows), dans une sous-couche
EchaloteVpn prioritaire, posés au démarrage du service avant Tor et retirés en dernier à l'arrêt.
Ils n'autorisent que la boucle locale, `tor.exe`, le TCP qui part de la carte `EchaloteVpn` et le
DHCP ; le réseau local est bloqué tant que `AllowLocalNetwork` est faux. Ils sont persistants : un
plantage laisse le PC bloqué jusqu'à `EchaloteVpn.Service.exe --reset-network`.

**Ponts** : `lyrebird.exe` rejoint les ponts obfs4 lui-même ; le service ajoute une route /32 vers
chaque pont par la passerelle de la carte physique, et le kill switch autorise `lyrebird.exe`.
Snowflake et meek ne sont pas pris en charge : ils ont besoin de DNS et d'UDP hors du tunnel, ce
que Windows ne permet de réserver à un seul programme qu'avec un pilote réseau.

**Profils réseau** : tun2socks donne un nouveau GUID à la carte à chaque lancement, et Windows lui
crée un nouveau profil (« EchaloteVpn 2 », « EchaloteVpn 3 »…). Le service les retire à la fin de
chaque vie du tunnel, et `--reset-network` aussi.

## Binaires externes

`tor.exe` (Tor Expert Bundle), `wintun.dll` et `tun2socks.exe` ne sont **pas** stockés dans ce
dépôt. `scripts/fetch-deps.ps1` les télécharge depuis leurs sources officielles, dans les versions
figées par `third_party/deps.json` :

- **Tor** : signature GPG de la clé des développeurs du Tor Browser
  (`EF6E 286D DA85 EA2A 4BA7 DE68 4E2C 6E87 9329 8290`) **et** empreinte SHA-256 figée ;
- **Wintun** : empreinte SHA-256 figée et signature Authenticode de WireGuard LLC sur `wintun.dll` ;
- **tun2socks** : empreinte SHA-256 figée (le projet ne signe pas ses fichiers).

Leurs licences sont reprises dans l'installateur (`installer/THIRD-PARTY.txt`).
