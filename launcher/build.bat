@echo off
echo Compilando DARKSIDE Launcher...
where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo No tienes .NET 8 SDK. Descargalo de: https://dotnet.microsoft.com/download/dotnet/8.0
  echo ^(elige "SDK" para Windows x64^), instalalo y vuelve a ejecutar este archivo.
  pause
  exit /b 1
)
dotnet publish DarkSideLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 (
  echo.
  echo ERROR al compilar. Copia el mensaje de arriba y pasaselo a Claude.
  pause
  exit /b 1
)
echo.
echo LISTO: publish\DarkSideLauncher.exe
explorer publish
pause
