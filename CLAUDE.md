# DNWsus-Client

Windows-Updates für **Clients** (keine Server) zentral steuern und überwachen: Webdienst auf einem Server pro Domain,
Agent auf den Clients. Ursprung war eine Kopie von DNWsus v1.0.28 (WinForms, WinRM-Push) – ab v2.0.0 komplett neu gebaut.
Keine Verbindung zum Original `D:\claude-projekte\dnwsus`, das bleibt unberührt.
Repo: https://github.com/DNWJENS/DNWsus-Client (privat, wird mit Kollegen geteilt).

## Regeln
- Gebaut wird immer mit `.\build.ps1 <Version>` (z. B. `.\build.ps1 2.1.0`). Ergebnis: `Releases\<Version>\DnwSus-Server-<Version>.msi`.
- Erst informieren und Vorgehen besprechen, dann umsetzen.
- **Auf dem Entwicklungsrechner nichts ausführen oder testen.** Er steht in einer abgekapselten Arbeitsumgebung.
  Erlaubt sind nur Code schreiben, bauen (`build.ps1`) und git. Gebaute Programme (Server, Agent, MSI) nie starten,
  keine AD-Abfragen, keine Zertifikate, Dienste, Ports oder sonstige Systemänderungen. Tests werden als Anleitung
  für eine Testumgebung beschrieben und dort vom Benutzer durchgeführt.
- Nach jedem abgeschlossenen Schritt: bauen, git commit, **git push** (Repo immer synchron halten).
- Keine verschleiert wirkenden Konstrukte (z. B. `powershell -EncodedCommand`/Base64, VBS-Starter, `conhost --headless`):
  ein Versuch damit (v1.0.35 im Original) wurde von Windows Defender gelöscht. Hilfs-Scripts immer als lesbare Datei.
- **Nichts Kundenspezifisches ins Repo**: keine Zertifikate, Servernamen, Passwörter. Alles Kundenspezifische entsteht
  erst bei der Installation beim Kunden.
- Für Benutzer auf den Clients darf nie ein PowerShell-/Konsolenfenster sichtbar sein.
- PowerShell-Scripts mit Umlauten als UTF-8 **mit BOM** speichern (sonst liest Windows PowerShell 5.1 sie falsch).

## Aufbau
- `src/DnwSus.Server` – ASP.NET Core .NET 10, self-contained, Windows-Dienst `DnwSusServer` (SYSTEM), HTTPS-Port **13162**.
  - Dashboard nur vom Server selbst und nur für Domänen-Admins (Negotiate; `DnwSus:AdminGroup` in appsettings.json).
  - Von Clients erreichbar nur `/api/agent/report` (Kerberos mit Computerkonto, Name muss zum Konto passen)
    und `/download/DnwSus-Agent.msi`. Siehe `Services/LocalAccess.cs`, `Api/AgentApi.cs`.
  - Zertifikat stellt der Server selbst aus (`Services/ServerCertificate.cs`, 10 Jahre, LocalMachine\My, FriendlyName
    "DNWsus Server", lokal in Root eingetragen). Agent vertraut nur dessen SHA-256-Fingerabdruck.
  - Daten: SQLite `C:\ProgramData\DNWsus\Server\dnwsus.db` (nur SYSTEM/Admins) – Clients, Läufe, Zeitpläne, Ereignisse.
  - Agent-MSI: neutrale Vorlage `agent\DnwSus-Agent.msi` im Installationsordner; `Services/AgentMsiService.cs` trägt
    `DNWSUS_SERVERURL`/`DNWSUS_CERTHASH` per msi.dll ein (eigener PackageCode) und liefert sie aus.
  - `Services/ScheduleService.cs` legt aus Zeitplänen (wöchentlich / Patchday + X Tage) Läufe an.
