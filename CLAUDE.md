# DNWsus-Client

Eigenständiges Projekt (abgespalten von DNWsus v1.0.34, keine Verbindung mehr zum alten Projekt).
Tool zum Installieren von Windows-Updates per WinRM, C#/WinForms, .NET Framework 4.8.

## Regeln
- Gebaut wird immer mit `.\build.ps1 <Version>` (z. B. `.\build.ps1 1.0.1`). Die exe `dnwsus-client_v<Version>.exe` muss danach in `Releases\` liegen.
- Erst informieren und Vorgehen besprechen, dann umsetzen.
- Nach jedem abgeschlossenen Schritt: bauen, dann git commit.

## Stand (v1.0.0)
- Neue GUIDs, Name/Titel "DNWsus-Client", Version 1.0.0, build.ps1 angepasst.
- Kürzel auf den Zielrechnern: Scheduled Tasks `DNWC_*`, Dateien `C:\Windows\Temp\dnwc_*` (nie `WUM_`/`wum_*` verwenden – das ist DNWsus).
- Log: `Logs\dnwsus-client_JJJJ-MM-TT.csv`.
- `.gitignore` schließt bin/, obj/, Releases/, _archiv/ aus.

## Nächster Umbau (noch offen, erst besprechen)
- Rechner nach vorgegebenen AD-Gruppen statt nach OUs filtern (verschachtelt über `memberOf:1.2.840.113556.1.4.1941:=`).
- Clients statt Server (Filter `operatingSystem=*Server*` in `AdDiscoveryService` entfernen/umstellen).
- Offene Fragen: Wie werden die Gruppen vorgegeben (Code / Config-Datei / Namensmuster)? Neustart-Verhalten bei Clients (Vorwarnung für angemeldete Benutzer?).
- Bekannter Fehler: Sofort- und geplante Installation nutzen dieselben festen Dateinamen in `C:\Windows\Temp` und können sich gegenseitig die Dateien löschen → Dateinamen pro Auftrag eindeutig machen.
