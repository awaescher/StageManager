# Stage Manager for Windows

This is an experimental approach to bring the macOS [Stage Manager](https://9to5mac.com/2022/07/26/stage-manager-on-mac/) to Microsoft Windows.

![Stage Manager](media/StageManager.jpg)

This prototype groups applications by their process. By switching between so called "scenes" on the left, Stage Manager hides other windows and the desktop icons, helping you to focus.

Windows can be moved from one scene to another by dragging them onto scenes on the left.

## To do

This is an experimental fun project. I don't have any idea whether or not this is going to be a final product one day. 

- Experimental phase
  - auto hide scenes for maximized content
  - place screenshots in relative size of the desktop
  - place app icons over 3D window adorners
  - rounded corners for the window thumbnails
  - limit maximum scenes (like 6 for macOS?)
  - limit window count per scene (like newest 5)
  - adjust the 3d angle according to screen position
- Product phase
  - virtual desktop support (pin window)
  - multimonitor support
  - feature parity with macOS Stage Manager
- Polishing phase
  - window animations
  - live dwm thumbnails

Contributions very welcome :heart:

---

Stage Manager is using a few code files to handle window tracking from [workspacer](https://github.com/workspacer/workspacer), an amazing open source project by [Rick Button](https://github.com/rickbutton).
