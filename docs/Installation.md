# DNWsus – Installation

## 1. Voraussetzungen

| | |
|---|---|
| **Server** | Windows Server 2019 oder neuer, Mitglied der Domäne. Keine weiteren Komponenten nötig (.NET ist enthalten). |
| **Clients** | Windows 10/11 (Domänenmitglied) mit .NET Framework 4.8 – in allen aktuellen Versionen enthalten. |
| **Netzwerk** | Clients erreichen den Server auf **TCP 13162** (im Büro oder per VPN). Die Firewall-Regel auf dem Server legt die MSI an (Profil Domäne). |
| **AD** | Gruppen `GRP_Update1`, `GRP_Update2`, `GRP_Update3` mit den Computerkonten der Clients (verschachtelte Gruppen sind erlaubt). |

## 2. Server installieren

1. `DnwSus-Server-<Version>.msi` auf dem Server als Administrator ausführen.
2. Die MSI richtet ein:
   - Programm unter `C:\Program Files\DNWsus\Server`
   - Windows-Dienst **DNWsus Server** (`DnwSusServer`, läuft als SYSTEM, startet automatisch)
   - Firewall-Regel „DNWsus Server (TCP 13162)“
   - Startmenü-Eintrag **DNWsus → DNWsus Dashboard**
3. Beim ersten Start stellt der Dienst sein **eigenes HTTPS-Zertifikat** aus (10 Jahre gültig, „DNWsus Server“ im
   Zertifikatsspeicher des Computers) und trägt es auf dem Server als vertrauenswürdig ein. Eine interne CA wird nicht benötigt.
4. Dashboard auf dem Server öffnen: Startmenü **DNWsus Dashboard** bzw. `https://localhost:13162`.
   Zugriff haben nur **Domänen-Admins**, und nur direkt auf diesem Server.

Daten (Datenbank, erzeugte Agent-MSI) liegen unter `C:\ProgramData\DNWsus\Server` (nur SYSTEM/Administratoren).
Einstellungen (Gruppennamen, Admin-Gruppe) stehen in `C:\Program Files\DNWsus\Server\appsettings.json`
und bleiben bei Updates erhalten. Nach Änderungen den Dienst neu starten.

**Update des Servers:** neue Server-MSI einfach installieren. Zertifikat und Daten bleiben erhalten,
die Agenten müssen nicht neu verteilt werden.

## 3. Agent verteilen

1. Im Dashboard die Seite **Agent** öffnen → **DnwSus-Agent.msi herunterladen**.
   Diese MSI enthält Adresse und Zertifikats-Fingerabdruck **dieses** Servers.
2. MSI auf eine Freigabe legen, die **Domänencomputer** lesen dürfen, z. B. `\\server\Software$\DNWsus\DnwSus-Agent.msi`.
3. GPO: *Computerkonfiguration → Richtlinien → Softwareeinstellungen → Softwareinstallation → Neu → Paket*
   → UNC-Pfad der MSI → **Zugewiesen**. GPO mit der OU der Clients verknüpfen.
4. Die Installation erfolgt beim nächsten Neustart des Clients. Danach meldet sich der Agent innerhalb von 5 Minuten im Dashboard.

Alternativ: MSI auf einem Client als Administrator per Doppelklick installieren.

Ein Agent ohne Update-Gruppe meldet nur seinen Stand (ausstehende Updates, Uptime, IP, Benutzer) und installiert nichts.

**Update des Agenten:** neue Agent-MSI im Dashboard herunterladen, in der GPO das Paket ersetzen
(bzw. als Aktualisierung des alten Pakets hinzufügen). Die MSI ersetzt vorhandene Versionen selbst.

**Deinstallation:** über die GPO („Software entfernen“) oder Programme und Features. Dienst, Programm,
Registry-Einträge und `C:\ProgramData\DNWsus\Agent` werden entfernt.

## 4. Verhalten der Gruppen

| Gruppe | Installation | Neustart |
|---|---|---|
| GRP_Update1 | alle Updates | nur mit Zustimmung: Popup „Jetzt neu starten“ / „Zeitpunkt wählen“ (max. 24 h), Erinnerung alle 30 Min. |
| GRP_Update2 | alle Updates | Benutzer wählt sofort / 15 / 60 / 120 Min., spätestens nach 2 h automatisch (5 Min. vorher Hinweis) |
| GRP_Update3 | alle Updates | automatisch nach 5 Min. mit Hinweis und Knopf „Jetzt neu starten“ |

Ist niemand angemeldet, startet der PC direkt neu. Clients in mehreren Gruppen werden nicht aktualisiert (Konflikt im Dashboard).

Installationen starten über **Läufe**: im Gruppen-Tab „Ganze Gruppe installieren“ / „Ausgewählte installieren“
(optional mit Updatestart-Zeit) oder automatisch über **Zeitpläne** (wöchentlich oder Patchday + X Tage).
Clients, die offline sind, holen offene Läufe nach, sobald sie sich wieder melden.

## 5. Erster Test (Checkliste)

1. Server-MSI installieren, Dashboard öffnen → Tabs zeigen die Clients aus den drei Gruppen bzw. „Ohne Gruppe“.
2. Agent-MSI herunterladen und auf **einem** Test-Client manuell installieren.
3. Nach ≤ 5 Min.: Client zeigt im Dashboard Status, IP, Uptime, Benutzer; nach dem ersten Scan die Anzahl ausstehender Updates
   (Detailseite per Klick auf den Namen).
4. Test-Client in z. B. `GRP_Update3` aufnehmen (AD-Stand im Dashboard ggf. mit „AD neu laden“ aktualisieren),
   im Tab „Ausgewählte installieren“ → Installation, danach Hinweis und Neustart beobachten.
5. Logs bei Problemen: Client `C:\ProgramData\DNWsus\Agent\agent.log`, Server Ereignisanzeige → Anwendung (Quelle DnwSus.Server).
