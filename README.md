# OnionGuard

**Routage Onion global** (*System-wide Onion Router*) : client Windows qui fait passer **tout le
trafic du système** par le réseau Tor, à la manière d'un VPN.

OnionGuard n'est ni développé ni soutenu par le Tor Project ; il utilise le logiciel Tor. Il
s'appelait TorVpn jusqu'au 25/10/2026.

> Statut : **étape 7, installateur**. OnionGuard s'installe avec `OnionGuard-Setup-0.8.2.exe` (service
> sous SYSTEM, icône près de l'horloge). La fenêtre OnionGuard connecte et déconnecte, affiche l'état et
> le pays de sortie, demande une nouvelle identité et règle le pays de sortie, le kill switch,
> le réseau local et les ponts obfs4 (intégrés ou personnalisés). Une fois connecté, le trafic TCP IPv4 et le DNS de Windows passent par Tor ;
> le kill switch bloque tout le reste (IPv6, UDP, DNS vers la box), et tout trafic direct quand
> Tor ou le tunnel est coupé.

> **Plus d'Internet après un plantage ?** Les filtres du kill switch restent en place, même après
> un redémarrage du PC. Dans un PowerShell **administrateur**, dans le dossier `service` :
> `.\OnionGuard.Service.exe --reset-network`

## Architecture

```
┌──────────────────┐   IPC (named pipe)   ┌──────────────────────────────┐
│ Interface (user) │ ◄──────────────────► │ Service Windows (SYSTEM)     │
│ OnionGuard.App (WPF) │                      │ OnionGuard.Service               │
│ - Connecter      │                      │  ├─ lance et surveille tor   │
│ - État, pays     │                      │  ├─ carte Wintun             │
│ - Nouvelle IP    │                      │  ├─ tun2socks                │
│ - Bridges        │                      │  ├─ routes + DNS             │
└──────────────────┘                      │  └─ kill switch (pare-feu)   │
                                          └──────────────────────────────┘
Trafic Windows → Wintun → tun2socks → SOCKS5 Tor (9050) → réseau Tor
Requêtes DNS   → règle NRPT → DNSPort Tor (127.66.0.1:53)
UDP / IPv6     → bloqués
```

| Projet | Rôle |
| --- | --- |
| `src/OnionGuard.Core` | Réglages et contrats IPC partagés |
| `src/OnionGuard.Service` | Service Windows avec les droits administrateur |
| `src/OnionGuard.App` | Interface WPF |
| `tests/OnionGuard.Core.Tests` | Tests unitaires (xUnit) |

## Feuille de route

1. Squelette : solution .NET 10, CI Windows
2. Moteur Tor : lancement de `tor.exe`, bootstrap, port de contrôle
3. Tunnel : Wintun, tun2socks, routes, exclusion du trafic de `tor.exe`
4. DNS et kill switch : DNS via Tor, blocage UDP/IPv6, coupure si Tor tombe
5. Interface : connexion, état, pays de sortie, nouvelle identité, réglages
6. Ponts : obfs4 intégrés ou personnalisés (Snowflake et meek non pris en charge, voir plus bas)
7. Installateur (Inno Setup), service sous SYSTEM, icône près de l'horloge, tests de fuite

## Installer

`OnionGuard-Setup-0.8.2.exe` (administrateur) installe OnionGuard dans `C:\Program Files\OnionGuard`, crée le
service Windows **OnionGuard** (compte SYSTEM, démarrage automatique, redémarré s'il s'arrête) et,
au choix, lance l'icône près de l'horloge à l'ouverture de session. La désinstallation arrête
le service, retire filtres et règle DNS (`--reset-network`), supprime le service et, si on le
demande, les réglages de `C:\ProgramData\OnionGuard`. Le journal du service est dans l'observateur
d'événements (Application, source OnionGuard).

L'installateur n'est pas signé : Windows SmartScreen peut avertir au premier lancement.

## Compiler

Prérequis : Windows et le SDK .NET 10.

```powershell
./scripts/fetch-deps.ps1   # télécharge et vérifie Tor, Wintun et tun2socks (PowerShell 7, gpg de Git for Windows)
dotnet build OnionGuard.slnx
dotnet test OnionGuard.slnx
./scripts/publish.ps1      # versions autonomes (.NET embarqué) dans publish/OnionGuard
iscc installer/OnionGuard.iss  # installateur dans publish/installer (Inno Setup 6)
```

## Tester sans compiler

Chaque PR produit un zip `OnionGuard-win-x64` (page de la PR, onglet **Checks**, puis **Artifacts**).
Il contient `service\OnionGuard.Service.exe` (avec Tor) et `app\OnionGuard.exe`, sans rien à installer.

### Utiliser OnionGuard

Tant que l'installateur n'existe pas (étape 7), le service se lance à la main :

1. Déconnecter tout autre VPN (Mullvad : désactiver aussi le « lockdown mode »).
2. Ouvrir PowerShell **en administrateur** dans le dossier `service`, puis taper `.\OnionGuard.Service.exe`.
   Le service attend l'interface ; laisser cette fenêtre ouverte.
3. Lancer `app\OnionGuard.exe`, sans droits administrateur, puis cliquer sur **Connecter**.
4. `Ctrl+C` dans la fenêtre du service l'arrête, ainsi que Tor et le tunnel.

