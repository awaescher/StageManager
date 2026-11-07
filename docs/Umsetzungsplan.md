# Umsetzungsplan: StageManager an stage-manager-for-windows angleichen

Dieser Plan übernimmt die **Techniken** aus `..\stage-manager-for-windows` (Go/MyGo) in unser C#/WPF-Projekt. Code wird nicht übernommen, nur die Win32-Verfahren und Konzepte. Die Phasen bauen aufeinander auf; innerhalb einer Phase sind die Aufgaben weitgehend unabhängig.

| Phase | Inhalt | Aufwand | Voraussetzung |
|---|---|---|---|
| 0 | Vorbereitung: CI, Logging, Datenordner | klein | – |
| 1 | Parking (Wegschieben) + Wiederherstellungs-Journal + DWM-Übergänge | mittel | 0 |
| 2 | Fenster-Erkennung: Filter, Gruppierung nach exe-Pfad, UWP, Icons, Hänger-Schutz | mittel | 0 |
| 3 | Testbares Szenen-Modell + Wechsellogik wie auf macOS | mittel | 2 |
| 4 | Einstellungen (JSON), Seite der Leiste, Anzahl + Thumbnail-Größe, Desktop-Symbole, Einstellungsfenster, optional AppBar | mittel–groß | 0 |
| 5 | Standbilder per PrintWindow | mittel | 1 |
| 6 | Übergangsanimationen (Overlay) | groß | 1, 5 |
| 7 | Mehrere Monitore | groß | 3, 4 |

Nicht übernommen werden: Wechsel auf Go/MyGo, Milchglas/Farbtönung per Bildschirm-Kopie, Taskleisten-Vorschau über UI Automation, Win+Tab-Tastatur-Hook, Hot Corner, Update-Check.

---

## Phase 0 – Vorbereitung

### 0.1 CI reparieren
- `.github/workflows/dotnet-desktop.yml`: `dotnet-version: 7.0.x` → `8.0.x`.
- Zusätzlich einen Schritt `dotnet test` vorsehen (greift ab Phase 3).
- **Fertig, wenn:** ein Push auf `main` ein Release mit `StageManager.exe` erzeugt.

### 0.2 Datenordner und Logging
- Neue Klasse `AppPaths` mit `DataDirectory = %LOCALAPPDATA%\StageManager` (wird beim Start angelegt).
- Einfaches Datei-Log (`debug.log`, rotierend, z. B. max. 1 MB) über eine kleine statische `Log`-Klasse; keine neue Abhängigkeit nötig.
- Globale Fehlerbehandlung in `App.xaml.cs`: `AppDomain.CurrentDomain.UnhandledException`, `DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException` → loggen und (ab Phase 1) Fenster zurückholen.
- **Fertig, wenn:** Ausnahmen in `debug.log` landen.

### 0.3 Aufräumen
- Ungenutzte workspacer-Reste entfernen oder markieren (`ToggleFocusedWindowTiling`, `_floating`, `ExternalWindow*`, ungenutzte NuGet-Referenz `WpfScreenHelper`).
- `DeferWindowPos`-Wrapper behalten – sie werden in Phase 1 nützlich.

---

## Phase 1 – Parking statt Minimieren

**Ziel:** Inaktive Fenster werden knapp außerhalb des sichtbaren Bereichs geparkt. Es gibt keine Minimier-Animation, und `DwmRegisterThumbnail` und `PrintWindow` liefern weiterhin ein gültiges Bild.

> **Messung (Phase-1-Vorversuch, mit Edge und einem WPF-Fenster):**
> - Ein per `SetWindowPos` auf x = −4000/−6000 geschobenes Fenster behält ein gültiges DWM-Thumbnail. Das gilt auch für ein Thumbnail, das **erst nach** dem Wegschieben registriert wird.
> - Der Inhalt ist nach dem Wegschieben **eingefroren** auf dem letzten Stand. Chromium (Edge) hört auf zu zeichnen, `PrintWindow` liefert ebenfalls nur noch das letzte Bild, und eine Änderung nach dem Parken kam erst nach dem Zurückholen an.
> - Folgen: Die Seitenleiste zeigt für geparkte Fenster das **letzte Bild vor dem Parken** (kein Live-Bild). Das reicht für die Leiste, ein Bild per `PrintWindow` ist dafür nicht nötig. Es wird erst für die Animationen (Phase 6) gebraucht und muss **vor** dem Parken aufgenommen werden.
> - Noch offen: maximierte Fenster, Visual Studio, Teams und UWP-Apps wurden nicht geprüft.

### 1.1 `ScreenOffsetWindowStrategy` implementieren
Datei: `StageManager/Strategies/ScreenOffsetWindowStrategy.cs`

**Hide(window):**
1. Ist das Fenster minimiert → nichts tun (minimierte Fenster bleiben der Taskleiste überlassen) bzw. per `GetWindowPlacement` nur die wiederhergestellte Position merken.
2. Aktuelle Position per `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` bzw. `GetWindowRect` lesen.
3. Parkposition berechnen (`ParkingSpot`, siehe 1.2).
4. Eintrag ins Journal schreiben (1.3) – **vor** dem Verschieben.
5. `SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS | SWP_NOREDRAW)`.
6. Fenster, die diesem Fenster gehören (`GetWindow(GW_OWNER) == h`, per `EnumWindows` gesucht), um denselben Versatz mitverschieben.

**Show(window):**
1. Originalposition aus dem Journal holen.
2. `SetWindowPos(..., SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_FRAMECHANGED)` – `SWP_FRAMECHANGED` sorgt dafür, dass Chromium-Fenster ihre runden Ecken behalten.
3. Eigene Fenster mitbewegen.
4. Journal-Eintrag entfernen.
5. Z-Reihenfolge wiederherstellen (siehe 1.4).

Umstellen in `SceneManager.cs`, Zeile 25:
```csharp
private IWindowStrategy WindowStrategy { get; } = new ScreenOffsetWindowStrategy(parkingJournal);
```
Die alte Strategie bleibt als Rückfalloption (später per Einstellung wählbar, Phase 4).

