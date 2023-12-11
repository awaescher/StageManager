# Stage Manager for Windows

This is an experimental approach to bring the macOS [Stage Manager](https://support.apple.com/en-us/HT213315) to Microsoft Windows.

![Stage Manager](media/StageManager%20Basics.gif)

This prototype groups applications by their process. By switching between so called "scenes" on the left, Stage Manager hides other windows and the desktop icons, helping you to focus.

Windows can be moved from one scene to another by dragging them onto scenes on the left.

## Usage

Download and run the executable from the [Releases tab](https://github.com/awaescher/StageManager/releases/) or 
 - clone this repository
 - cd into the repository directory
 - run `dotnet run --project StageManager`

To quit, find the app's tray icon (Windows might move it into the overflow menu) and use its context menu to close the app.
 
### Requirements
 - Windows 10 version 2004 or newer
 - [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download)

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
|limit window count per scene (like newest 5)|⬜|
|tray icon to start & stop|✅|
|start with Windows|✅|
|**Product stage**||
|virtual desktop support (pin window)|⬜|
|multi-monitor support|⬜|
|visual feedback when dragging windows from other scenes|⬜|
|feature parity with macOS Stage Manager|⬜|
|**Polishing stage**||
|window animations|⬜|
|live dwm thumbnails|✅|
|adjust 3D angle according to screen position|⬜|
|flyover sidebar in desktop view mode if icons are close to the left|⬜|

Contributions very welcome :heart:

---

Stage Manager is using a few code files to handle window tracking from [workspacer](https://github.com/workspacer/workspacer), an amazing open source project by [Rick Button](https://github.com/rickbutton).
