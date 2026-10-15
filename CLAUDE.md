# DNWsus-Client

Eigenständiges Projekt, 1:1-Kopie des Codes von DNWsus v1.0.28 (= v1.0.34, inhaltlich identisch).
Keine Verbindung mehr zum Original `D:\claude-projekte\dnwsus` – das Original bleibt unberührt.
Tool zum Installieren von Windows-Updates per WinRM, C#/WinForms, .NET Framework 4.8.

## Regeln
- Gebaut wird immer mit `.\build.ps1 <Version>` (z. B. `.\build.ps1 1.0.35`). Die exe `dnwsus_v<Version>.exe` muss danach in `Releases\` liegen.
- Erst informieren und Vorgehen besprechen, dann umsetzen.
- Nach jedem abgeschlossenen Schritt: bauen, dann git commit.
- Keine verschleiert wirkenden Konstrukte einbauen (z. B. `powershell -EncodedCommand`/Base64): ein Versuch damit (v1.0.35 im Original) wurde von Windows Defender zur Laufzeit gelöscht. Remote-Scripts immer als lesbare Datei + `-File` ausführen, wie im bestehenden Code.

## Stand
- Code exakt wie v1.0.28/v1.0.34 (Titel "DNWSUS", Kürzel auf Zielrechnern `WUM_*` / `C:\Windows\Temp\wum_*`). Original und Client laufen nie auf denselben Systemen, daher keine Umbenennung nötig.
- `.gitignore` schließt bin/, obj/, Releases/, _archiv/ aus.

## Nächster Umbau (noch offen, erst besprechen)
- Rechner nach vorgegebenen AD-Gruppen statt nach OUs filtern (verschachtelt über `memberOf:1.2.840.113556.1.4.1941:=`).
- Clients statt Server (Filter `operatingSystem=*Server*` in `AdDiscoveryService` entfernen/umstellen).
- Offene Fragen: Wie werden die Gruppen vorgegeben (Code / Config-Datei / Namensmuster)? Neustart-Verhalten bei Clients (Vorwarnung für angemeldete Benutzer?).
- Bekannter Fehler (auch im Original): Sofort- und geplante Installation nutzen dieselben festen Dateinamen in `C:\Windows\Temp` und können sich gegenseitig die Dateien löschen/überschreiben → Dateinamen pro Auftrag eindeutig machen.
- Bekannter Fehler (auch im Original): Bei geplanter Installation wird ein eingestellter Neustart nicht angelegt (`UpdateInstallerService`, Zweig für `InstallScheduledAt` kehrt vor der Reboot-Behandlung zurück).
