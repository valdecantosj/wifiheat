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

Para usar además el panel, cargar con `NETLOAD` las dos DLL de `src\WifiHeat.AutoCAD.UI\bin\Debug\net10.0-windows` (esa carpeta también debe ser de confianza): primero `WifiHeat.AutoCAD.dll` y después `WifiHeat.AutoCAD.UI.dll`. El panel es opcional: sin `WifiHeat.AutoCAD.UI.dll` todos los comandos funcionan igual.

## Comandos

| Comando | Descripción |
| --- | --- |
| `WIFI` | Muestra la versión y los comandos disponibles. |
| `WIFISETUP` | Crea las capas `WIFIHEAT_MUROS`, `WIFIHEAT_AP`, `WIFIHEAT_OVERLAY` y `WIFIHEAT_LEYENDA`, y guarda en el dibujo la unidad de trabajo (metros, centímetros o milímetros). |
| `WIFIEJES` | Opcional, para planos con muros dibujados con sus dos caras. Pide una ventana (una sola planta), las capas de las caras (sugiere `muros`; las capas `0` y `ventanas` solo se procesan si se nombran) y el umbral de unión de tramos (0,20 m por defecto). Propone un eje a mitad de distancia entre cada par de segmentos paralelos separados hasta 0,50 m, une los tramos alineados cuya separación no supera el umbral y cierra esquinas y uniones en T. Crea los ejes en `WIFIHEAT_MUROS` con el espesor estimado y sin material, sin modificar las entidades originales, e informa los segmentos que quedaron sin par. Al repetirlo sobre la misma ventana no duplica: conserva los ejes que coinciden (con su material) y reemplaza el resto de los generados antes. |
| `WIFIWALL` | Asigna material y espesor a las líneas o polilíneas seleccionadas y las pasa a `WIFIHEAT_MUROS`. Enter en el espesor conserva el de cada muro (0,15 m si no tiene). |
| `WIFIVALIDATE` | Lee los muros y lista errores y avisos con el handle de cada entidad (por ejemplo, `FALTA PROPIEDAD`). |
| `WIFIADDAP` | Ubica el punto de acceso y pide banda (2,4 o 5 GHz) y potencia de transmisión. |
| `WIFIAREA` | Opcional. Define el área de cobertura con una polilínea cerrada de tramos rectos; la opción `Rectángulo` vuelve al valor por defecto, el rectángulo que encierra los muros. Fuera del área no se calcula ni se pinta. |
| `WIFICALCULATE` | Calcula la grilla y dibuja el mapa de calor y la leyenda. Opciones: `Resolución` (tamaño de celda, 0,50 m por defecto, entre 0,10 y 5 m) y `Atenuación` (0 a 90 %, 40 % por defecto, que se aplica también al mapa ya dibujado); los valores se mantienen mientras el dibujo esté abierto. El mapa es una imagen PNG con degradé continuo, opaca dentro del área y transparente fuera (`<dibujo>_wifiheat_NNN.png`, un archivo nuevo por cálculo junto al DWG, o en la carpeta temporal si el dibujo no está guardado; se borran los anteriores). Se inserta en `WIFIHEAT_OVERLAY` debajo del resto del dibujo y se atenúa con la propiedad Atenuación (Fade) de la imagen. |
| `WIFIQUERY` | Explica el RSSI estimado en un punto: distancia, muros cruzados, pérdidas y potencia recibida. |
| `WIFIEXPORT` | Exporta a PNG la misma imagen del mapa de calor, con la misma atenuación, junto con los muros, el punto de acceso y la leyenda. |
| `WIFIPANEL` | Abre el panel acoplable con todo el recorrido (ver [Panel](#panel)). Solo está disponible si se cargó `WifiHeat.AutoCAD.UI.dll`. |
| `WIFIINFO` | Lista unidades, extensión, capas y entidades del espacio modelo. |

Orden de uso: `WIFISETUP` → `WIFIEJES` (opcional) → `WIFIWALL` → `WIFIVALIDATE` → `WIFIADDAP` → `WIFIAREA` (opcional) → `WIFICALCULATE` → `WIFIQUERY` → `WIFIEXPORT`.

Cada muro se dibuja como un eje (una línea o polilínea de tramos rectos por muro). El trayecto directo suma una vez la pérdida de cada eje que cruza; en uniones y cruces se cuenta una sola vez la pérdida mayor (ver [docs/modelo.md](docs/modelo.md)).

## Panel

`WIFIPANEL` abre una paleta acoplable que reúne el mismo recorrido que los comandos. El panel no calcula: muestra y edita los datos del dibujo activo y usa las mismas operaciones que los comandos, así que se pueden combinar. Sigue el tema claro u oscuro de AutoCAD y se actualiza al cambiar de dibujo y al terminar cada comando.

| Sección | Qué permite |
| --- | --- |
| 1 · Preparar plano | Elegir la unidad del dibujo. Para muros de doble cara: elegir una ventana, ver las capas que contiene con su cantidad de segmentos y de ejes, marcar las que son caras de muro, ajustar el umbral de unión y generar los ejes. |
| 2 · Plano y validación | Validar y ver la cantidad de muros, errores y avisos. Al elegir un problema se selecciona la entidad en el dibujo y se encuadra la vista. |
| 3 · Estructuras (muros) | Tabla con handle, capa, longitud, material y espesor; el material y el espesor se editan en la fila. Filtros: todos, sin material y seleccionados. Seleccionar muros o líneas en el dibujo y asignarles material y espesor en bloque (las líneas pasan a `WIFIHEAT_MUROS`). Muestra la pérdida del material elegido en 2,4 y 5 GHz y marca los muros que atraviesa el trayecto hacia el último punto consultado. |
| 4 · Punto de acceso | Posición X/Y (escrita o con un clic en el dibujo), banda con su frecuencia, potencia de transmisión, ganancias de antena, pérdidas de cable y resolución de la grilla. Cada cambio deja el mapa marcado como desactualizado. |
| 5 · Calcular y mostrar | Área de cobertura (polilínea o rectángulo de los muros), cálculo con resumen (celdas, RSSI mínimo y máximo, tiempo), atenuación del mapa sin recalcular y leyenda de colores. |
| 6 · Consultar punto | Cada clic en el dibujo muestra distancia, L_0, muros cruzados con su material y su pérdida, P_RX y clase, con el texto completo de la explicación. Termina con Esc o Enter. |
| 7 · Exportar PNG | Guarda la imagen del último cálculo y muestra la ruta del archivo. |

Los valores se confirman con Enter o al salir del campo. Si AutoCAD está ejecutando otro comando, el panel lo avisa y no modifica el dibujo hasta que termine.

## Probar con el ejemplo

`samples/demo.dwg` es una planta sintética de 30 × 14 m con seis ambientes: muros exteriores de hormigón (0,25 m); muros interiores de ladrillo macizo (0,20 m) en x = 10 m y en y = 7 m desde x = 20 m, de ladrillo hueco (0,15 m) en x = 20 m y de placa de yeso (0,10 m) en y = 7 m entre x = 0 y x = 20 m. El punto de acceso está en (2; 3) m, en 5 GHz con 20 dBm. Todo está ya asignado.

1. Abrir `samples/demo.dwg` en AutoCAD 2027.
2. Ejecutar `NETLOAD` y elegir `src\WifiHeat.AutoCAD\bin\Debug\net10.0-windows\WifiHeat.AutoCAD.dll` (compilar antes con `dotnet build WifiHeat.slnx`).
3. Ejecutar `SCRIPT` y elegir `samples/demo.scr`. Valida los muros, calcula el mapa, consulta dos puntos —(8; 5) m, en el mismo ambiente que el AP, y (26; 11) m, detrás de tres muros— y exporta `samples/demo-wifiheat.png`. La imagen del mapa que se inserta en el dibujo queda en `samples/demo_wifiheat_NNN.png`.

Notas:

- Con `SECURELOAD` activo, `NETLOAD` exige que la carpeta del plugin esté en `TRUSTEDPATHS`: agregar `src\WifiHeat.AutoCAD\bin\Debug\net10.0-windows`. Si AutoCAD bloquea también el script, agregar la carpeta `samples`.
- `demo.scr` ejecuta además `NETLOAD` con la ruta relativa a `demo.dwg`, así que el paso 2 solo es imprescindible si ese `NETLOAD` es bloqueado.
- No desactivar `SECURELOAD`.

## Estructura

- `src/WifiHeat.Core`: modelo de propagación, geometría, materiales y grilla; no depende de AutoCAD.
- `src/WifiHeat.AutoCAD`: comandos, lectura del dibujo y salida en AutoCAD. No usa la interfaz de AutoCAD, así que también se carga en `accoreconsole`.
- `src/WifiHeat.AutoCAD.UI`: panel WPF (`WIFIPANEL`). Los modelos de vista no dependen de AutoCAD; solo la carpeta `Host` usa la paleta y el editor.
- `tests/WifiHeat.Core.Tests`: pruebas de Core con xUnit.
- `tests/WifiHeat.AutoCAD.UI.Tests`: pruebas de los modelos de vista del panel con un dibujo en memoria.

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