### 1.2 Parkposition berechnen (`ParkingSpot`)
- Virtuellen Bildschirm per `GetSystemMetrics(SM_XVIRTUALSCREEN=76, SM_YVIRTUALSCREEN=77, SM_CXVIRTUALSCREEN=78, SM_CYVIRTUALSCREEN=79)` ermitteln.
- Kandidaten: 160 px jenseits jeder Kante (links, rechts, oben, unten), Fenster bleibt dabei auf derselben Höhe/Breite ausgerichtet.
- Nur einen Kandidaten nehmen, bei dem `MonitorFromRect(neuesRect, MONITOR_DEFAULTTONULL)` **denselben Monitor** wie vorher liefert. Dazu reicht es, wenn ein kleiner Streifen des Fensters (wenige Pixel, außerhalb des sichtbaren Bereichs nicht relevant) rechnerisch den Monitor schneidet – ansonsten die Variante mit der größten Überlappung wählen. Grund: Wechselt der Monitor, skaliert Windows das Fenster bei unterschiedlicher DPI neu.
- Reine Funktion ohne Win32-Zugriff (Monitor-Rechtecke als Parameter) → unit-testbar.

### 1.3 Wiederherstellungs-Journal
Neue Klasse `Parking/ParkingJournal.cs`:
- Datei: `%LOCALAPPDATA%\StageManager\parked-windows.json`.
- Eintrag: `Handle` (als long), `ProcessId`, `ProcessPath`, `Title`, `OriginalRect`, `ParkedRect`, `Timestamp`.
- Zusätzlich ein Feld `DesktopIconsOriginallyVisible` (siehe Phase 4.4), damit auch der Zustand der Desktop-Symbole nach einem Absturz wiederhergestellt wird.
- Atomar schreiben: in `*.tmp` schreiben, dann `File.Replace`/`File.Move(overwrite: true)`.
- Thread-sicher (Lock), Schreiben gebündelt (z. B. max. alle 100 ms), beim Beenden sofort.

**Wiederherstellen (`RestoreAll`)** aufrufen bei:
- Programmstart (`Recover`): Für jeden Eintrag prüfen, ob `IsWindow(h)` noch existiert **und** PID passt (Handles werden wiederverwendet!) → zurück auf `OriginalRect`. Danach Journal leeren.
- `SceneManager.Stop()` (statt nur `Show` aufzurufen).
- `WM_ENDSESSION` / `SystemEvents.SessionEnding`.
- Unbehandelten Ausnahmen (Phase 0.2).
- Neuer Tray-Eintrag „Alle Fenster zurückholen“.

### 1.4 Z-Reihenfolge und hängende Apps
- Beim Anzeigen einer Szene die Fenster in gespeicherter Reihenfolge mit `SetWindowPos(HWND_TOP)` übereinanderlegen; optional gebündelt über die vorhandenen `BeginDeferWindowPos`/`DeferWindowPos`/`EndDeferWindowPos`-Wrapper.
- Vor jedem synchronen Aufruf `IsHungAppWindow(h)` prüfen; hängende Fenster immer asynchron (`SWP_ASYNCWINDOWPOS`) behandeln.
- Längere Arbeit nicht auf dem UI-Thread ausführen.

### 1.5 Ausbrecher und Nachzügler
- **Ausbrecher:** Manche Apps setzen sich selbst wieder auf den Bildschirm (z. B. nach eigenem Restore). Bei `EVENT_OBJECT_LOCATIONCHANGE` für ein geparktes Fenster prüfen, ob es wieder sichtbar ist → aus dem Journal nehmen und der aktuellen Szene zuordnen (oder erneut parken, je nach Einstellung).
- **Nachzügler:** Nach dem Zurückholen mit kurzem Abstand (z. B. 150 ms und 500 ms) prüfen, ob das Fenster angekommen ist; sonst erneut senden. Liegt es danach immer noch außerhalb, auf den nächsten Monitor-Arbeitsbereich einpassen (`FitOnScreen`).

### 1.6 Windows-eigene Übergänge abschalten
- `DwmSetWindowAttribute(h, DWMWA_TRANSITIONS_FORCEDISABLED = 3, true)` kurz vor dem Zurückholen setzen, nach ca. 700 ms wieder auf `false`.
- Für die eigenen Fenster (Seitenleiste, später Overlay) dauerhaft setzen.
- `DWMWINDOWATTRIBUTE` und `NativeMethods` um `DwmSetWindowAttribute` ergänzen.

### 1.7 Folgeanpassungen
- `OverlapCheck` in `MainWindow` ignoriert geparkte Fenster (sie liegen außerhalb und würden sonst falsch gezählt).
- `IsCandidate`/`CanLayout`: geparkte Fenster müssen weiterhin als Kandidaten gelten.
- Fenster, die der Benutzer per Drag in eine andere Szene zieht (`StoreLastLocation`/`PopLastLocation`), über das Journal statt über separate Speicherung behandeln.

**Fertig, wenn:**
- Szenenwechsel ohne Minimier-Animation funktioniert.
- `taskkill /f` auf StageManager + Neustart alle Fenster zurückbringt.
- Fenster auf einem zweiten Monitor mit anderer DPI nach dem Zurückholen dieselbe Größe haben.
- Visual Studio, Chrome, Teams, Explorer und eine UWP-App (Einstellungen) korrekt geparkt und zurückgeholt werden.

**Risiken:** Apps, die sich gegen Verschieben wehren oder ihre Position speichern (beim Schließen im geparkten Zustand würde die Parkposition gespeichert). Gegenmaßnahme: bei `EVENT_OBJECT_DESTROY`/`WM_CLOSE` eines geparkten Fensters lässt sich das nicht verhindern – daher Fenster vor dem Schließen über das eigene UI erst zurückholen und in der ReadMe dokumentieren.

---

## Phase 2 – Fenster-Erkennung

### 2.1 Strengere Filter
Datei: `Native/PInvoke/Win32Helper.cs`, `Native/WindowsWindow.cs` (`IsCandidate`, `CanLayout`)
- Nur Hauptfenster: `GetAncestor(h, GA_ROOT) == h`.
- `WS_EX_TOOLWINDOW` nur zulassen, wenn zugleich `WS_EX_APPWINDOW` gesetzt ist.
- Mindestgröße des sichtbaren Rahmens (`DWMWA_EXTENDED_FRAME_BOUNDS`): 40 × 30 px.
- Fenster, die einem anderen Fenster gehören, ausschließen, wenn ihr Besitzer bereits gelistet ist (sie wandern mit dem Besitzer).
- Klassen-Ausschlussliste ergänzen: `Shell_SecondaryTrayWnd`, `Windows.UI.Core.CoreWindow`, `XamlExplorerHostIslandWindow`, `DesktopWindowContentBridge`.
- Optionale Entwickler-Umgebungsvariable `STAGEMANAGER_APP_FILTER` (Liste von exe-Namen), um beim Testen nur bestimmte Apps zu verwalten.