- `src/DnwSus.Agent` – .NET Framework 4.6.2 (läuft ab Server 2016 / allen Win10/11 ohne Nachinstallation), Windows-Dienst `DnwSusAgent` (SYSTEM). Meldet alle 5 Min. (`/api/agent/report`),
  Scan alle 4 h bzw. auf Anforderung, installiert offene Läufe über die WUA-COM-Schnittstelle (kein PowerShell),
  steuert den Neustart je Gruppe (`Worker.cs`). Konfiguration in `HKLM\SOFTWARE\DNWsus\Agent` (ServerUrl, CertHash),
  Zustand/Log in `C:\ProgramData\DNWsus\Agent`. Kein Selbst-Update (neue Versionen per GPO/MSI).
- `src/DnwSus.Hinweis` – WinForms-Popup (.NET Framework 4.6.2), vom Agenten per CreateProcessAsUser in der Benutzersitzung gestartet.
  Antwort per Exit-Code: 0 = später, 1 = jetzt, 1000+n = in n Minuten.
- `src/DnwSus.Server/Api/AgentContracts.cs` und `src/DnwSus.Agent/Contracts.cs` sind das Austauschformat – immer gemeinsam ändern.
- `setup/Agent`, `setup/Server` – WiX 5 (WixToolset.Sdk 5.0.2). `docs/Installation.md` – Installation, GPO, Testcheckliste.

## Gruppen (AD, verschachtelt über `memberOf:1.2.840.113556.1.4.1941:=`)
- **GRP_Update1**: alles installieren; Neustart nur mit Zustimmung. Popup „Jetzt“ / „Zeitpunkt wählen“ (max. 24 h),
  Erinnerung alle 30 Min.
- **GRP_Update2**: alles installieren; Benutzer wählt sofort / 15 / 60 / 120 Min., spätestens nach 2 h Neustart
  (5 Min. vorher Hinweis).
- **GRP_Update3**: alles installieren, bei Bedarf automatischer Neustart nach 5 Min. mit Hinweis + Knopf „Jetzt neu starten“.
- Niemand angemeldet: direkt neu starten. Keine Reboot-Zeit-Einstellung im Dashboard.
- Läufe pro Gruppe manuell (ganze Gruppe / ausgewählte Clients, optional Updatestart) oder per Zeitplan; offline Clients
  holen offene Läufe immer nach. Grundeinstellung: alle Updates (KB-Ausschlüsse/Testgruppen evtl. später).
- Tab „Ohne Gruppe“: aktive Client-Computerkonten in keiner Gruppe; veraltete (> 90 Tage keine AD-Anmeldung) grau.
  Clients in mehreren Gruppen = Konflikt, werden nicht aktualisiert.

## Update-Klassen und Regeln (ab v2.3.0)
- Der Agent **klassifiziert** jedes Update (`UpdateClassifier`): Definition / Security / Quality / Feature / Driver / Product / Other.
- Pro **Gruppe × Klasse** gilt eine Regel (`Services/UpdatePolicy.cs`, Tabelle `policies`, Standard wenn kein Eintrag):
  **Install** = immediate (Agent installiert selbst beim Scan) / schedule (nur per Zeitplan) / manual (nur per Dashboard-Lauf) / off;
  **Reboot** = template (Neustart wie Gruppe) / prompt / auto / none (kein erzwungener Neustart).
