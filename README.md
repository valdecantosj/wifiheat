# WifiHeat

Plugin de AutoCAD que estima la cobertura Wi-Fi de un plano 2D. Calcula el RSSI de cada punto a partir de la posición del punto de acceso, la pérdida en el espacio libre y la atenuación de los muros que cruza el trayecto directo. Dos modelos opcionales suman además la señal que rodea los obstáculos por difracción; el segundo agrega también un rebote en cada muro y columna.

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
| `WIFIEJES` | Opcional, para planos con muros dibujados con sus dos caras. Pide la zona de la planta (un rectángulo que encierre una sola planta), las capas de las caras (sugiere `muros`; las capas `0` y `ventanas` solo se procesan si se nombran) y el umbral de unión de tramos (0,20 m por defecto). Propone un eje a mitad de distancia entre cada par de segmentos paralelos separados hasta 0,50 m, une los tramos alineados cuya separación no supera el umbral y cierra esquinas y uniones en T. Crea los ejes en `WIFIHEAT_MUROS` con el espesor estimado y sin material, sin modificar las entidades originales, e informa los segmentos que quedaron sin par. Al repetirlo sobre la misma zona no duplica: conserva los ejes que coinciden (con su material) y reemplaza el resto de los generados antes. Las polilíneas cerradas de hasta 1,5 m de lado y los hatches no se toman como caras: se informan como posibles columnas. La opción `Líneas`, en lugar de la zona, aplica el mismo detector solo a las líneas y polilíneas que se elijan, sin importar su capa; no borra nada y conserva los ejes que ya existan en ese lugar. |
| `WIFIWALL` | Asigna material y espesor a las líneas o polilíneas seleccionadas y las pasa a `WIFIHEAT_MUROS`. Enter en el espesor conserva el de cada muro (0,15 m si no tiene). Si la selección trae las dos caras de un mismo muro (paralelas, a no más de 0,50 m), pregunta `Unir caras [Sí/No]`: con Sí crea un eje con el material elegido y el espesor medido, deja las caras en su capa y solo las líneas sin par pasan a `WIFIHEAT_MUROS`. Si en lugar del material se escribe `columna`, marca como columnas de hormigón armado los hatches y las polilíneas cerradas elegidos y pide la armadura (baja, media o alta): crea una polilínea cerrada por columna en `WIFIHEAT_MUROS` sin tocar las figuras originales. Un hatch y su contorno dan una sola columna, repetir no duplica, y las figuras de más de 1,5 m de lado o con arcos o islas no se generan. |
| `WIFIVALIDATE` | Lee los muros y las columnas y lista errores y avisos con el handle de cada entidad (por ejemplo, `FALTA PROPIEDAD`). Avisa `DOBLE CONTEO` cuando dos muros son paralelos y están a menos de 0,50 m (las dos caras de un mismo muro, que sumarían dos veces) y ofrece convertirlos en un eje; las caras pasan a la capa `WIFIHEAT_CARAS`, que no se lee como muro. |
| `WIFIADDAP` | Ubica el punto de acceso y pide banda (2,4 o 5 GHz) y potencia de transmisión. |
| `WIFIAREA` | Opcional. Define el área de cobertura con una polilínea cerrada de tramos rectos; la opción `Rectángulo` vuelve al valor por defecto, el rectángulo que encierra los muros. Fuera del área no se calcula ni se pinta. |
| `WIFICALCULATE` | Calcula la grilla y dibuja el mapa de calor y la leyenda. Opciones: `Resolución` (tamaño de celda, 0,50 m por defecto, entre 0,10 y 5 m), `Atenuación` (0 a 90 %, 40 % por defecto, que se aplica también al mapa ya dibujado) y `Modelo` (`Recta`, el valor por defecto, `Difracción` o `Reflexiones`; los dos últimos piden además las difracciones máximas por trayecto, de 1 a 4, con 3 por defecto); los valores se mantienen mientras el dibujo esté abierto. Enter calcula con los valores vigentes. El resultado y la leyenda nombran el modelo usado. El mapa es una imagen PNG con degradé continuo, opaca dentro del área y transparente fuera (`<dibujo>_wifiheat_NNN.png`, un archivo nuevo por cálculo junto al DWG, o en la carpeta temporal si el dibujo no está guardado; se borran los anteriores). Se inserta en `WIFIHEAT_OVERLAY` debajo del resto del dibujo y se atenúa con la propiedad Atenuación (Fade) de la imagen. |
| `WIFIQUERY` | Explica el RSSI estimado en un punto: distancia, muros y columnas cruzados, pérdidas y potencia recibida. Con los modelos con difracción o reflexiones agrega la cantidad de trayectos sumados y los cinco que más aportan, con su longitud, su pérdida y su parte de la potencia. Con el modelo con reflexiones lista además todos los muros y columnas donde rebota la señal («Muros donde rebota»), con su material, el punto de rebote, el ángulo de incidencia, la pérdida del rebote y su parte de la potencia. Si el punto o el AP están dentro de una columna, lo avisa en lugar de calcular. |
| `WIFIARMADURA` | Reemplaza el mapa por la pérdida en dB que suma la armadura de las columnas: la diferencia entre el cálculo normal y el mismo cálculo con las columnas como hormigón sin armadura. Solo se pinta el trayecto que cruza alguna columna (7,5 dB por cruce con armadura alta); el resto queda transparente. No es un nivel de señal. Usa una resolución de 0,20 m como máximo (si la de la sesión era mayor, lo avisa y la sesión no cambia), con su propia leyenda y una imagen `<dibujo>_wifiarmadura_NNN.png`. Informa el efecto máximo, las celdas afectadas y la resolución usada; sin columnas con armadura avisa que no hay nada que mostrar. Con el modelo con difracción la diferencia sale de dos cálculos completos y es mucho menor, porque detrás de la columna domina la señal que la rodea. `WIFICALCULATE` vuelve al mapa de cobertura. |
| `WIFIEXPORT` | Exporta a PNG la misma imagen del mapa de calor, con la misma atenuación, junto con los muros, el punto de acceso y la leyenda. |
| `WIFIPANEL` | Abre el panel acoplable con todo el recorrido (ver [Panel](#panel)). Solo está disponible si se cargó `WifiHeat.AutoCAD.UI.dll`. |
| `WIFIINFO` | Lista unidades, extensión, capas y entidades del espacio modelo. |

Orden de uso: `WIFISETUP` → `WIFIEJES` (opcional) → `WIFIWALL` → `WIFIVALIDATE` → `WIFIADDAP` → `WIFIAREA` (opcional) → `WIFICALCULATE` → `WIFIQUERY` → `WIFIEXPORT`. `WIFIARMADURA` (opcional) se usa después de calcular, si hay columnas con armadura.

Cada muro se dibuja como un eje (una línea o polilínea de tramos rectos por muro). El trayecto directo suma una vez la pérdida de cada eje que cruza; en uniones y cruces se cuenta una sola vez la pérdida mayor. Una columna es un contorno cerrado y suma, una sola vez por trayecto, la pérdida del hormigón más un adicional por su nivel de armadura (ver [docs/modelo.md](docs/modelo.md)).

Modelos de propagación:

| Modelo | Qué calcula |
| --- | --- |
| Línea recta (por defecto) | El trayecto directo: espacio libre más los muros y columnas que cruza. |
| Con difracción | Suma además los trayectos que rodean extremos libres de muro, esquinas y vértices de columna, con la difracción por filo de cuchilla de ITU-R P.526. Detrás de una columna o de una abertura la sombra deja de ser abrupta. Tarda más en calcular. |
| Con difracción y reflexiones | Suma además un rebote en cada muro y cara de columna: el punto de rebote sale del método de las imágenes y la pérdida, del coeficiente de Fresnel con las propiedades eléctricas de ITU-R P.2040 (clave `reflection` de cada material). Es el más lento. |

El modelo se elige en `WIFICALCULATE` (opción `Modelo`) o en el panel, y vale para el mapa, la consulta y el efecto de la armadura. Las fórmulas, lo que está publicado y lo que es convención del proyecto están en [docs/modelo.md](docs/modelo.md#modelo-con-difracción) y [docs/modelo.md](docs/modelo.md#modelo-con-reflexiones).

## Panel

`WIFIPANEL` abre una paleta acoplable que reúne el mismo recorrido que los comandos. El panel no calcula: muestra y edita los datos del dibujo activo y usa las mismas operaciones que los comandos, así que se pueden combinar. Sigue el tema claro u oscuro de AutoCAD y se actualiza al cambiar de dibujo y al terminar cada comando.

| Sección | Qué permite |
| --- | --- |
| 1 · Preparar plano | Elegir la unidad del dibujo. Para muros de doble cara: «Elegir zona de la planta…» (marcar con dos clics el rectángulo que encierra una sola planta), ver las capas que contiene con su cantidad de segmentos y de ejes, marcar las que son caras de muro, ajustar el umbral de unión y generar los ejes. «Elegir líneas…» genera ejes solo entre las líneas que se elijan en el dibujo, sin importar su capa. Si en la zona hay figuras cerradas pequeñas (posibles columnas), lo avisa y ofrece «Marcar como columnas». |
| 2 · Plano y validación | Validar y ver la cantidad de muros, errores y avisos. Al elegir un problema se selecciona la entidad en el dibujo y se encuadra la vista. Si hay muros paralelos que suman la pérdida dos veces, lo avisa y ofrece «Convertir en ejes». |
| 3 · Estructuras (muros y columnas) | Tabla con handle, capa, longitud, material y espesor; el material y el espesor se editan en la fila. Filtros: todos, sin material y seleccionados. Seleccionar muros o líneas en el dibujo y asignarles material y espesor en bloque (las líneas pasan a `WIFIHEAT_MUROS`); si la selección trae las dos caras de un muro, ofrece «Unir en ejes» o «Ignorar». Con el tipo Columna, el material queda en hormigón y se elige la armadura, con su ejemplo y su adicional en dB; la selección admite hatches y polilíneas cerradas. Muestra la pérdida del material elegido en 2,4 y 5 GHz y marca los muros que atraviesa el trayecto hacia el último punto consultado. |
| 4 · Punto de acceso | Posición X/Y (escrita o con un clic en el dibujo), banda con su frecuencia, potencia de transmisión, ganancias de antena, pérdidas de cable y resolución de la grilla. Cada cambio deja el mapa marcado como desactualizado. |
| 5 · Calcular y mostrar | Área de cobertura (polilínea o rectángulo de los muros), lista «Modelo de propagación» («Línea recta», «Con difracción» o «Con difracción y reflexiones», con una línea de ayuda; cambiarla deja el mapa desactualizado), cálculo con resumen (celdas, RSSI mínimo y máximo, tiempo y modelo), atenuación del mapa sin recalcular y leyenda de colores. El botón «Ver efecto de la armadura» (equivale a `WIFIARMADURA`) reemplaza el mapa por la pérdida que suma la armadura; Calcular vuelve al mapa de cobertura. |
| 6 · Consultar punto | Cada clic en el dibujo muestra distancia, L_0, muros cruzados con su material y su pérdida, P_RX y clase, con el texto completo de la explicación. Con los modelos con difracción o reflexiones lista además los trayectos sumados, con su longitud, su pérdida y su parte de la potencia. Con reflexiones, «Muros donde rebota» muestra, igual que «Muros cruzados», cada muro o columna donde rebota la señal con su material, el punto de rebote, el ángulo, la pérdida del rebote y la parte de la potencia, y «Ver en el dibujo» los resalta. Termina con Esc o Enter. |
| 7 · Exportar PNG | Guarda la imagen del último cálculo y muestra la ruta del archivo. |

Los valores se confirman con Enter o al salir del campo. Si AutoCAD está ejecutando otro comando, el panel lo avisa y no modifica el dibujo hasta que termine.

Mientras el panel espera algo en el dibujo, arriba se ve un aviso fijo con el modo (por ejemplo, «MODO: UBICAR PUNTO DE ACCESO»), la instrucción y el botón «CANCELAR (ESC)». El resultado de cada acción aparece al pie: ✖ error, ⚠ advertencia, ✔ éxito o ℹ información, con un título corto y el detalle; cuando hay algo para decidir, el mensaje trae los botones de la acción sugerida. Los errores y advertencias quedan hasta cerrarlos o hasta la acción siguiente. El historial plegable guarda los últimos 50 mensajes y se puede copiar.

## Probar con el ejemplo

`samples/demo.dwg` es una planta sintética de 30 × 14 m con seis ambientes: muros exteriores de hormigón (0,25 m); muros interiores de ladrillo macizo (0,20 m) en x = 10 m y en y = 7 m desde x = 20 m, de ladrillo hueco (0,15 m) en x = 20 m y de placa de yeso (0,10 m) en y = 7 m entre x = 0 y x = 20 m. El punto de acceso está en (2; 3) m, en 5 GHz con 20 dBm. Todo está ya asignado.

1. Abrir `samples/demo.dwg` en AutoCAD 2027.
2. Ejecutar `NETLOAD` y elegir `src\WifiHeat.AutoCAD\bin\Debug\net10.0-windows\WifiHeat.AutoCAD.dll` (compilar antes con `dotnet build WifiHeat.slnx`).
3. Ejecutar `SCRIPT` y elegir `samples/demo.scr`. Valida los muros, calcula el mapa, consulta dos puntos —(8; 5) m, en el mismo ambiente que el AP, y (26; 11) m, detrás de tres muros— y exporta `samples/demo-wifiheat.png`. La imagen del mapa que se inserta en el dibujo queda en `samples/demo_wifiheat_NNN.png`.

Para trabajar desde el panel en lugar de los comandos:

1. Con el ejemplo abierto, agregar `src\WifiHeat.AutoCAD.UI\bin\Debug\net10.0-windows` a `TRUSTEDPATHS` y ejecutar `NETLOAD` con `WifiHeat.AutoCAD.dll` y después con `WifiHeat.AutoCAD.UI.dll`, ambos de esa carpeta.
2. Ejecutar `WIFIPANEL`: se abre la paleta acoplable con siete secciones, de "Preparar plano" a "Exportar PNG".
3. En "Calcular y mostrar", pulsar "Calcular"; en "Consultar punto", pulsar "Consultar punto…" y hacer clic en el plano para ver la señal en cada punto (Esc termina).

Notas:

- Con `SECURELOAD` activo, `NETLOAD` exige que la carpeta del plugin esté en `TRUSTEDPATHS`: agregar `src\WifiHeat.AutoCAD\bin\Debug\net10.0-windows`. Si AutoCAD bloquea también el script, agregar la carpeta `samples`.
- `demo.scr` ejecuta además `NETLOAD` con la ruta relativa a `demo.dwg`, así que el paso 2 solo es imprescindible si ese `NETLOAD` es bloqueado.
- No desactivar `SECURELOAD`.
- En el ejemplo los ambientes están cerrados (no hay aberturas ni extremos libres de muro), así que la línea recta y la difracción dan el mismo mapa; el modelo con reflexiones sí cambia, porque suma los rebotes en las paredes. Para ver el efecto de la difracción, agregar una columna (`WIFIWALL` con material `columna`) o un muro con un extremo libre, y calcular con cada modelo.

## Guía de uso

Recorrido paso a paso sobre `samples/demo.dwg`, configuración del punto de acceso, lectura de los colores y problemas frecuentes: [docs/guia-de-uso.md](docs/guia-de-uso.md).

## Ramas

- `main`: versión estable con el panel.
- `dev/stable`: versión de la entrega, con el doble conteo resuelto, las columnas con armadura y el mapa del efecto de la armadura.
- `feature/diffraction`: agrega el modelo con difracción, elegible junto al de línea recta.
- `feature/reflection`: agrega el modelo con difracción y reflexiones (un rebote por trayecto).

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