### 2.2 Prozess- und App-Identität
- `Process.MainModule` ersetzen durch `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageNameW`. Vorteile: funktioniert auch bei Prozessen mit erhöhten Rechten (kein „Access denied“), ist deutlich schneller.
- Gleiches in `WindowsManager.cs` Zeile 103 (`SetWindowsHookEx` – dort reicht `GetModuleHandle(null)`).
- Neue Eigenschaft `IWindow.AppKey`: vollständiger exe-Pfad in Großbuchstaben.
- Anzeigename aus `FileVersionInfo.FileDescription`, Rückfall auf Dateiname.
- **UWP:** Ist die Klasse `ApplicationFrameWindow`, per `EnumChildWindows` das Kind `Windows.UI.Core.CoreWindow` suchen und dessen PID verwenden. Ist es (noch) nicht vorhanden, Fenster später erneut prüfen (`EVENT_OBJECT_NAMECHANGE`/Refresh). Damit ist das TODO in `WindowsWindow` erledigt.

### 2.3 Gruppierung nach exe-Pfad
- `SceneManager.GetWindowGroupKey` (Zeile 299): `window.ProcessName` → `window.AppKey`.
- `FindSceneForProcess` entsprechend umbenennen/anpassen.
- Effekt: Zwei verschiedene Programme mit gleichem Prozessnamen werden getrennt, UWP-Apps nicht mehr alle unter `ApplicationFrameHost` zusammengefasst.

### 2.4 Icons ohne Hänger
Datei: `WindowsWindow.ExtractIcon`, `WindowModel.Icon`
1. `SendMessageTimeout(h, WM_GETICON, ICON_BIG, 0, SMTO_ABORTIFHUNG, 100 ms)`, dann `ICON_SMALL2`, `ICON_SMALL`.
2. `GetClassLongPtr(h, GCLP_HICON)`.
3. Rückfall: `Icon.ExtractAssociatedIcon(exePfad)` bzw. `PrivateExtractIconsW` in höherer Auflösung.
- Ergebnis pro `AppKey` cachen.
- Für UWP: Icon aus dem Paket (`AppxManifest`) ist optional; vorerst Rückfall auf das Fenster-Icon.

### 2.5 Event-Hooks schärfen
- `SetWinEventHook` mit `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS` aufrufen – eigene Fenster erzeugen dann keine Ereignisse.
- `EVENT_OBJECT_NAMECHANGE` ergänzen (Fenster ohne Titel bekommen ihn oft erst später; UWP).
- Fokus-Wechsel mit kurzer Beruhigungszeit (ca. 70 ms) entprellen, damit schnelles Alt+Tab nicht mehrere Szenenwechsel auslöst.
- Fallback-Abgleich alle 1,5 s (`EnumWindows`), falls Ereignisse verloren gehen.

**Fertig, wenn:** Taskleiste, Startmenü, Benachrichtigungen, IME-Fenster und Tooltips nie als Szene erscheinen, UWP-Apps eigene Szenen bekommen und Prozesse mit Admin-Rechten ein Icon haben.

---

## Phase 3 – Testbares Szenen-Modell

### 3.1 Modell ohne Win32 herauslösen
- Neues Projekt `StageManager.Core` (Class Library, `net8.0`, ohne WPF) mit `StageModel`:
  - arbeitet nur mit `string AppKey` und abstrakten Fenster-IDs, keine `IntPtr`/`IWindow`.
  - Zustand: `Stage` (Apps auf der Bühne), `Sets` (Karten in der Seitenleiste, oberste zuerst; eine Karte kann mehrere Apps enthalten), `Recent` (Rückfall, wenn die Bühne leer wird), `ChangedAt` (Beruhigungszeit 1 s, ersetzt die bisherige „Reentrancy-Sperre“).
- `SceneManager` wird zur dünnen Schicht: übersetzt Win32-Ereignisse in Modell-Aufrufe und Modell-Änderungen in `WindowStrategy.Show/Hide`.
- `Scene`/`SceneModel` werden aus dem Modell abgeleitet.

### 3.2 Wechsellogik wie auf macOS
- `Switch(set)`: Die Apps, die die Bühne verlassen, bilden **eine** Karte und nehmen den Platz der hereinkommenden Karte ein (statt ans Ende zu wandern).
- `Join(app)` (Shift+Klick auf Karte, Drag einer Karte auf die Bühne, Kontextmenü „Zur Bühne hinzufügen“): App kommt zur aktuellen Bühne dazu.
- `Split(app)`: App aus einer Karte lösen (Kontextmenü).
- Neues Fenster einer App, die schon auf der Bühne ist → bleibt auf der Bühne; einer unbekannten App → neue Karte oben, Wechsel dorthin (wie bisher).
- „Begleit-Apps“ (Einstellung, Phase 4): Liste von Apps, die beim Öffnen zur aktuellen Bühne kommen statt sie zu ersetzen (z. B. Rechner, Notizen, Teams-Anrufe).
- Leere Bühne → letzte Karte aus `Recent` oder Desktop-Ansicht.
- `MAX_SCENES` bleibt als Anzeige-Grenze, nicht als Modell-Grenze.

### 3.3 Tests
- Neues Projekt `StageManager.Tests` (xUnit), in die Solution aufnehmen, in der CI ausführen.
- Testfälle: Wechsel, Rückkehr, Join, Split, Schließen der letzten App, neue App, Beruhigungszeit, Begleit-Apps.
- Zusätzlich Tests für `ParkingSpot` (Phase 1.2) und das Journal (Serialisierung, atomares Schreiben).

**Fertig, wenn:** die gesamte Szenen-Logik ohne laufendes Windows getestet ist und `SceneManager` keine eigene Gruppierungslogik mehr enthält.

---

## Phase 4 – Einstellungen, Seitenleisten-Layout, Desktop-Symbole, AppBar

