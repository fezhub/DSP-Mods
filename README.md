# DSP-Mods
Dyson Sphere Program Mods by thisisbrad

**Do not report problems to the developers** when running any of these mods (or even a savegame you've used one of these mods in). The game currently does not support mods. The devs do not need spurious bug reports caused by a modded game. Reproduce your bug on a 100% vanilla game and save, or assume it's potentially related to a mod unless you know with 100% certainty it wasn't influenced in any way by any mod you have installed now or in the past. 

### Copy Inserters
When copying a building, the attached inserters are also copied to the new location
![Copy Inserters Demo](copyinserters.gif)

### Installation Instructions
Download and install BepInEx into steamapps/common/Dyson Sphere Program: https://bepinex.github.io/bepinex_docs/master/articles/user_guide/installation/index.html?tabs=tabid-win

Download the latest version: https://github.com/fezhub/DSP-Mods/releases

Add dll file to steamapps/common/Dyson Sphere Program/BepInEx/plugins

Launch the game and the mod should be loaded

### Changelog
#### v1.2.0
- Fixed visual bugs and placement issues (massive thanks DavisCook777, colin-daniels)
- Fixed incorrect speed of copied inserters (thanks brokenmass)
- Added build previews (thanks brokenmass)
- Added ability to disable copying inserters via TAB key (thanks brokenmass)
#### v1.1.0
##### Fixes
- IndexOutOfRangeException when copying a building plan
- IndexOutOfRangeException when copying chained buildings
- Doesn't work if 'Automatically move building target' has been disabled
- Incompatible with AdvancedBuildDestruct mod (thanks DavisCook777)
#### v1.0.0
- Initial release



#### Acknowledgements

All trademarks, copyright, and resources related to Dyson Sphere Project itself, remain the property of Gamera Game and Youthcat Studio as applicable according to the license agreement distributed with Dyson Sphere Program.
