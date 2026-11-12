# WifiHeat

Plugin de AutoCAD que estima la cobertura Wi-Fi de un plano 2D. Calcula el RSSI de cada punto a partir de la posición del punto de acceso, la pérdida en el espacio libre y la atenuación de los muros que cruza el trayecto directo.

## Requisitos

- AutoCAD 2027 para Windows
- SDK .NET 10
- Visual Studio 2026

## Compilar y cargar

```powershell
dotnet build WifiHeat.slnx
dotnet test WifiHeat.slnx
```

En AutoCAD, ejecutar `NETLOAD` y elegir `src\WifiHeat.AutoCAD\bin\Debug\net10.0-windows\WifiHeat.AutoCAD.dll`. Con `SECURELOAD` activo, agregar esa carpeta a las ubicaciones de confianza (`TRUSTEDPATHS`).

## Comandos

| Comando | Descripción |
| --- | --- |
| `WIFI` | Muestra la versión y los comandos disponibles. |
| `WIFISETUP` | Crea las capas `WIFIHEAT_MUROS`, `WIFIHEAT_AP`, `WIFIHEAT_OVERLAY` y `WIFIHEAT_LEYENDA`, y guarda en el dibujo la unidad de trabajo (metros, centímetros o milímetros). |
| `WIFIWALL` | Asigna material y espesor a las líneas o polilíneas seleccionadas y las pasa a `WIFIHEAT_MUROS`. |
| `WIFIVALIDATE` | Lee los muros y lista errores y avisos con el handle de cada entidad (por ejemplo, `FALTA PROPIEDAD`). |
| `WIFIADDAP` | Ubica el punto de acceso y pide banda (2,4 o 5 GHz) y potencia de transmisión. |
| `WIFIAREA` | Opcional. Define el área de cobertura con una polilínea cerrada de tramos rectos; la opción `Rectangulo` vuelve al valor por defecto, el rectángulo que encierra los muros. Fuera del área no se calcula ni se pinta. |
| `WIFICALCULATE` | Calcula la grilla de 0,50 m, genera el mapa de calor como imagen PNG con degradé continuo (`<dibujo>_wifiheat.png`, junto al DWG; en la carpeta temporal si el dibujo no está guardado), la inserta en `WIFIHEAT_OVERLAY` debajo del resto del dibujo y agrega la leyenda. |
| `WIFIQUERY` | Explica el RSSI estimado en un punto: distancia, muros cruzados, pérdidas y potencia recibida. |
| `WIFIEXPORT` | Exporta a PNG la misma imagen del mapa de calor con los muros, el punto de acceso y la leyenda. |
| `WIFIINFO` | Lista unidades, extensión, capas y entidades del espacio modelo. |

Orden de uso: `WIFISETUP` → `WIFIWALL` → `WIFIVALIDATE` → `WIFIADDAP` → `WIFIAREA` (opcional) → `WIFICALCULATE` → `WIFIQUERY` → `WIFIEXPORT`.

Cada muro se dibuja como un eje (una línea o polilínea de tramos rectos por muro). El trayecto directo suma una vez la pérdida de cada eje que cruza.

## Probar con el ejemplo

`samples/demo.dwg` es una planta sintética de 30 × 14 m con seis ambientes: muros exteriores de hormigón (0,25 m); muros interiores de ladrillo macizo (0,20 m) en x = 10 m y en y = 7 m desde x = 20 m, de ladrillo hueco (0,15 m) en x = 20 m y de placa de yeso (0,10 m) en y = 7 m entre x = 0 y x = 20 m. El punto de acceso está en (2; 3) m, en 5 GHz con 20 dBm. Todo está ya asignado.

1. Abrir `samples/demo.dwg` en AutoCAD 2027.
2. Ejecutar `NETLOAD` y elegir `src\WifiHeat.AutoCAD\bin\Debug\net10.0-windows\WifiHeat.AutoCAD.dll` (compilar antes con `dotnet build WifiHeat.slnx`).
3. Ejecutar `SCRIPT` y elegir `samples/demo.scr`. Valida los muros, calcula el mapa, consulta dos puntos —(8; 5) m, en el mismo ambiente que el AP, y (26; 11) m, detrás de tres muros— y exporta `samples/demo-wifiheat.png`. La imagen del mapa que se inserta en el dibujo queda en `samples/demo_wifiheat.png`.

Notas:

- Con `SECURELOAD` activo, `NETLOAD` exige que la carpeta del plugin esté en `TRUSTEDPATHS`: agregar `src\WifiHeat.AutoCAD\bin\Debug\net10.0-windows`. Si AutoCAD bloquea también el script, agregar la carpeta `samples`.
- `demo.scr` ejecuta además `NETLOAD` con la ruta relativa a `demo.dwg`, así que el paso 2 solo es imprescindible si ese `NETLOAD` es bloqueado.
- No desactivar `SECURELOAD`.

## Estructura

- `src/WifiHeat.Core`: modelo de propagación, geometría, materiales y grilla; no depende de AutoCAD.
- `src/WifiHeat.AutoCAD`: comandos, lectura del dibujo y salida en AutoCAD.
- `tests/WifiHeat.Core.Tests`: pruebas de Core con xUnit.

Modelo y ecuaciones: [docs/modelo.md](docs/modelo.md). Materiales y fuentes: [config/materials.json](config/materials.json).

## Herramientas utilizadas

- AutoCAD 2027 y su API .NET administrada (paquetes NuGet `AutoCAD.NET.Core` y `AutoCAD.NET.Model` 26.0.0)
- AutoCAD Core Console (`accoreconsole.exe`) para pruebas sin interfaz
- .NET 10 y C#
- Visual Studio 2026
- xUnit.net v3 con Microsoft Testing Platform
- Git, GitHub y GitHub Actions
- Claude Code
- Codex

## Licencia

Pendiente.

## Créditos

Ferrari Osado Bautista, Torres Gauffin Tadeo y Valdecantos Joaquín.
Trabajo Final Integrador de Comunicación de Datos, UTN-FRT.
