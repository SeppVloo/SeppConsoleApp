# PongWeb – Specificatie & ontwikkelrichtlijnen

Dit document beschrijft hoe het Pong-spel is opgezet, welke functionaliteit het heeft en welke regels gelden bij het aanpassen.
Het is bedoeld als bron voor ontwikkelaars én voor een AI-agent die het spel moet aanpassen of opnieuw moet opbouwen.

> **Onderhoud:** elke nieuwe of gewijzigde spec wordt hier bijgewerkt (zie [Changelog](#changelog)), in dezelfde commit als de code.

---

## 1. Overzicht

- **Type:** Blazor WebAssembly (standalone, .NET 10), gehost als statische site (GitHub Pages, `wwwroot/CNAME`). PWA met service worker en manifest.
- **Taal UI:** Nederlands. Doelgroep: kinderen/gezin – teksten kort, speels, met emoji.
- **Platformen:** laptop/desktop (toetsenbord + muis) en iPad/iPhone (touch). Alles moet op beide werken.
- **Geen eigen server.** Multiplayer gaat peer-to-peer via WebRTC; matchmaking via publieke MQTT-signaalservers (Trystero).
- Onderdeel van het "Gameplein" (`Pages/GameCenter.razor`), route `/pong`.

## 2. Architectuur

| Bestand | Rol |
|---|---|
| `Game/PongEngine.cs` | Autoritatieve simulatie (C#). Draait alleen op de **host** of bij lokaal spel. |
| `Game/GameState.cs` | Snapshot die elke frame naar JS (en naar clients) gaat: ballen, batjes, power-ups, muurtjes, score, events-sequences. |
| `Game/PowerUps.cs` | Power-up basisklasse, `Modifiers`, `PowerUpRegistry.All`. |
| `Pages/Pong.razor` | Lobby-UI, spelinstellingen, opslag (localStorage), brug tussen JS en engine (`[JSInvokable]`). |
| `wwwroot/js/pong.js` | Game-loop (`requestAnimationFrame`), rendering op canvas, input (pointer/touch/toetsen). |
| `wwwroot/js/net.js` | Matchmaking + P2P-transport (Trystero, MQTT-strategie). |
| `wwwroot/css/app.css` | Alle styling (lobby + game). |

### Dataflow
- **Lokaal / host:** `pong.js` loop → `Tick(dt)` (C#) → `GameState` → tekenen. Host stuurt de state ook naar alle clients.
- **Client:** tekent alleen ontvangen states (met 50 ms interpolatie-buffer), stuurt eigen input (`{ y, slot }`) naar de host.
- Input: `SetTarget(slot, y)` – y in spelcoördinaten (0–450) of `null` = loslaten.

### Engine-regels
- Veld 800×450. Slots: `team = slot % 2` (0 = links, 1 = rechts), `lane = slot / 2`.
- 1v1 = slots 0 en 1; 2v2 = slots 0–3, ieder team een boven- en onderbaan.
- Tick in vaste substappen (≤ 1/120 s) zodat de bal niet door een batje kan tunnelen.
- Effect/spin: beweging van het batje bij raken geeft curve aan de bal.
- 2v2: optioneel botsen teamgenoten (knockback + korte stun).
- Wint: eerste tot `MaxScore` (7). Drone-show met de winnaarsnaam.

### Power-ups
- Nieuwe power-up = subclass van `PowerUp` + toevoegen aan `PowerUpRegistry.All`. Effecten via `Modify` (wordt elke tick opnieuw berekend, dus stapelen werkt vanzelf) of eenmalig via `Activate`.
- Max `PongEngine.MaxPickups` (3) tegelijk in het veld.
- Muurtje: aantal bounces en grootte instelbaar (min/max).
- Instellingen van de **host** gelden in een online spel.

### Computer (AI)
- Niveaus: 🐢 🚶 🏃 🚀 en 🎯 Auto. Auto past snelheid aan het opgeslagen spelersniveau aan.
- Spelersniveau (0–100) wordt na elk potje bijgewerkt (alleen als niet alle spelers op hetzelfde apparaat tegen elkaar spelen).

## 3. Spelers & plekken

Elke plek (behalve "Jij", slot 0) heeft één van drie types; tikken wisselt in deze volgorde:

1. 🤖 **Computer** – met niveaukeuze.
2. 📱 **Op dit apparaat** – extra speler op hetzelfde apparaat, met eigen naam.
3. 🌐 **Ander apparaat** – wordt gevuld door iemand die via wifi/internet meedoet.

- Zijn er geen 🌐-plekken → spel start direct lokaal.
- Zijn er wel 🌐-plekken → host wacht tot alle 🌐-plekken bezet zijn.
- Een meedoend apparaat kan **1–3 spelers** meenemen (elk met eigen naam). De host verdeelt ze bij voorkeur over hetzelfde team.
- Valt een apparaat weg tijdens het spel → computer neemt de batjes van dat apparaat over.

## 4. Besturing

| Situatie | Touch (iPad/iPhone) | Laptop |
|---|---|---|
| 1 speler op apparaat | slepen over het veld | W/S **én** ↑/↓ **én** muis |
| Meerdere spelers op apparaat | ieder sleept op de eigen helft (2v2: boven/onder = baan); multi-touch | ieder eigen toetsen in volgorde: W/S, ↑/↓, T/G, I/K; muis uitgeschakeld |

- Toetsvolgorde volgt de volgorde van de lokale spelers op dat apparaat (eerste = W/S).
- Bij meerdere spelers op een toetsenbord-apparaat staan de toetsen de eerste seconden naast elk batje.
- **Touch-detectie:** `(pointer: coarse)` en geen `(any-pointer: fine)`. Op touch-apparaten worden **nooit** toetsenbord-hints getoond.
- Toetsaanslagen in invoervelden worden genegeerd.

## 5. Lobby-UI

Volgorde van boven naar beneden:
1. Terug naar Gameplein, logo, spelersniveau.
2. Statusmelding (indien aanwezig).
3. **Tabs:** 🎮 *Nieuw spel* | 🤝 *Meedoen* (badge met aantal beschikbare spellen).
   - *Nieuw spel:* 1v1/2v2, twee kolommen LINKS/RECHTS met plek-kaarten. "Jij"-kaart bevat het naamveld. Lokale plekken hebben een naamveld; computerplekken de niveaukeuze. Elke speler-kaart toont de besturing. Bij 🌐-plekken: keuze Wifi/Internet. Samenvatting + startknop.
   - *Meedoen:* Wifi/Internet, aantal spelers op dit apparaat (1–3) met namen, lijst met spellen ("Meedoen →"), inklapbare verbindingsinfo.
4. **⚙ Instellingen** (inklapbaar): power-ups aan/uit, power-ups blijven na goal, muurtje bounces/grootte.
- Tijdens hosten vervangt een wachtkaart de tabs.

## 6. Netwerk (net.js)

- Wifi-room = hash van publiek IP (IPv4 of /64 IPv6-prefix), IP via STUN met fallback naar IP-services. Internet-room = `world`.
- Berichten: `hello` (naam, hosting, busy), `join` (`names[]`), `reply` (`ok`, `slots[]`, `reason`), `state`, `input` (`y`, `slot`), `cmd` (`start`, `leave`, `pause`, `restart`).
- Host valideert dat input-slots bij het verzendende apparaat horen.
- Alle apparaten moeten dezelfde versie draaien (protocol niet backwards-compatible).

## 7. Opslag (localStorage)

| Key | Inhoud |
|---|---|
| `pongName` | eigen naam |
| `pongSetup2` | `teamSize,aiMask,wallMin,wallMax,wallSizeMin,wallSizeMax,aiLevels(-),localMask` |
| `pongLocalNames` | namen lokale plekken (`|`-gescheiden, per slot) |
| `pongJoin` | `aantal|naam2|naam3` voor meedoen |
| `pongDisabledPowerUps` | indices uitgeschakelde power-ups |
| `pongKeepPowerUps`, `pongPaddlesCollide` | `1`/`0` |
| `pongSkill` | `niveau,aantalPotjes` |

## 8. Richtlijnen bij aanpassen

- Engine blijft de enige bron van waarheid; JS rendert alleen.
- Houd het werkend op touch én toetsenbord; toon alleen hints die bij het apparaat passen.
- Nieuwe instellingen → opslaan in localStorage en documenteren in §7.
- Wijzigingen in netwerkberichten → documenteren in §6.
- Teksten in het Nederlands, kort en kindvriendelijk.
- Na elke taak: build, dit document bijwerken, commit en push.

## Changelog

- **Basis:** Pong met power-ups, muurtjes, effect, AI-niveaus, spelersniveau, 1v1/2v2, wifi/internet multiplayer, drone-show.
- **Meerdere spelers op één apparaat:** plek-type 📱, multi-touch per helft, eigen toetsen per speler, namen per lokale speler.
- **Besturing:** 1 speler kan toetsen (W/S, ↑/↓) en muis gebruiken; bij meerdere spelers op laptop alleen toetsenbord.
- **Meerdere spelers per apparaat online:** meedoen met 1–3 spelers; host deelt meerdere slots toe.
- **Lobby herontwerp:** tabs Nieuw spel/Meedoen, naam in eigen plek-kaart, instellingen inklapbaar, touch-apparaten tonen geen toetsenbord-hints. Dit document toegevoegd.
