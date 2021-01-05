net stop "CocoWatcher" 
"%cd%\InstallUtil.exe" "%cd%\CocoWatcher.exe"  -u
taskkill /f /im CocoWatcher.exe
pause