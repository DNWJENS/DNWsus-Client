# DNWsus-Client

Windows-Updates für **Clients** (keine Server) zentral steuern und überwachen.
Ursprung: 1:1-Kopie von DNWsus v1.0.28 (WinForms, WinRM-Push) – wird gerade komplett neu gebaut.
Keine Verbindung zum Original `D:\claude-projekte\dnwsus`, das bleibt unberührt.
Repo: https://github.com/DNWJENS/DNWsus-Client (privat, wird mit Kollegen geteilt).

## Regeln
- Gebaut wird immer mit `.\build.ps1 <Version>` (z. B. `.\build.ps1 2.0.0`). Ergebnis liegt in `Releases\<Version>\`.
- Erst informieren und Vorgehen besprechen, dann umsetzen.
- Nach jedem abgeschlossenen Schritt: bauen, git commit, **git push** (Repo immer synchron halten).
- Keine verschleiert wirkenden Konstrukte (z. B. `powershell -EncodedCommand`/Base64, VBS-Starter, `conhost --headless`):
  ein Versuch damit (v1.0.35 im Original) wurde von Windows Defender gelöscht. Remote-/Hilfs-Scripts immer als lesbare Datei.
- **Nichts Kundenspezifisches ins Repo**: keine Zertifikate, Servernamen, Passwörter. Alles Kundenspezifische entsteht
  erst bei der Installation beim Kunden.
- Für Benutzer auf den Clients darf nie ein PowerShell-/Konsolenfenster sichtbar sein.

## Architektur (beschlossen)
- **DnwSus.Server** (`src/DnwSus.Server`, ASP.NET Core .NET 10, self-contained, Windows-Dienst `DnwSusServer`):
  - Läuft auf genau einem Server pro Domain (Windows Server 2019+), HTTPS-Port **13162**.
  - Dashboard nur vom Server selbst erreichbar und nur für Domänen-Admins (Windows-Anmeldung/Negotiate).
    Einstellbar über `DnwSus:AdminGroup` in appsettings.json.
  - Von Clients erreichbar nur `/api/agent/*` (Agent-Schnittstelle, Kerberos mit Computerkonto) und `/download/*` (MSI).
  - Zertifikat: stellt der Server beim ersten Start selbst aus (10 Jahre, LocalMachine\My, FriendlyName "DNWsus Server"),
    trägt es lokal in Root ein. Die Agent-MSI enthält URL + Fingerabdruck, der Agent vertraut nur genau diesem Zertifikat.
  - Bis zur Server-MSI: Installation mit `setup\install-server.ps1` (als Admin).
- **DnwSus.Agent** (geplant, .NET Framework 4.8, SYSTEM-Aufgabe): holt Einstellungen per HTTPS, meldet Status,
  installiert über die Windows-Update-API (kein PowerShell). Meldet sich alle 5 Min., voller Scan ca. alle 4 Std.
  Verteilung per MSI (GPO oder manuell), MSI wird im Dashboard erzeugt. Kein Selbst-Update (Updates kommen per GPO).
- **DnwSus.Hinweis** (geplant): Popup-exe im Benutzerkontext, Logo eingebettet.

## Gruppen (AD, verschachtelt über `memberOf:1.2.840.113556.1.4.1941:=`)
- **GRP_Update1**: alles installieren; Neustart nur mit Zustimmung. Popup „Jetzt“ / „Selbst wählen“ (max. 24 h),
  Erinnerung alle 30 Min.
- **GRP_Update2**: alles installieren; Benutzer wählt sofort / 15 / 60 / 120 Min., spätestens nach 2 h Neustart
  (5 Min. vorher Hinweis).
- **GRP_Update3**: alles installieren, bei Bedarf automatischer Neustart mit Hinweis „in 5 Minuten“ + Knopf „Jetzt neu starten“.
- Ist niemand angemeldet: direkt neu starten. Keine Reboot-Zeit-Einstellung im Dashboard.
- Läufe pro Gruppe manuell auslösbar (ganze Gruppe oder einzelne Clients) und per festem Zeitplan; offline Clients werden
  immer nachgezogen. Grundeinstellung: alle Updates (KB-Ausschlüsse/Testgruppen evtl. später).
- Tab „Ohne Gruppe“: aktive Client-Computerkonten in keiner der drei Gruppen; veraltete (> 90 Tage keine AD-Anmeldung)
  grau, per Filter ausblendbar. Clients in mehreren Gruppen = Konflikt, werden nicht aktualisiert.

## Etappen
1. ✔ Grundgerüst: Server als Dienst, eigenes Zertifikat, Zugriffsschutz, Tabs aus dem AD (v2.0.0)
2. Agent + MSI-Erzeugung im Dashboard, Statusmeldung (Updates, Uptime, IP, Benutzer, letzter Kontakt, Fehler), GPO-Anleitung
3. Gruppen-Einstellungen, Läufe/Zeitpläne, Installation durch den Agenten
4. Hinweis-exe mit Popups
5. Alten WinForms-Code (`WindowsUpdateManager/`) entfernen, Server-MSI, Logo (transparente Version kommt vom Benutzer), Feinschliff

## Altbestand (bis Etappe 5)
- `WindowsUpdateManager/`: alter WinForms-Code v1.0.34, nur noch als Referenz (WUA-Scripts, AD-Logik).
- Bekannte Fehler dort (für den Neubau beachten): feste Dateinamen in `C:\Windows\Temp` bei Sofort-/geplanter Installation;
  Neustart wird bei geplanter Installation nicht angelegt.