Diese Phase hängt nur von Phase 0 ab und ist für den Alltag besonders wichtig. Sie kann deshalb **direkt nach Phase 0** umgesetzt werden (siehe Reihenfolge der Pull Requests). Die Teile 4.1–4.5 sind der Kern; 4.6 (AppBar) ist optional.

### 4.1 Einstellungen als JSON (Infrastruktur)
- `Settings`-Klasse (System.Text.Json), Datei `%LOCALAPPDATA%\StageManager\settings.json`, atomar gespeichert (erst `*.tmp`, dann ersetzen), mit Standardwerten, `Normalize()` (Bereiche prüfen, ungültige Werte auf Standard zurücksetzen) und Versionsfeld für Migrationen.
- `SettingsService` (Singleton):
  - hält die aktuellen `Settings`,
  - `Update(Action<Settings>)` speichert und löst `Changed` aus,
  - Verbraucher (`MainWindow`, `SceneManager`, später Overlay) abonnieren `Changed` und wenden Änderungen **sofort ohne Neustart** an.
- Eine kaputte oder fehlende Datei darf den Start nicht verhindern: Standardwerte verwenden, alte Datei als `settings.json.bad` sichern und im Log vermerken.
- Felder (Stand dieser Phase, weitere folgen in späteren Phasen):

| Feld | Werte | Standard | Ersetzt im Code |
|---|---|---|---|
| `Sidebar.Side` | `Left` \| `Right` | `Left` | `Left = 0` in `OnRenderSizeChanged`, `X <= 44`, `_lastWidth`-Vergleiche |
| `Sidebar.Width` | `Auto` oder 160–480 (DIP) | `Auto` | `Width="280"` in `MainWindow.xaml` |
| `Sidebar.MaxScenes` | 1–12 | 6 | `MAX_SCENES = 6` in `MainWindow.xaml.cs` |
| `Sidebar.MaxWindowsPerScene` | 1–6 | 3 | bisher unbegrenzt in einer Karte gestapelt |
| `Sidebar.OverflowBehavior` | `HideInSidebar` \| `Release` | `HideInSidebar` | bisher nur Ausblenden in der Leiste |
| `Sidebar.ThumbnailSize` | `Auto` oder `{Width, Height}` (DIP) | `Auto` | `MinHeight=100`, `MinWidth=150` in `MainWindow.xaml` |
| `Sidebar.ThumbnailAspect` | z. B. 1.6 (16:10) | 1.6 | – |
| `Sidebar.Mode` | `Floating` \| `Fixed` | `Floating` | (AppBar, 4.6) |
| `DesktopIcons.Mode` | `Manage` \| `AlwaysHidden` \| `AlwaysVisible` | `Manage` | `Desktop.HideIcons()/ShowIcons()` in `SceneManager` |
| `DesktopIcons.RestoreOnExit` | `true` \| `false` | `true` | `_desktop.ShowIcons()` in `SceneManager.Stop` |
| `EnableWindowPullToScene` | bool | `true` | Feld in `MainWindow` |
| `EnableWindowDropToScene` | bool | `false` | Feld in `MainWindow` |
| `ClickDesktopClears` | bool | `true` | bisher fest |
| `HideStrategy` | `Park` \| `Minimize` | `Park` (nach Phase 1) | `SceneManager.WindowStrategy` |
| `Companions`, `ExcludedApps` | Listen von AppKeys | leer | (Phase 3 bzw. 2) |
| `Animations.Enabled`, `Animations.Speed` | bool, 0.5–2.0 | `true`, 1.0 | (Phase 6) |

Beispiel:
```json
{
  "Version": 1,
  "Sidebar": {
    "Side": "Right",
    "Width": "Auto",
    "MaxScenes": 5,
    "MaxWindowsPerScene": 3,
    "OverflowBehavior": "HideInSidebar",
    "ThumbnailSize": "Auto",
    "ThumbnailAspect": 1.6,
    "Mode": "Floating"
  },
  "DesktopIcons": { "Mode": "Manage", "RestoreOnExit": true }
}
```
- Autostart bleibt in der Registry (`AutoStart.cs`), wird aber im Einstellungsfenster angezeigt.
- Änderungen an der Datei zur Laufzeit per `FileSystemWatcher` übernehmen (optional, mit Entprellung).

### 4.2 Option: Seite der Leiste (links / rechts)

**Geometrie in eine reine Klasse auslagern** (`SidebarGeometry`, ohne WPF/Win32, unit-testbar). Alle Stellen, die heute „links“ voraussetzen, laufen künftig darüber:

| Heute (`MainWindow.xaml(.cs)`) | Künftig |
|---|---|
| `OnRenderSizeChanged`: `Left = 0; Top = 0; Height = area.Height` | `Left` = Arbeitsbereich links bzw. `Right − Width`; `Top` = Arbeitsbereich oben |
| `ApplyWindowMode`: `OffScreen` = `-Width`, Animation von/nach `Left` | `OffScreen` = `−Width` (links) bzw. `Right` des Arbeitsbereichs (rechts); Animationsrichtung gespiegelt |
| `_hook_MouseMoved`: `e.Data.X <= 44` | links: `x ≤ Links + 44`, rechts: `x ≥ Rechts − 44` |
| `UpdateModeByWindows`: `loc.X * 2 < _lastWidth`, `_mouse.X <= _lastWidth` | gespiegelt: Fensterrechtskante `> Rechts − Breite/2`, Maus `≥ Rechts − Breite` |
| `OnMouseReleased`: `e.Data.X > _lastWidth` (Fenster aus Leiste herausziehen) | links: `x > Links + Breite`, rechts: `x < Rechts − Breite` |
| `MainWindow.xaml`: `HorizontalAlignment="Left"`, `Padding="12,0,0,0"` | Ausrichtung und Innenabstand an `Side` binden (Trigger oder Code) |

