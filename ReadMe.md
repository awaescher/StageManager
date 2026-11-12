# Stage Manager for Windows

This is an experimental approach to bring the macOS [Stage Manager](https://support.apple.com/en-us/HT213315) to Microsoft Windows.

> **Important:** This is a prototype and a feasibility study - I am not actively developing this project at the moment but I'd be happy to review and merge pull requests. 

![Stage Manager](media/StageManager%20Basics.gif)

This prototype groups applications by their executable. By switching between so called "scenes" in the sidebar, Stage Manager hides other windows and the desktop icons, helping you to focus. Scene changes are animated: the windows fly between the sidebar and the stage.

Drag a card from the sidebar onto the stage to add its windows to the current scene, or onto another card to merge both. Shift+click does the same as dragging onto the stage. The sidebar can sit on the left or the right side of the screen.

Scenes can be switched with the keyboard: hold `Ctrl+Alt+Win` and press `Tab` for the next scene (the sidebar shows the pick), `Shift+Tab` for the previous one, or `1` to `9` for the card at that position. The scene opens when you let go of the keys, `Esc` cancels. The held keys can be changed in the settings by clicking into the field and pressing the new keys. Combinations that are already used by Windows or another app are named there.

Apps that StageManager should leave alone, such as launchers or overlays, can be excluded: either on the "Apps" page of the settings (choose from the running apps or an app file, and take them off the list with the trash can) or with a right click on a card. If the card holds several apps, the menu names each of them. The windows of an excluded app get no card and are never hidden, and they are back right away when the app is included again.

The menus and settings are available in English, German, Spanish, French, Italian, Portuguese, Russian, Japanese, Korean, Simplified Chinese and Turkish. StageManager follows the display language of Windows, or you choose one in the settings. New languages are a copy of `StageManager.Core/Resources/Strings.resx`: translate the values, name the file `Strings.<language>.resx` and add the language to `Languages.cs`. A test checks that every language has all texts.

## Usage

Download and run the executable from the [Releases tab](https://github.com/awaescher/StageManager/releases/) or 
 - clone this repository
 - cd into the repository directory
 - run `dotnet run --project StageManager`

To quit, find the app's tray icon (Windows might move it into the overflow menu) and use its context menu to close the app. The tray menu also has the settings and can bring back windows that ended up out of the screen.
 
### Requirements
 - Windows 10 version 2004 or newer
 - [.NET 11 SDK](https://dotnet.microsoft.com/en-us/download)

## To do

This is an experimental fun project. I don't have any idea whether or not this is going to be a final product one day. 

|Topic|State|
|-|-|
|**Experimental stage**||
|initial windows grouping by process|✅|
|3D display of opened windows (static)|✅|
|hide/show windows of given scenes|✅|
|hide/show desktop icons|✅|
|scene management with drag&drop|✅|
|restore windows on quit/restart|✅|
|auto hide & fly-in scenes for maximized windows|✅|
|full screenshots for windows that were minimized on startup|✅|
|drag windows from other scenes into the current one|✅|
|place screenshots in relative size of the desktop|⬜|
|limit maximum scenes (like 6 for macOS?)|✅|
|limit window count per scene (like newest 5)|✅|
|tray icon to start & stop|✅|
|start with Windows|✅|
|**Product stage**||
|several apps per scene (merge, split, companion apps)|✅|
|keep the stacking order of the windows of a scene|✅|
|sidebar on the left or the right|✅|
|settings window|✅|
|switch scenes with the keyboard (configurable keys)|✅|
|translated user interface (11 languages, switchable in the settings)|✅|
|exclude apps (settings page "Apps" or right click on a card)|✅|
|bring back windows that ended up out of the screen|✅|
|virtual desktop support (pin window)|⬜|
|multi-monitor support|⬜|
|visual feedback when dragging windows from other scenes|✅|
|feature parity with macOS Stage Manager|⬜|
|**Polishing stage**||
|window animations|✅|
|live dwm thumbnails|✅|
|adjust 3D angle according to screen position|⬜|
|sidebar steps aside in the desktop view and flies back in at the screen edge|✅|

Contributions very welcome :heart:

---

Stage Manager is using a few code files to handle window tracking from [workspacer](https://github.com/workspacer/workspacer), an amazing open source project by [Rick Button](https://github.com/rickbutton).
