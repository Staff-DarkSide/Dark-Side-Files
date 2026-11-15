# DARKSIDE Launcher (WPF, .NET 8)

## Compilar (Windows, .NET 8 SDK)
    dotnet run
    dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

## Como funciona
1. Primera vez: el boton dice INSTALAR. Eliges una carpeta, descarga Dark.Side.UO.rar
   desde GitHub (v1.0), lo descomprime y deja el boton en JUGAR. Necesita ~4 GB libres.
2. Despues: al abrir y al pulsar JUGAR comprueba actualizaciones y lanza client.exe.
3. "Cliente..." sirve para apuntar a una instalacion que el jugador ya tenga.

Config del jugador: %AppData%\DarkSide\launcher.json

## Actualizaciones pequenas
Sube la carpeta `updates` + `manifest.json` al repo (ver manifest.example.json).
URL por defecto: https://raw.githubusercontent.com/Staff-DarkSide/Dark-Side-Files/main/updates/manifest.json
Las rutas del manifest son relativas a la carpeta de client.exe ("Ultima Online/").
