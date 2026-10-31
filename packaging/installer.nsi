Unicode true
SetCompressor /SOLID lzma

Name "VELOCE"
Icon "..\src\Veloce.App\Assets\app.ico"
UninstallIcon "..\src\Veloce.App\Assets\app.ico"
OutFile "..\bin\Veloce-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\Veloce"
RequestExecutionLevel user

Page directory
Page instfiles

UninstPage uninstConfirm
UninstPage instfiles

Section "MainSection" SEC01
    SetOutPath "$INSTDIR"
    File /r "..\bin\VELOCE\*.*"
    
    CreateDirectory "$SMPROGRAMS\Veloce"
    CreateShortcut "$SMPROGRAMS\Veloce\VELOCE.lnk" "$INSTDIR\VELOCE.exe"
    CreateShortcut "$DESKTOP\VELOCE.lnk" "$INSTDIR\VELOCE.exe"
    
    WriteUninstaller "$INSTDIR\uninstall.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Veloce" "DisplayName" "VELOCE"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Veloce" "UninstallString" "$\"$INSTDIR\uninstall.exe$\""
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Veloce" "DisplayIcon" "$\"$INSTDIR\VELOCE.exe$\""
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Veloce" "Publisher" "Gabriel Soares Neco"
SectionEnd

Section "Uninstall"
    Delete "$DESKTOP\VELOCE.lnk"
    Delete "$SMPROGRAMS\Veloce\VELOCE.lnk"
    RMDir "$SMPROGRAMS\Veloce"
    RMDir /r "$INSTDIR"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Veloce"
SectionEnd