Les réglages sont dans `C:\ProgramData\OnionGuard\settings.json` (modifiable seulement par les
administrateurs, l'interface passe par le service). Si le service s'arrête pendant une
connexion, il la reprend tout seul au démarrage suivant ; le kill switch bloque le trafic en
attendant. Sinon, au démarrage, il retire ce qu'un plantage aurait laissé.

### Tester le tunnel

Créer la carte Wintun et poser des routes demande les **droits administrateur**. Sans eux, Tor
démarre quand même, et le journal signale toutes les 30 s que le tunnel est indisponible.

1. Déconnecter tout autre VPN (Mullvad : désactiver aussi le « lockdown mode »).
2. Lancer le service et l'interface comme ci-dessus, puis cliquer sur **Connecter**.
3. Attendre « Connecté via Tor » dans la fenêtre (`Tunnel actif` dans le journal du service).
4. Dans un autre PowerShell : `curl.exe -4 https://check.torproject.org/api/ip` doit répondre
   `"IsTor":true`. Le `-4` évite d'attendre l'échec de l'IPv6, que le kill switch bloque.
5. `Get-DnsClientNrptRule` montre une règle `.` vers `127.66.0.1`, et `Resolve-DnsName example.com`
   répond.
6. **Déconnecter** dans la fenêtre, puis `route print -4` : les routes `0.0.0.0/1`
   (masque `128.0.0.0`) et `128.0.0.0/1` ont disparu, la carte `OnionGuard` aussi, et
   `Get-DnsClientNrptRule` ne montre plus de règle OnionGuard.

Fonctionnement : la carte `OnionGuard` reçoit l'adresse `10.66.0.1/30` et les routes `0.0.0.0/1` et
`128.0.0.0/1`, plus précises que la route par défaut de Windows, qui reste en place. `tor.exe`
se lie à l'adresse de la carte physique (`OutboundBindAddress`) : ses propres connexions sortent par
elle et ne repassent pas dans le tunnel. `tun2socks` tourne dans un Job Object : s'il meurt avec
le service, Windows supprime la carte et ses routes.

DNS : Tor écoute en `127.66.0.1:53`, et une règle NRPT (table de résolution de Windows) pour le
domaine `.`, donc tous les noms, y envoie toutes les requêtes ; Windows n'interroge plus la box.
Limites du DNS de Tor : seulement les requêtes A, AAAA et PTR (pas MX, TXT ni SRV), et seulement
en UDP. Windows refuse lui-même de résoudre les noms en `.onion` : pour ces sites, utiliser le
Tor Browser.

Kill switch : des filtres WFP (le moteur du pare-feu de Windows), dans une sous-couche OnionGuard
prioritaire, posés au démarrage du service avant Tor et retirés en dernier à l'arrêt. Ils
n'autorisent que la boucle locale, `tor.exe`, le TCP qui part de la carte `OnionGuard` et le DHCP ;
le réseau local (box, imprimante, NAS) est bloqué tant que le réglage `AllowLocalNetwork` est
faux. Ils sont persistants : un plantage laisse le PC bloqué, voir `--reset-network` plus haut.
Le mode « lockdown » de Mullvad doit être désactivé : son propre blocage l'emporterait.

Ponts : si Tor est bloqué sur votre réseau, choisir « Ponts obfs4 intégrés » (ceux du bundle de
Tor), ou « Ponts obfs4 personnalisés » et coller une ligne `obfs4 …` par pont, obtenue sur
https://bridges.torproject.org (au moins deux ponts, conseille Tor). `lyrebird.exe` rejoint les
ponts lui-même : le service ajoute une route /32 vers chaque pont par la passerelle de la carte
physique, et le kill switch autorise `lyrebird.exe`. **Snowflake et meek ne sont pas pris en
charge** : ils ont besoin de DNS et d'UDP hors du tunnel, ce que Windows ne permet de réserver à
un seul programme qu'avec un pilote réseau.

Profils réseau : tun2socks donne un nouveau GUID à la carte `OnionGuard` à chaque lancement, et
Windows lui crée un nouveau profil réseau (« OnionGuard 2 », « OnionGuard 3 »…). Le service les retire à
la fin de chaque vie du tunnel, et `--reset-network` aussi.

## Binaires externes

`tor.exe` (Tor Expert Bundle), `wintun.dll` et `tun2socks.exe` ne sont **pas** stockés dans ce dépôt.
Ils sont téléchargés à la compilation par `scripts/fetch-deps.ps1`, depuis leurs sources officielles,
dans les versions figées par `third_party/deps.json`. Pour Tor, l'archive doit porter une signature GPG
de la clé des développeurs du Tor Browser (`EF6E 286D DA85 EA2A 4BA7 DE68 4E2C 6E87 9329 8290`) **et**
correspondre à l'empreinte SHA-256 figée. Pour Wintun, l'archive doit correspondre à son empreinte
SHA-256 figée et `wintun.dll` porter une signature Authenticode valide de WireGuard LLC. Pour
`tun2socks`, qui ne signe pas ses fichiers, seule compte l'empreinte SHA-256 figée. `tun2socks` est sous GPL-3.0, Wintun et Tor sous
leurs propres licences, qui seront reprises dans l'installateur.

## Avertissement sur l'anonymat

Faire passer tout le système par Tor protège **moins** que le Tor Browser : les navigateurs et
applications ordinaires laissent des empreintes qui permettent de vous reconnaître. Pour un
anonymat fort, utilisez le Tor Browser. Tor ne transporte ni l'UDP, ni la VoIP, ni les jeux en ligne.

## Soutenir le projet

OnionGuard est gratuit. Pour soutenir son développement, un don ponctuel est possible sur
[Ko-fi](https://ko-fi.com/ptidej) (lien aussi dans la fenêtre et dans le menu de l'icône).

## Licence

GPL-3.0-or-later. Voir [LICENSE](LICENSE).