Weitere Punkte:
- **Bestehende Fehler gleich mitbeheben:** `GetMonitorWorkSize()` liefert physische Pixel, wird aber auf `Height` (DIP) gesetzt, und `Left = 0` setzt den primären Monitor voraus. Künftig: Arbeitsbereich des Monitors in physischen Pixeln lesen, **einmal** in DIP umrechnen (`VisualTreeHelper.GetDpi`) und Position über `SetWindowPos` oder `Left/Top` mit den umgerechneten Werten setzen. Dasselbe Prinzip wie in Phase 7.1.
- **Seitenwechsel zur Laufzeit:** Leiste fährt kurz hinaus (Animation aus `ApplyWindowMode`), wechselt Seite, fährt auf der neuen Seite wieder hinein. Mit `Animations.Enabled = false` springt sie sofort.
- **Schatten und Spiegelung:** `DropShadowEffect` ist richtungslos und braucht keine Änderung; Karten werden zur Bildschirmkante hin ausgerichtet (rechts: rechtsbündig).
- **Kontextmenü** im Tray: Untermenü „Seite“ mit „Links“ / „Rechts“ (Häkchen), als Schnellzugriff neben dem Einstellungsfenster.
- **Tests (`SidebarGeometry`):** je Seite Position, OffScreen-Ziel, Auslösezone, Überlappungsprüfung und „herausgezogen“; zusätzlich Arbeitsbereich mit negativem Ursprung (Monitor links vom Hauptmonitor).

### 4.3 Option: Anzahl der verwalteten Fenstergruppen und automatische Thumbnail-Größe

**Begriffe:** Eine *Fenstergruppe* ist eine Karte in der Leiste (heute eine `Scene`, später ein `Set`), sie kann mehrere Fenster derselben App enthalten. Die Einstellung `MaxScenes` legt fest, wie viele Karten gleichzeitig in der Leiste stehen; `MaxWindowsPerScene` begrenzt, wie viele Fenster pro Karte übereinandergestapelt gezeigt werden.

**Verhalten bei Überlauf** (mehr Gruppen als `MaxScenes`), per `OverflowBehavior`:
- `HideInSidebar` (Standard, entspricht dem heutigen Verhalten): überzählige Gruppen bleiben verwaltet (werden also geparkt/versteckt), sind aber nur über Alt+Tab, Taskleiste oder den Fokuswechsel erreichbar. Die jeweils am längsten nicht benutzten werden ausgeblendet (`SyncVisibilityByUpdatedTimeStamp`).
- `Release`: die am längsten nicht benutzte Gruppe wird **aus der Verwaltung entlassen** – ihre Fenster werden an der Originalposition wiederhergestellt (Phase 1: aus dem Journal) und danach nicht mehr beeinflusst, bis sie wieder in den Vordergrund kommen.

**Umbau im Code:**
- `MAX_SCENES` in `MainWindow.xaml.cs` entfällt. Die zwei verschiedenen Zählweisen (`i <= MAX_SCENES` in `AddInitialScenes`, `i < MAX_SCENES` in `SyncVisibilityByUpdatedTimeStamp`) werden zu **einer** Methode, die sowohl beim Start als auch bei Änderungen der Einstellung läuft.
- `Sidebar.MaxWindowsPerScene` wirkt in `SceneModel`: es werden nur die zuletzt benutzten Fenster der Gruppe als Thumbnail gezeigt, die übrigen nur als Icons in der Icon-Reihe.

**Thumbnail-Größe aus der Anzahl berechnen** (`SidebarLayout.Calculate(...)`, reine Funktion, unit-testbar):

Eingaben: Höhe des Arbeitsbereichs in DIP `H`, Breite der Leiste `W` (oder `Auto`), `N = MaxScenes`, `Aspect`, feste Maße (obere/untere Ränder 24, Abstand zwischen Karten 12, Icon-Reihe = Iconhöhe 30 + 4 + 24 wie im heutigen XAML).

1. Verfügbare Höhe: `A = H − Rand oben − Rand unten`.
2. Höhe einer Karte: `Zelle = A / N`; Thumbnail-Höhe `h = Zelle − Abstand − Icon-Reihe`.
3. Thumbnail-Breite aus dem Seitenverhältnis: `w = h · Aspect`.
4. Ist `W` fest und `w > W − 2·Innenabstand`: `w = W − 2·Innenabstand`, `h = w / Aspect`; die übrige Höhe bleibt frei (Karten werden mittig angeordnet wie heute).
5. Ist `W = Auto`: `W = w + 2·Innenabstand`, begrenzt auf 160 … min(480, 20 % der Monitorbreite).
6. **Untergrenze:** Thumbnail nicht kleiner als 96 × 60. Wird sie unterschritten, ist die tatsächliche Anzahl `N_eff = ⌊A / (60 + Abstand + Icon-Reihe)⌋`; es werden nur `N_eff` Karten gezeigt (Rest läuft in den Überlauf), das Einstellungsfenster zeigt dazu eine Warnung („Bei dieser Bildschirmhöhe passen nur X Gruppen“).
7. **Obergrenze:** maximal 320 × 200, damit bei `MaxScenes = 1` kein riesiges Thumbnail entsteht.
8. `ThumbnailSize` ≠ `Auto`: feste Maße verwenden, `N_eff` daraus ableiten wie in Punkt 6.

Ergebnis `{ ThumbWidth, ThumbHeight, CardSpacing, IconSize, EffectiveMaxScenes, SidebarWidth }` wird per Datenbindung an `MainWindow.xaml` gegeben; die festen `MinHeight="100"`/`MinWidth="150"` entfallen. Neu berechnen bei: Änderung der Einstellungen, `WM_DISPLAYCHANGE`, DPI-Wechsel, Änderung des Arbeitsbereichs (z. B. Taskleiste verschoben).

**`DwmThumbnail` korrigieren:** Das Ziel-Rechteck der DWM-Vorschau füllt heute das gesamte Steuerelement, das Bild wird also verzerrt. Künftig mit `DwmQueryThumbnailSourceSize` (bereits deklariert, bisher ungenutzt) das Seitenverhältnis des Quellfensters bestimmen und das Rechteck eingepasst und zentriert setzen (Letterbox). Bei mehreren gestapelten Fenstern pro Karte jedes Thumbnail verkleinert und leicht versetzt anordnen (z. B. 8 px nach oben/zur Mitte je Fenster), damit man die Stapelung sieht.

### 4.4 Option: Desktop-Symbole ein- und ausblenden

**Ist-Zustand:** `SceneManager` blendet die Symbole **immer** aus: im Konstruktor (`_desktop.HideIcons()`), bei jedem Wechsel in eine Szene und blendet sie in der Desktop-Ansicht (`SwitchTo(null)`) sowie beim Beenden (`Stop`) wieder ein. Das Umschalten erfolgt per `WM_COMMAND 0x7402`, der Zustand wird aus der Registry (`HideIcons`) gelesen.