- Austauschformat: `AgentResponse.Policy` (Regeln der Gruppe), `UpdateItem.Class`, `InstallRun.Classes`, `InstallReport.InstalledClasses`.
- Agent (`Worker`): „immediate"-Klassen werden autonom installiert; Läufe installieren nur ihre `Classes`; Neustart je Stapel
  = sanftestes Verhalten der installierten Klassen (prompt→1, template→Gruppe, auto→3; alle „none" → kein Neustart).
- Zeitpläne haben eine **Klasse** (leer = alle „schedule"-Klassen der Gruppe); `ScheduleService` legt klassen-spezifische Läufe an.
- Manuelle Läufe (Gruppe installieren / Soforthilfe) sparen „off"-Klassen aus. Dashboard: **Regel-Editor** je Gruppen-Tab
  (Klasse × Install/Reboot) + Klassen-Auswahl im Zeitplan. Design-Referenz: `docs/mockups/dashboard-regeln.html`.
- **Offen (nächster Schritt):** kompletter optischer Umbau der übrigen Tabs (Karten/Badges wie im Mockup), bisher nur der Regel-Editor.

## Stand / offen
- Getestet und läuft: GPO-Verteilung (Zielgruppenadressierung nach GRP_Update1-3, selbstheilende Aufgabe
  `/c msiexec /i "\\…\DnwSus-Agent.msi" /qn /norestart` – **ohne** `if not exist`-Guard, sonst keine Upgrades!). Agent scannt
  direkt gegen Windows Update (ssWindowsUpdate). Dashboard im DNWironment-Design (Palette/Logo aus `D:\claude-projekte\DNWironment`
  bzw. `DNW-brand`), sortierbare Spalten, Agent-Version-Spalte.
- v2.1.9: Bulk-Soforthilfe („…“-Menü): Clients anhaken, Updatestart + Reboot-Zeit; manuelle, gruppenunabhängige Läufe,
  die das Gruppen-Neustart-Verhalten übersteuern. Agent-Format um `InstallRuns` (Id/Manual/RebootAt) erweitert.
- v2.2.0: **Server legt seine Agent-Freigabe selbst an.** `AgentShareService` erzeugt beim Start Ordner
  `C:\ProgramData\DNWsus\Share` (NTFS: Jeder lesen, SYSTEM/Admins voll) + öffentliche SMB-Freigabe `\\<server>\DNWsus`
  (`Services/WindowsShare.cs`, NetShareAdd/502) und legt die aktuelle Agent-MSI dort ab (beim Start + stündlich, fester Name).
  Pro Domäne nur noch **eine** GPO mit dem im Dashboard angezeigten Pfad. Firewall (SMB 445) macht der Admin. Ein abweichender
  Zielpfad (zentraler Dateiserver) ist im Dashboard als optionaler Override setzbar (Vorrang vor der Freigabe).
- v2.2.0: **MSI-Härtung** – Agent-`ServiceControl` nur noch `Stop=”uninstall”` (nicht `both`): gleichversionierter
  `msiexec /i` ist ein echtes No-Op, der Dienst läuft durch; beim Upgrade stoppt MajorUpgrade die alte Version.
- v2.2.0: **Serverumzug** ohne neue MSI. Am alten Server im Dashboard (Seite „Agent”) Adresse + SHA-256 des neuen Servers
  eintragen → `AgentResponse.MoveToUrl/MoveToCertHash` → der Agent prüft den neuen Server (erreichbar + Fingerabdruck passt),
  schreibt dann `HKLM\SOFTWARE\DNWsus\Agent` (ServerUrl/CertHash) um und meldet sich künftig dort. Offline-Clients ziehen nach.
  Notweg bleibt neue Agent-MSI per GPO.
- **Remote-Agent-Push aus dem Dashboard (ADMIN$ + WinRM/WMI o. Ä.) wird NICHT gebaut** – feste Grenze (laterale-Bewegung-Technik).
  Verteilung läuft über GPO; für Massen-Push ist das Kunden-RMM/Intune zuständig.
- v2.2.1: **Logo im Hinweis-Popup.** `DnwSus.Hinweis` zeigt im Kopf das DNW-Logo (transparent) + Wortmarke „DNWsus”
  in der Hausschrift **Ubuntu**; Schrift (Ubuntu-Regular/Bold, Ubuntu Font License) und Logo sind als Ressourcen
  eingebettet (`src/DnwSus.Hinweis/Resources`, per `PrivateFontCollection` geladen, Rückfall Segoe UI), weil Ubuntu
  auf Clients nicht installiert ist.
- v2.2.2: **Agent-Markierung entschärft.** Nur noch Agenten **unterhalb** `IndexModel.MinCompatibleAgentVersion`
  (derzeit 2.0.0) werden als „inkompatibel” markiert – entkoppelt von der Server-/Dashboard-Version, damit ein reines
  Server-Update nicht mehr alle Agenten fälschlich als „alt” flaggt. Die Mindestversion wird nur bei einer echten,
  nicht abwärtskompatiblen Änderung am Austauschformat erhöht.
- **Code-Signatur gestrichen** – Verteilung läuft ausschließlich per GPO, eine Signatur ist dafür nicht nötig.
