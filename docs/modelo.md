# Modelo de propagación

Hay tres modelos, que se eligen al calcular:

| Modelo | Qué considera |
| --- | --- |
| Línea recta (por defecto) | El trayecto directo entre el punto de acceso (AP) y el punto: espacio libre más los muros y columnas que cruza. |
| Con difracción | Además, los trayectos que rodean extremos de muro, esquinas y columnas. Se suman las potencias de todos los trayectos. |
| Con difracción y reflexiones | Además, un rebote en cada muro y cara de columna, con la pérdida de reflexión de Fresnel. |

Las secciones que siguen describen el modelo de línea recta, que también es la base de los otros. Lo que agregan está en [Modelo con difracción](#modelo-con-difracción) y [Modelo con reflexiones](#modelo-con-reflexiones).

## Pérdida en el espacio libre

$$L_0\,[\mathrm{dB}] = 32{,}45 + 20\log_{10} f\,[\mathrm{MHz}] + 20\log_{10} d\,[\mathrm{km}]$$

| Símbolo | Significado | Unidad |
| --- | --- | --- |
| $L_0$ | Pérdida en el espacio libre | dB |
| $f$ | Frecuencia representativa de la banda: 2437 (2,4 GHz) o 5180 (5 GHz) | MHz |
| $d$ | Distancia AP–punto, con un mínimo de 1 m para que $L_0$ sea finita junto al AP | km |

Por qué: la potencia se dispersa en una esfera que crece con la distancia. Es la forma logarítmica de la ecuación de Friis; ITU-R P.525 redondea la constante a 32,4.

## Pérdida por materiales

$$L_{materiales}\,[\mathrm{dB}] = \sum_i L_i(\text{banda})$$

| Símbolo | Significado | Unidad |
| --- | --- | --- |
| $L_{materiales}$ | Pérdida total de los muros cruzados | dB |
| $L_i$ | Pérdida del muro $i$ según su material y la banda, de `config/materials.json` | dB |

Por qué: cada muro que cruza el trayecto directo atenúa la señal. El espesor no cambia la pérdida de penetración.

Reglas de conteo:

- Cada muro cuenta a lo sumo una vez, aunque el trayecto lo toque en un vértice o en varios de sus tramos.
- Si el trayecto toca varios muros en el mismo punto (con una tolerancia de 1 mm), como en una unión en T, un cruce o una esquina formada por dos muros, ese punto aporta una sola vez la pérdida mayor de esos muros. Lo mismo vale para dos muros superpuestos.
- Los muros que el trayecto toca en puntos distintos se suman.
- Una línea o una polilínea es un solo obstáculo. Para separar muros hay que usar entidades distintas.
- Un muro dibujado con sus dos caras son dos entidades y sumaría dos veces. Por eso las caras se reemplazan por un eje a mitad de distancia, y la validación avisa (`DOBLE CONTEO`) si quedan dos muros paralelos a menos de 0,50 m.

## Columnas de hormigón armado

$$L_{columna}\,[\mathrm{dB}] = L_{hormigón}(\text{banda}) + \Delta_{armadura}$$

| Símbolo | Significado | Unidad |
| --- | --- | --- |
| $L_{columna}$ | Pérdida de una columna cruzada por el trayecto | dB |
| $L_{hormigón}$ | Pérdida del hormigón según la banda, de `config/materials.json` | dB |
| $\Delta_{armadura}$ | Adicional por nivel de armadura, igual en las dos bandas | dB |

| Armadura | Ejemplo en una columna de 30 × 30 cm | Cuantía | $\Delta_{armadura}$ |
| --- | --- | --- | --- |
| Baja | 4 barras Ø20 | ≈ 1,4 % | 2,5 dB |
| Media | 8 barras Ø20 | ≈ 2,8 % | 5 dB |
| Alta | 12 barras Ø20 | ≈ 4,2 % | 7,5 dB |

Por qué: el acero de la armadura refleja y dispersa parte de la señal, además de lo que atenúa el hormigón.

Los adicionales son una convención de este proyecto, no un dato publicado: no hay una tabla que relacione la cuantía de acero con decibeles. Su orden de magnitud sale de comparar las tablas de hormigón sin armar (22,8 / 44,8 dB en iBwave) con las de hormigón armado (25–30 dB o más / 50 dB o más en Interline), que difieren en unos 5 dB. Las cuantías son un cálculo geométrico: área de las barras sobre el área de la columna.

Reglas de conteo:

- La columna es un contorno cerrado y cuenta una sola vez por trayecto, aunque este corte dos de sus lados o roce una esquina.
- Si la columna toca un muro en el punto donde pasa el trayecto (tolerancia de 1 mm), vale la regla de las uniones: se cuenta una vez la pérdida mayor.
- Si el AP o el punto consultado están dentro de una columna, no se calcula y se avisa.

Límites: el modelo de línea recta solo considera el trayecto directo, sin difracción ni rebotes, así que detrás de la columna aparece una sombra en cuña que en la realidad se rellena en parte (el modelo con difracción la rellena con la señal que rodea la columna). La armadura real (barras y estribos separados 10 a 20 cm, frente a una longitud de onda de unos 12 cm en 2,4 GHz y 6 cm en 5 GHz) no es una malla cerrada. La altura no interviene: el plano es 2D.

### Mapa del efecto de la armadura

Además del mapa de cobertura se puede dibujar la diferencia entre dos cálculos de la misma escena: el normal y otro con las columnas como hormigón sin armadura ($\Delta_{armadura} = 0$). Por celda:

$$\Delta L\,[\mathrm{dB}] = L_{materiales}(\text{con armadura}) - L_{materiales}(\text{sin armadura}) = P_{RX}(\text{sin armadura}) - P_{RX}(\text{normal})$$

- **Es una diferencia en dB, no un nivel de señal.** Los colores (de amarillo claro a rojo oscuro, cinco tramos entre 0 y el máximo) no se comparan con la escala de RSSI.
- Una celda vale 0 y queda transparente si el trayecto desde el AP no cruza ninguna columna, o si cruza una pero en una unión con un muro de mayor pérdida (se cuenta una vez la mayor y la armadura no agrega nada).
- Con una columna el valor es su adicional (2,5; 5 o 7,5 dB); un trayecto que cruza dos columnas suma los adicionales de ambas.
- Se calcula con una celda de 0,20 m como máximo, para que se vea la sombra de una columna de 0,30 m. Si la resolución elegida era mayor, solo este mapa usa 0,20 m.
- Los adicionales por nivel de armadura son una convención del proyecto (ver arriba): el mapa muestra el efecto de esa convención, no una medición.
- Con el modelo con difracción la diferencia sale de dos cálculos completos, con y sin armadura, y ya no vale un adicional fijo: detrás de una columna domina la señal que la rodea, así que la armadura cambia mucho menos el resultado. Las celdas donde la diferencia no llega a 0,05 dB quedan sin valor.

## Balance de enlace

$$P_{RX}\,[\mathrm{dBm}] = P_{TX} - L_{cable,TX} + G_{antena,TX} - L_0 - L_{materiales} + G_{antena,RX} - L_{cable,RX}$$

| Símbolo | Significado | Unidad |
| --- | --- | --- |
| $P_{TX}$ | Potencia del transmisor | dBm |
| $L_{cable,TX}$, $L_{cable,RX}$ | Pérdida del cable en transmisión y en recepción | dB |
| $G_{antena,TX}$, $G_{antena,RX}$ | Ganancia de antena en transmisión y en recepción | dBi |
| $P_{RX}$ | Potencia recibida; $\mathrm{RSSI}$ estimado $= P_{RX}$ | dBm |

Por qué: en escala logarítmica, las ganancias y las pérdidas de la cadena se suman y se restan.

## Modelo con difracción

Detrás de un obstáculo la señal no desaparece: una parte lo rodea. Este modelo suma esos trayectos al directo. Es un modelo 2D, en planta.

### Trayectos y suma

En cada punto se consideran:

1. El trayecto directo AP–punto.
2. Los trayectos difractados: polilíneas AP → v₁ → … → v_k → punto que rodean k vértices difractantes, con k ≤ 3 (configurable de 1 a 4). Cada tramo puede atravesar materiales.

$$L_k\,[\mathrm{dB}] = L_0(d_k) + \sum_{\text{tramos}} L_{materiales} + L_{difracción}$$

$$P_{RX}\,[\mathrm{dBm}] = P_{TX} - L_{cable,TX} + G_{antena,TX} + G_{antena,RX} - L_{cable,RX} + 10\log_{10}\sum_k 10^{-L_k/10}$$

| Símbolo | Significado | Unidad |
| --- | --- | --- |
| $L_k$ | Pérdida total del trayecto $k$ | dB |
| $d_k$ | Longitud total del trayecto $k$, con el mínimo de 1 m | m |
| $L_{difracción}$ | Suma de las pérdidas de los vértices del trayecto (ver abajo) | dB |

Las potencias se suman sin fases. Se descartan los trayectos que pierden 30 dB más que el mejor.

### Vértices difractantes

Un vértice difractante es un nodo de la geometría alrededor del cual puede doblar un trayecto. Los nodos son los extremos de todos los segmentos, unidos con una tolerancia de 1 mm. En cada nodo se miran las direcciones de los segmentos que salen de él: si el mayor hueco angular entre direcciones supera 181°, el nodo es difractante y ese hueco es su **sector libre**.

| Nodo | Sector libre | ¿Es difractante? |
| --- | --- | --- |
| Extremo libre de un muro | 360° | Sí |
| Esquina en L | 270° | Sí |
| Vértice convexo de una columna | 270° | Sí |
| Unión en T, cruce o prolongación recta | 180° o menos | No |

El nodo que cae dentro de una columna se descarta. Un trayecto **rodea** un vértice cuando sus dos direcciones caen en el sector libre y forman, por el lado libre, más de 180°. El exceso sobre 180° es el ángulo de difracción $\theta$.

### Pérdida por un vértice

$$\nu = \theta\,\sqrt{\frac{2\,d_1 d_2}{\lambda\,(d_1 + d_2)}}$$

$$J(\nu)\,[\mathrm{dB}] = 6{,}9 + 20\log_{10}\left(\sqrt{(\nu - 0{,}1)^2 + 1} + \nu - 0{,}1\right)\quad\text{para } \nu > -0{,}78;\qquad J(\nu) = 0 \text{ para } \nu \le -0{,}78$$

| Símbolo | Significado | Unidad |
| --- | --- | --- |
| $\theta$ | Ángulo de difracción | rad |
| $d_1$, $d_2$ | Distancias del vértice al punto anterior y al siguiente del trayecto | m |
| $\lambda$ | Longitud de onda, $299{,}792458 / f\,[\mathrm{MHz}]$ | m |
| $\nu$ | Parámetro de difracción | — |
| $J(\nu)$ | Pérdida por difracción; $J(0) = 6{,}03$ dB | dB |

Por qué: es la difracción por filo de cuchilla de ITU-R P.526 (§4.1), con $\nu$ en su forma angular. Trata el borde como una cuña absorbente, así que no usa las propiedades eléctricas del material.

### Zona de transición

Los vértices que un tramo deja cerca, del lado libre y sin rodearlos, también cuentan, con $\nu$ negativo (entre −0,78 y 0). Así el resultado no salta al cruzar el borde de una sombra. Por tramo se consideran hasta 8, los de mayor $\nu$. Ejemplo a 5 GHz: un trayecto de 6,3 m que pasa a entre 6 y 9 cm de las esquinas de una columna, sin tocarla, pierde 3,9 dB respecto del espacio libre.

### Varios vértices

Los elementos de un trayecto (los vértices rodeados y los de transición) se combinan con la construcción de Deygout, en forma angular:

1. La **arista principal** es el elemento de mayor $\nu$ respecto de la cuerda AP–punto. Su pérdida es $J(\nu)$.
2. Los elementos anteriores se resuelven respecto de la cuerda AP–principal y los posteriores respecto de principal–punto, de forma recursiva.
3. El signo de $\nu$ sale del giro con signo de la cuerda en ese vértice, comparado con el sentido en que el trayecto lo rodea. Un vértice rodeado nunca toma $\nu < 0$.

**Corrección por aristas próximas.** La construcción sobrestima la pérdida cuando dos aristas están muy cerca, como las dos esquinas de una misma columna. Una arista secundaria aporta

$$J_{sec} = e\cdot\left(J - G\cdot\min\left(1,\ \frac{J}{J(0)}\right)\right),\qquad G = 12 - 20\log_{10}\left(\frac{2}{1 - \alpha/\pi}\right),\qquad \tan\alpha = \sqrt{\frac{b\,(a + b + c)}{a\,c}}$$

| Símbolo | Significado | Unidad |
| --- | --- | --- |
| $a$, $b$, $c$ | Distancias extremo–arista, arista–arista y arista–extremo, en el orden del trayecto | m |
| $G$ | Corrección: de 6 dB (aristas pegadas) a 0 dB (aristas lejanas) | dB |
| $e$ | Escala: 1 en un vértice rodeado; en uno de transición, $\min(1,\ J_{principal}/J(0))$, acumulada | — |

**Obstáculos que dejan pasar señal.** Con $T = 10^{-L/10}$ la transmisión en potencia del obstáculo del vértice ($L$ es su pérdida de penetración):

| Vértice | Se suma |
| --- | --- |
| Rodeado | su pérdida efectiva más $-10\log_{10}(1 - T)$ |
| De transición | $-10\log_{10}\left(T + (1 - T)\cdot 10^{-J/10}\right)$ |

Con un material opaco ($T \approx 0$) queda el filo de cuchilla puro; con uno casi transparente, el borde casi no difracta.

### Materiales de cada tramo

Vale la regla de uniones del modelo de línea recta (los obstáculos tocados en el mismo punto cuentan una vez, la pérdida mayor), con tres diferencias:

1. El tramo se recorta 2 mm en el extremo que es un vértice, para no contar el obstáculo que rodea.
2. Una columna cuenta solo si el tramo pasa por su interior. Correr a lo largo de una cara o tocar un vértice no cuenta.
3. Un muro no cuenta si el tramo corre a lo largo de él, ni si lo toca a menos de 1 µm de un extremo que es vértice difractante: ese trayecto es rasante y se trata como zona de transición.

El trayecto directo de este modelo usa la misma regla.

### Búsqueda de trayectos

Para cada escena, AP y frecuencia se precalculan las llegadas a cada vértice: la directa desde el AP y las que rodean antes uno o dos vértices. Por vértice y por cantidad de difracciones previas se conservan las 4 mejores, ordenadas con una estimación. En cada punto se evalúa la pérdida completa de las llegadas cuyo trayecto rodea el vértice hacia el punto.

| Tope | Valor |
| --- | --- |
| Difracciones por trayecto | 3 (de 1 a 4) |
| Llegadas conservadas por vértice y nivel | 4 |
| Vértices de transición por tramo | 8, con $\nu > -3$ respecto del tramo |
| Poda | 30 dB sobre el mejor trayecto |
| Sector libre mínimo | 181° |

Los empates se resuelven por las coordenadas de los vértices, para que el resultado no dependa del orden en que se leen los obstáculos.

### Qué está publicado y qué es convención del proyecto

| Publicado | Convención de este proyecto |
| --- | --- |
| $J(\nu)$ y $\nu$ en forma angular (ITU-R P.526, §4.1) | Aplicar el filo de cuchilla en planta, alrededor de esquinas y con ángulos grandes |
| Construcción de Deygout: arista principal y secundarias (Deygout, 1966) | Su forma angular, con el giro con signo |
| Factor $12 - 20\log_{10}\left(2/(1 - \alpha/\pi)\right)$ para dos aristas (ITU-R P.526, §4.3) | Usarlo como corrección continua y ponderada para aristas próximas |
| Suma de potencias de los bordes de una pantalla de ancho finito (ITU-R P.526, §5.1) | Ponderar la difracción con $(1 - T)$ en obstáculos que dejan pasar señal |
| | La zona de transición con hasta 8 vértices por tramo |
| | Los topes: 3 difracciones, 4 llegadas y poda de 30 dB |

Nada de esto está calibrado con mediciones.

### Ejemplos

Valores que da el modelo a 5 GHz con 20 dBm, sin ganancias ni cables. No son mediciones.

| Caso | Línea recta | Con difracción |
| --- | --- | --- |
| Detrás de una columna de 30 × 30 cm con armadura media, con el AP y el punto a unos 3 m de cada lado | −92,5 dBm | −54,2 dBm |
| Detrás del extremo libre de un muro de hormigón, a 3 m del muro | −87 a −88 dBm | de −76 a −66 dBm, según qué tan adentro de la sombra |
| A la vuelta de una esquina de hormigón | −135 dBm | de −73 a −80 dBm |
| Detrás de dos y de tres columnas alineadas con el AP | −141,9 y −196,1 dBm | −67,8 y −75,2 dBm |

En el primer caso la columna cuesta 49,8 dB si se la atraviesa y 11,5 dB si se la rodea.

## Modelo con reflexiones

Además de los trayectos del modelo con difracción, suma los que llegan al punto con un rebote en un muro o en una cara de columna (reflexión de primer orden). Es un modelo 2D, en planta.

### Geometría: método de las imágenes

Para cada segmento reflector se calcula la imagen del AP respecto de la recta que lo contiene. El trayecto reflejado es AP → R → punto, donde R es la intersección de la recta imagen–punto con el segmento.

$$d_1 + d_2 = \lvert AP' - P \rvert, \qquad \cos\theta = \frac{h_{AP} + h_P}{d_1 + d_2}$$

| Símbolo | Significado | Unidad |
| --- | --- | --- |
| $AP'$ | Imagen del AP respecto de la recta del segmento | m |
| $d_1$, $d_2$ | Longitudes de los tramos AP–R y R–punto | m |
| $h_{AP}$, $h_P$ | Distancias del AP y del punto a la recta | m |
| $\theta$ | Ángulo de incidencia, medido desde la normal | rad |

El rebote vale solo si se cumplen tres condiciones:

1. El AP y el punto están del mismo lado de la recta.
2. En una columna, ese lado es el exterior de la cara.
3. R cae en el interior del segmento.

El rebote se calcula sobre el eje del muro, no sobre sus caras.

### Pérdida por reflexión

Se usa el coeficiente de Fresnel de una interfaz aire–material (ITU-R P.2040, ec. 31a). La polarización es TE, con el campo eléctrico paralelo a la pared, que es la de una antena vertical frente a una pared vertical.

$$\Gamma_{TE} = \frac{\cos\theta - \sqrt{\varepsilon_{rc} - \sin^2\theta}}{\cos\theta + \sqrt{\varepsilon_{rc} - \sin^2\theta}}, \qquad L_{refl}\,[\mathrm{dB}] = -20\log_{10}\lvert\Gamma_{TE}\rvert$$

$$\varepsilon_{rc} = \varepsilon' - j\,\frac{17{,}98\,\sigma}{f}, \qquad \varepsilon' = a\,f^{\,b}, \qquad \sigma = c\,f^{\,d}$$

con $f$ en GHz y $\sigma$ en S/m (P.2040, ecs. 57 a 59). Los coeficientes salen de la Tabla 3 de la P.2040-4 y están en `config/materials.json`, en la clave `reflection` de cada material.

| Material | Fila de P.2040 | $a$ | $b$ | $c$ | $d$ |
| --- | --- | --- | --- | --- | --- |
| Hormigón (y columnas) | Concrete | 5,24 | 0 | 0,0462 | 0,7822 |
| Ladrillo macizo | Brick | 3,91 | 0 | 0,0238 | 0,16 |
| Ladrillo hueco | Brick (supuesto) | 3,91 | 0 | 0,0238 | 0,16 |
| Placa de yeso | Plasterboard | 2,73 | 0 | 0,0085 | 0,9395 |
| Vidrio común | Glass | 6,31 | 0 | 0,0036 | 1,3394 |

La P.2040 no tiene una fila para el ladrillo hueco: se usan los valores de Brick. Un material sin `reflection` no refleja, y el cálculo lo avisa.

Valores de $L_{refl}$ que resultan:

| Material | 0° | 45° | 70° |
| --- | --- | --- | --- |
| Hormigón | 8,1 dB | 5,8 dB | 2,9 dB |
| Ladrillo | 9,7 dB | 7,0 dB | 3,5 dB |
| Placa de yeso | 12,2 dB | 8,9 dB | 4,5 dB |
| Vidrio | 7,3 dB | 5,3 dB | 2,6 dB |

Varían menos de 0,05 dB entre 2437 y 5180 MHz. En incidencia rasante, $\lvert\Gamma\rvert$ tiende a 1 y la pérdida a 0 dB.

### Pérdida del trayecto reflejado

$$L_{refl,total}\,[\mathrm{dB}] = L_0(d_1 + d_2) + L_{materiales}(AP\text{–}R) + L_{materiales}(R\text{–}P) + L_{refl}$$

Los materiales de cada tramo siguen las reglas de [Materiales de cada tramo](#materiales-de-cada-tramo): el tramo se recorta 2 mm en R, así que el muro donde rebota no se cuenta como cruzado. El trayecto reflejado se suma en potencia con el directo y los difractados, con la misma poda de 30 dB. Antes de calcular los materiales se descarta el rebote si $L_0 + L_{refl}$ ya supera esa poda.

### Ejemplo

Muro de hormigón, AP y punto a 2 m del muro y a 4 m entre sí, 5 GHz (5180 MHz) y 20 dBm, sin ganancias ni cables:

- Directo: $L_0(4\ \mathrm{m}) = 58{,}78$ dB, o sea $-38{,}78$ dBm.
- Reflejado: $d_1 + d_2 = 5{,}66$ m, $\theta = 45°$, $L_0 = 61{,}79$ dB, $L_{refl} = 5{,}82$ dB y total $67{,}60$ dB.
- Suma: $-38{,}24$ dBm, es decir 0,54 dB más que el directo solo.

El rebote pesa más donde el directo atraviesa muros y el reflejado no.

### Qué está publicado y qué es convención del proyecto

| Publicado | Convención de este proyecto |
| --- | --- |
| Coeficiente de Fresnel TE de una interfaz (ITU-R P.2040, ec. 31a) | Interfaz semi-infinita: no se usa el espesor ni el modelo de losa |
| Ajuste $\varepsilon' = a f^b$, $\sigma = c f^d$ y Tabla 3 (ITU-R P.2040) | Usar la fila de Brick para el ladrillo hueco |
| Reflexión en dieléctricos y modelo de dos rayos con trayecto reflejado (Rappaport, secs. 4.5 y 4.6) | Un solo rebote, en planta, sobre el eje del muro, sumado en potencia |

## Escala de colores

| $P_{RX}$ [dBm] | Color |
| --- | --- |
| ≥ −50 | Verde oscuro |
| −60 a −50 | Verde claro |
| −67 a −60 | Amarillo verdoso |
| −75 a −67 | Amarillo |
| −85 a −75 | Naranja |
| < −85 | Rojo |

Cada intervalo incluye el límite inferior. La escala es orientativa: no garantiza conexión ni calidad de servicio.

El mapa de calor usa un degradé continuo: el color se interpola linealmente en RGB entre colores vecinos de la tabla, y cada color queda anclado en el centro de su intervalo:

| Color | Anclaje [dBm] |
| --- | --- |
| Verde oscuro | −50 (por encima, constante) |
| Verde claro | −55 |
| Amarillo verdoso | −63,5 |
| Amarillo | −71 |
| Naranja | −80 |
| Rojo | −90 (por debajo, constante) |

Así, el color puro aparece en el centro de cada intervalo y en los límites se ve la mezcla de los dos colores vecinos. La leyenda y la consulta de un punto usan los mismos seis colores y los mismos límites de la tabla anterior.

## Limitaciones

- **Modelo 2D, sin pisos ni alturas.** El plano es una sola planta. No hay pérdida entre pisos y no se consideran las alturas del AP, del receptor ni de los muros y las aberturas.
- **Antena isotrópica.** La ganancia se aplica igual en todas las direcciones: no hay diagrama de radiación ni orientación. La polarización solo interviene en el rebote, que supone antenas verticales (TE).
- **Materiales de tablas publicadas, no medidos.** Los valores de `config/materials.json` salen de fuentes de práctica profesional y no son mediciones propias. El del ladrillo hueco es un supuesto intermedio entre la placa de yeso y el ladrillo macizo, sin fuente directa.
- **El espesor no cambia la pérdida de penetración.** Cada muro cruzado resta el valor completo de su material, sea de 10 o de 30 cm.
- **Armadura por niveles como convención.** Los adicionales de 2,5, 5 y 7,5 dB son una convención del proyecto, no una relación medida entre cuantía de acero y decibeles.
- **Línea recta: sin difracción ni reflexiones.** Solo considera el trayecto directo.
- **Con difracción: sin reflexiones.** Suma los trayectos que rodean obstáculos, pero no los rebotes.
- **Con reflexiones: un solo rebote.** No hay trayectos con dos rebotes ni que combinen rebote y difracción.

### Difracción

La señal real rodea los obstáculos angostos. El modelo de línea recta no lo representa. Ejemplo: una columna de 30 cm a 5 GHz.

- Con línea recta, atravesarla cuesta 49,8 dB: 44,8 dB del hormigón más 5 dB de armadura media.
- Una estimación por filo de cuchilla del camino que la rodea da del orden de 12 dB. Con el AP y el punto a 3 m de la columna cada uno y el punto detrás de su eje, $\nu \approx 0{,}72$ y $J(\nu)$ da unos 12 dB. El modelo con difracción, que combina las dos esquinas de cada lado, da 11,5 dB.

Por eso, con línea recta, la sombra detrás de columnas angostas está sobrestimada: se resta más de cuatro veces la pérdida estimada del camino que rodea la columna.

Límites del modelo con difracción:

- **Sin fases.** Se suman potencias, así que no reproduce los desvanecimientos rápidos por interferencia entre trayectos.
- **Filo de cuchilla extrapolado.** Es una teoría de ángulos pequeños y obstáculos lejanos; aquí se usa en giros grandes y a distancias de pocas longitudes de onda.
- **Búsqueda acotada.** Hasta 3 difracciones por trayecto y 4 llegadas por vértice: en plantas con muchos vértices puede faltar algún trayecto débil.
- **Mismas tablas de materiales.** Las pérdidas de penetración siguen saliendo de valores publicados, no de mediciones.
- **Sin calibrar.** Los valores son teóricos; no se compararon con mediciones.

### Reflexiones

El modelo con reflexiones agrega un rebote en muros y columnas. Límites:

- **Un rebote, en planta.** No hay rebotes en techo ni piso, ni dos rebotes en un pasillo.
- **Rebote sobre el eje.** El punto de rebote está en el eje del muro, no en la cara que mira al AP: con muros de 15 a 30 cm, el trayecto se alarga unos centímetros.
- **Interfaz semi-infinita.** No se usa el espesor del muro, así que no hay rebotes internos en la losa ni dependencia con el espesor. Con muros delgados de yeso o vidrio, la reflexión real puede ser bastante distinta.
- **Superficie lisa.** No hay dispersión por rugosidad, aberturas ni muebles.
- **La armadura no cambia el rebote.** Una columna refleja como hormigón; el acero no se modela.
- **Borde especular abrupto.** Al salir R del segmento, el rebote desaparece de golpe: el mapa muestra un escalón donde el rebote pesa en la suma. No se suaviza con difracción.
- **Sin fases.** Igual que en la difracción, se suman potencias.

### Error esperable

En interiores la señal también llega por rebotes en techo y piso y por varios rebotes sucesivos, que no se calculan. Los modelos multi-pared de este tipo tienen errores típicos de 5 a 10 dB frente a mediciones, y el mapa debe leerse como una estimación orientativa de la cobertura, no como una predicción de la potencia en cada punto.

## Referencias

1. ITU-R, Recomendación P.525-5 (11/2024), *Calculation of free-space attenuation*. Pérdida básica de transmisión en el espacio libre.
2. H. T. Friis, "A Note on a Simple Transmission Formula", *Proceedings of the IRE*, vol. 34, n.º 5, pp. 254–256, mayo de 1946. Ecuación de transmisión en el espacio libre.
3. W. Stallings, *Data and Computer Communications*, 10.ª ed., Pearson, 2014. Atenuación, decibeles y dBm.
4. T. S. Rappaport, *Wireless Communications: Principles and Practice*, 2.ª ed., Prentice Hall, 2002. Modelo de espacio libre (sec. 4.2), reflexión (sec. 4.5), modelo de dos rayos (sec. 4.6) y pérdidas por tabiques en interiores (sec. 4.11.1).
5. ITU-R, Recomendación P.2040-4 (09/2025), *Effects of building materials and structures on radiowave propagation in the range of 1 MHz to 450 GHz*. Efecto de los materiales de construcción; coeficientes de reflexión TE y TM (ecs. 31a y 31b); ajuste de permitividad y conductividad (ecs. 57 a 59) y Tabla 3.

6. iBwave, *A closer look at attenuation across materials & the 2.4 GHz & 5 GHz bands* (2018). Pérdida por material: «Concrete – Heavy» 22,792 / 44,769 dB. https://blog.ibwave.com/a-closer-look-at-attenuation-across-materials-the-2-4ghz-5ghz-bands/
7. Interline, *Building Materials and Wi-Fi Signal Loss (2.4 GHz and 5 GHz)*. «Reinforced concrete»: 25–30 dB o más / 50 dB o más. https://interline.pl/Information-and-Tips/Building-Materials-and-Wi-Fi-Signal-Loss-2.4-GHz-and-5-GHz

8. ITU-R, Recomendación P.526-16 (11/2025), *Propagation by diffraction*. Filo de cuchilla, $J(\nu)$ (§4.1); dos aristas aisladas (§4.3); pantalla de ancho finito (§5.1). https://www.itu.int/rec/R-REC-P.526-16-202511-I/en
9. J. Deygout, "Multiple knife-edge diffraction of microwaves", *IEEE Transactions on Antennas and Propagation*, vol. 14, n.º 4, pp. 480–489, 1966. Arista principal y aristas secundarias.

Los valores de cada material, los adicionales de armadura y sus fuentes están en `config/materials.json`.