**Neu – Modi** (`DesktopIcons.Mode`):
- `Manage` (Standard, bisheriges Verhalten): ausgeblendet, solange eine Szene aktiv ist; sichtbar in der Desktop-Ansicht.
- `AlwaysHidden`: immer ausgeblendet, solange StageManager läuft.
- `AlwaysVisible`: StageManager fasst die Symbole nie an.
- `RestoreOnExit` (Standard `true`): beim Beenden **den ursprünglichen Zustand** wiederherstellen. Heute wird stattdessen immer „einblenden“ erzwungen – wer die Symbole selbst ausgeblendet hatte, bekommt sie so gegen seinen Willen zurück.

**Umbau im Code:**
- Schnittstelle `IDesktopIcons` (`bool? IsVisible`, `Show()`, `Hide()`) vor `Desktop`, damit die Entscheidungslogik ohne Windows getestet werden kann.
- Neue Klasse `DesktopIconsPolicy` mit `Apply(Mode, bool sceneActive)`, die nur dann umschaltet, wenn sich der gewünschte vom tatsächlichen Zustand unterscheidet. `SceneManager` ruft nur noch `Apply` auf (Konstruktor, `SwitchTo`, `Stop`).
- **Ursprungszustand merken:** beim Start `GetDesktopIconsVisible()` einmal lesen und ablegen. Außerdem im Journal (Phase 1.3) festhalten (`desktopIconsOriginallyVisible`); beim Start nach einem Absturz wird er von dort wiederhergestellt, bevor irgendetwas anderes passiert.
- **Zustand selbst mitführen:** Nach `ToggleDesktopIcons()` wird die Registry nicht sofort aktualisiert; ein sofortiger zweiter Aufruf kann so doppelt umschalten. Daher den erwarteten Zustand intern speichern und die Registry nur zur Synchronisierung (Start, nach Explorer-Neustart) lesen. Bei `TaskbarCreated` (Explorer-Neustart) neu anwenden.
- **Änderung zur Laufzeit** (Einstellungsfenster oder Tray-Menü) wirkt sofort über `SettingsService.Changed`.
- Kontextmenü im Tray: Untermenü „Desktop-Symbole“ mit „Automatisch“, „Immer ausblenden“, „Nie ändern“.
- Bei mehreren Monitoren (Phase 7) sind die Symbole global: im Modus `Manage` ausblenden, sobald **mindestens eine** Anzeige eine aktive Bühne hat; einblenden, wenn alle in der Desktop-Ansicht sind.
- **Tests:** `DesktopIconsPolicy` mit Fake-`IDesktopIcons`: jeder Modus × (Szene aktiv / Desktop-Ansicht / Beenden mit und ohne `RestoreOnExit`) × Ursprungszustand sichtbar/unsichtbar; Moduswechsel zur Laufzeit; kein doppeltes Umschalten.

### 4.5 Einstellungsfenster und Tray-Menü
- Schlichtes WPF-Fenster mit Bereichen: **Allgemein** (Autostart, Verstecken-Strategie), **Seitenleiste** (Seite, Breite, Anzahl, Fenster pro Gruppe, Überlauf, Thumbnail-Größe, Modus), **Desktop** (Symbole), **Bühne** (Begleit-Apps), **Ausnahmen**.
- Alle Änderungen gelten **sofort** (Live-Vorschau); eine Schaltfläche „Zurücksetzen“ stellt die Standardwerte wieder her.
- Unter dem Regler für die Anzahl steht die berechnete Thumbnail-Größe („Vorschau: 168 × 105 px, Leiste 196 px breit“) und gegebenenfalls die Warnung aus 4.3.
- Aufruf per Linksklick aufs Tray-Icon und über das Kontextmenü; das Menü bekommt Schnellzugriffe für Seite und Desktop-Symbole (siehe 4.2 und 4.4).
- Eingaben werden über `Normalize()` begrenzt; ungültige Werte werden korrigiert statt abgewiesen.

### 4.6 Fester Modus als AppBar (optional)
- Im Modus `Fixed` die Seitenleiste per `SHAppBarMessage` registrieren: `ABM_NEW`, `ABM_QUERYPOS`, `ABM_SETPOS`, beim Beenden `ABM_REMOVE`. Die Kante folgt der Einstellung `Sidebar.Side`: `ABE_LEFT` oder `ABE_RIGHT`; bei einem Seitenwechsel zuerst `ABM_REMOVE`, dann neu registrieren.
- Die Breite aus 4.3 wird als reservierter Streifen verwendet; ändert sich die Breite (neue Anzahl, anderes Thumbnail-Maß), mit `ABM_SETPOS` neu anfordern.
- Callback-Nachricht in einem `HwndSource`-Hook auswerten: `ABN_POSCHANGED` → neu positionieren, `ABN_FULLSCREENAPP` → `Topmost` aus/ein.
- `RegisterWindowMessage("TaskbarCreated")` behandeln (Explorer-Neustart) → AppBar neu registrieren.
- Effekt: maximierte Fenster überdecken die Leiste nicht mehr; `OverlapCheck` ist in diesem Modus überflüssig.
- Der bisherige Modus (Einblenden am Rand, Auto-Hide) bleibt als `Floating`.

**Fertig, wenn:**
- alle bisher fest einprogrammierten Schalter in `settings.json` stehen und zur Laufzeit wirken;
- die Leiste links wie rechts vollständig funktioniert (Einblenden am Rand, Auto-Hide, Herausziehen, Animation, Überlappungsprüfung), auch auf einem Monitor mit negativem Ursprung;
- bei Änderung der Anzahl von 3 auf 8 die Thumbnails ohne Neustart schrumpfen, das Seitenverhältnis stimmt und nie mehr Karten gezeigt werden, als in die Höhe passen;
- die Desktop-Symbole in allen drei Modi wie beschrieben reagieren und nach Beenden oder Absturz im Ausgangszustand sind.

---

## Phase 5 – Standbilder per PrintWindow

### 5.1 Aufnahme
Neue Klasse `Capture/WindowCapture.cs`:
1. Fenstergröße aus `DWMWA_EXTENDED_FRAME_BOUNDS` (ohne unsichtbaren Rahmen).
2. `CreateDIBSection` (32 bpp, top-down) in passender Größe.
3. `PrintWindow(h, hdc, PW_RENDERFULLCONTENT = 2)`.
4. Auf den sichtbaren Rahmen zuschneiden, mit `StretchBlt` + `SetStretchBltMode(HALFTONE)` auf Zielhöhe (z. B. 2× Kartenhöhe in Gerätepixeln) verkleinern.
5. Komplett schwarzes Bild (Stichprobe der Pixel) = Fehler → Rückfall auf App-Icon.
6. Ergebnis als eingefrorene `BitmapSource` (`Freeze()`), damit sie threadübergreifend nutzbar ist.

### 5.2 Ausführung und Cache
- Immer in einem Hintergrund-Task mit Timeout (z. B. 300 ms), weil `PrintWindow` auf die Ziel-App wartet; hängende Apps (`IsHungAppWindow`) gar nicht erst aufnehmen.
- Aufnahme-Zeitpunkte: kurz bevor ein Fenster geparkt wird (Phase 1) – dann ist der Inhalt aktuell –, sowie periodisch für geparkte Fenster mit niedriger Frequenz.
- Cache pro Fenster, Speicherbudget begrenzen (LRU), alte Bitmaps freigeben.

### 5.3 Verwendung in der Seitenleiste
- `DwmThumbnail` bleibt für Live-Vorschau; Standbild als Hintergrund bzw. Platzhalter, solange das Thumbnail noch nicht registriert ist oder fehlschlägt.
- Optional per Einstellung zwischen Live und Standbild wählen (Standbild spart GPU bei vielen Szenen und erlaubt WPF-Effekte wie Neigung, Abrundung, Transparenz – die bei DWM-Thumbnails nicht möglich sind).

**Fertig, wenn:** jedes geparkte Fenster ein aktuelles Standbild hat und keine Aufnahme den UI-Thread blockiert.

---

## Phase 6 – Übergangsanimationen

### 6.1 Overlay-Fenster
- Pro Monitor ein WPF-Fenster: randlos, `AllowsTransparency=True`, `Topmost`, `ShowInTaskbar=False`, `ShowActivated=False`, über die Arbeitsfläche gespannt.
- Erweiterte Stile: `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` → durchklickbar und nicht in Alt+Tab.
- `DWMWA_TRANSITIONS_FORCEDISABLED` dauerhaft gesetzt.
- Nur während einer Animation sichtbar.

### 6.2 Ablauf eines Szenenwechsels
1. Standbilder aller beteiligten Fenster sicherstellen (Phase 5; das ausgehende Fenster ist noch sichtbar und wird direkt aufgenommen).
2. Für jedes Fenster ein `Image` auf dem Overlay platzieren:
   - **hereinkommend:** von der Kartenposition in der Seitenleiste → Originalposition des Fensters.
   - **hinausgehend:** von der aktuellen Fensterposition → neue Kartenposition.
3. Echte Fenster sofort umschalten: hinausgehende parken, hereinkommende bleiben geparkt.
4. Animation von Position und Größe über `RenderTransform` (`TranslateTransform` + `ScaleTransform`) mit `CompositionTarget.Rendering` oder `DoubleAnimation`, Dauer ca. 450–550 ms, geteilt durch `AnimationSpeed`.
5. Easing: kritisch gedämpfte Feder (`1 - (1 + ωt)·e^(-ωt)`, normiert auf 1 am Ende) als eigene `EasingFunctionBase`.
6. Kurz vor Ende die hereinkommenden echten Fenster an ihre Position holen (mit abgeschalteten DWM-Übergängen), dann das Overlay ausblenden (Ein-Frame-Übergabe).
7. Sicherheits-Timeout (1,5 s): Overlay in jedem Fall schließen und Fenster zurückholen.

### 6.3 Details
- Schatten: einfacher `DropShadowEffect` passend zu Windows 11 (weich, ca. 20–30 px Unschärfe). Die exakte Nachbildung des DWM-Schattens wird zunächst nicht angestrebt.
- Abgerundete Ecken über `Clip` mit `RectangleGeometry` (Radius 8 px).
- Unterbrechbarkeit: Kommt während einer Animation ein neuer Wechsel, Animation an der aktuellen Position übernehmen und zum neuen Ziel weiterführen (aktuelle Werte statt Startwerte verwenden).
- Gerätepixel ↔ DIP sauber umrechnen (`VisualTreeHelper.GetDpi`), Overlay ist pro Monitor DPI-bewusst (siehe Manifest in Phase 7).
- Ruckelfreiheit: Animationsschritte an `CompositionTarget.Rendering` koppeln; bei Bedarf `DwmFlush` vor dem Fenster-Umschalten.
- Abschaltbar über `EnableAnimations`; bei `SystemParameters.ClientAreaAnimation == false` (Windows-Animationen aus) standardmäßig aus.

### 6.4 Kleinere UI-Animationen
- Hover auf Karten: Opazität/Skalierung weich übergehen (`Storyboard` statt harter Trigger).
- Karten beim Einfügen/Entfernen in der Seitenleiste verschieben statt springen lassen.

**Fertig, wenn:** Szenenwechsel flüssig animiert ist, kein Flackern beim Übergang vom Bild zum echten Fenster sichtbar ist und schnelle Mehrfachklicks nichts durcheinanderbringen.

**Risiken:** Fenster, deren Standbild schwarz ist (DRM, manche Spiele, GPU-Apps) → Rückfall: Icon-Karte animieren oder Animation für dieses Fenster auslassen.

---

## Phase 7 – Mehrere Monitore

### 7.1 Grundlagen
- App-Manifest (`app.manifest`) mit `<dpiAwareness>PerMonitorV2</dpiAwareness>` hinzufügen.
- Monitore per `EnumDisplayMonitors` + `GetMonitorInfoW` (mit `MONITORINFOEX`, Gerätename `\\.\DISPLAYn` als Schlüssel), DPI per `GetDpiForMonitor`.
- Auf Monitor-Änderungen reagieren: `WM_DISPLAYCHANGE`, `SystemEvents.DisplaySettingsChanged`.
- Klare Regel: Win32 arbeitet in physischen Pixeln, WPF in DIPs – Umrechnung an genau einer Stelle (`Monitor.Scale`).

### 7.2 Pro Monitor
- Ein `StageModel` (Phase 3) pro Monitor; ein Fenster gehört zu dem Monitor, auf dem es liegt (`MonitorFromWindow`).
- Eine Seitenleiste pro Monitor (Instanzen von `MainWindow` bzw. einer daraus extrahierten `SidebarWindow`-Klasse), ein Overlay pro Monitor (Phase 6).
- Einstellung `Displays`: pro Monitor, ob eine Seitenleiste angezeigt wird, und auf welcher Seite (links/rechts). Die globalen Werte aus Phase 4 (`Sidebar.Side`, `MaxScenes`, `Width`, `ThumbnailSize`) sind die Vorgabe; pro Monitor lassen sie sich überschreiben (`Displays["\\.\DISPLAY2"].Side = Right`). `SidebarGeometry` und `SidebarLayout` (4.2/4.3) werden dazu je Monitor mit dessen Arbeitsbereich und DPI aufgerufen – so ändert sich an der Berechnung nichts, nur die Eingaben.
- Fenster per Drag auf einen anderen Monitor (`EVENT_SYSTEM_MOVESIZEEND`) → wechselt in dessen Modell und auf dessen Bühne.
- Parken (Phase 1) hält Fenster bereits auf ihrem Monitor – passt ohne Änderung.

### 7.3 Abhängige Anpassungen
- `MainWindow`-Positionierung (`Left=0`, `Top=0`) durch Monitor-Arbeitsbereich ersetzen.
- Mauskante zum Einblenden (`X ≤ 44`) pro Monitor und Seite berechnen (`SidebarGeometry`, Phase 4.2).
- Die Anzahl der Karten und die Thumbnail-Größe hängen von der Höhe des jeweiligen Arbeitsbereichs ab: ein Hochformat-Monitor bekommt automatisch größere oder weniger Karten als ein Querformat-Monitor.
- Desktop-Symbole sind global (siehe 4.4): Regel für `Manage` über alle Anzeigen hinweg anwenden.
- Desktop-Ansicht: Klick auf das Hintergrundbild leert nur die Bühne des betroffenen Monitors.

**Fertig, wenn:** zwei Monitore mit unterschiedlicher Skalierung (z. B. 100 % und 150 %) unabhängige Bühnen haben und Fenster zwischen ihnen korrekt wandern.

---

## Querschnitt: manuelle Testliste

Nach jeder Phase prüfen (vorerst manuell, als `docs/Testliste.md` pflegen):
- Apps: Explorer, Chrome/Edge, Visual Studio, VS Code, Teams, Outlook, Einstellungen (UWP), eine App mit Admin-Rechten, eine hängende App (z. B. Testprogramm mit `Thread.Sleep` im UI-Thread).
- Abläufe: Wechsel per Seitenleiste, per Alt+Tab, per Taskleiste; neues Fenster öffnen; Fenster schließen; Fenster in andere Szene ziehen; Desktop-Klick; Beenden über Tray; `taskkill /f` + Neustart; Abmelden/Herunterfahren.
- Umgebungen: 100 % und 150 % Skalierung, ein und zwei Monitore, Explorer-Neustart.
- Leiste: links und rechts; Anzahl 1, 3, 6, 12; Bildschirmhöhe 768 px und 2160 px; Taskleiste unten, oben, links, rechts; Seitenwechsel während der Laufzeit; Fenster aus der Leiste herausziehen auf beiden Seiten.
- Desktop-Symbole: je Modus Start, Szenenwechsel, Desktop-Ansicht, Beenden, `taskkill /f` + Neustart, Explorer-Neustart; Ausgangszustand „Symbole bereits ausgeblendet“.

## Empfohlene Reihenfolge der Pull Requests

Phase 4 (Einstellungen) wird vorgezogen, weil sie keine anderen Phasen braucht und die drei gewünschten Optionen enthält.

1. CI-Fix + Logging + Datenordner (Phase 0)
2. Einstellungs-Infrastruktur + `SidebarGeometry` + Option „Seite“ + Korrektur der Pixel/DIP-Fehler (Phase 4.1, 4.2)
3. `SidebarLayout` + Option „Anzahl / Thumbnail-Größe“ + Seitenverhältnis im `DwmThumbnail` (Phase 4.3)
4. `IDesktopIcons` + `DesktopIconsPolicy` + Option „Desktop-Symbole“ (Phase 4.4)
5. Einstellungsfenster + Tray-Schnellzugriffe (Phase 4.5)
6. Journal + `ParkingSpot` + `ScreenOffsetWindowStrategy` + DWM-Übergänge (Phase 1); das Journal speichert auch den Ursprungszustand der Desktop-Symbole
7. Filter + `AppKey` + UWP + Icons + Hooks (Phase 2)
8. `StageManager.Core` + Tests + macOS-Wechsellogik (Phase 3); `SidebarGeometry`, `SidebarLayout` und `DesktopIconsPolicy` ziehen dorthin um, falls sie vorher im Hauptprojekt lagen
9. AppBar-Modus (Phase 4.6)
10. PrintWindow-Standbilder (Phase 5)
11. Overlay-Animationen (Phase 6)
12. Mehrere Monitore (Phase 7)

## Annahmen und offene Punkte

- **„Fenster(paare)“** habe ich als *Fenstergruppen* verstanden, also Karten in der Leiste, die mehrere Fenster einer App enthalten können. Daher gibt es zwei Zahlen: `MaxScenes` (Karten) und `MaxWindowsPerScene` (gestapelte Fenster pro Karte). Falls mit „Paaren“ etwas anderes gemeint ist, ist 4.3 anzupassen.
- **„Verwaltet“** verstehe ich so, dass die Anzahl auch bestimmt, welche Gruppen StageManager überhaupt anfasst. Das regelt `OverflowBehavior`; der Standard `HideInSidebar` ändert gegenüber heute nichts.
- Die Thumbnail-Größe richtet sich nach der Anzahl der Karten und der Bildschirmhöhe. Wer lieber feste Maße will, stellt `ThumbnailSize` auf feste Werte; die Anzahl der gezeigten Karten wird dann begrenzt.
- Mindest- und Höchstgrößen (96 × 60 bzw. 320 × 200), Rand und Abstände sind Startwerte und sollten nach dem ersten Test mit echten Fenstern angepasst werden.
