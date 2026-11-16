# Modelo de propagación

El valor de cada punto se calcula sobre el trayecto directo entre el punto de acceso (AP) y el punto.

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

Por qué: cada muro que cruza el trayecto directo atenúa la señal. El espesor no cambia el valor.

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

Límites: el modelo solo considera el trayecto directo, sin difracción ni rebotes, así que detrás de la columna aparece una sombra en cuña que en la realidad se rellena en parte. La armadura real (barras y estribos separados 10 a 20 cm, frente a una longitud de onda de unos 12 cm en 2,4 GHz y 6 cm en 5 GHz) no es una malla cerrada. La altura no interviene: el plano es 2D.

### Mapa del efecto de la armadura

Además del mapa de cobertura se puede dibujar la diferencia entre dos cálculos de la misma escena: el normal y otro con las columnas como hormigón sin armadura ($\Delta_{armadura} = 0$). Por celda:

$$\Delta L\,[\mathrm{dB}] = L_{materiales}(\text{con armadura}) - L_{materiales}(\text{sin armadura}) = P_{RX}(\text{sin armadura}) - P_{RX}(\text{normal})$$

- **Es una diferencia en dB, no un nivel de señal.** Los colores (de amarillo claro a rojo oscuro, cinco tramos entre 0 y el máximo) no se comparan con la escala de RSSI.
- Una celda vale 0 y queda transparente si el trayecto desde el AP no cruza ninguna columna, o si cruza una pero en una unión con un muro de mayor pérdida (se cuenta una vez la mayor y la armadura no agrega nada).
- Con una columna el valor es su adicional (2,5; 5 o 7,5 dB); un trayecto que cruza dos columnas suma los adicionales de ambas.
- Se calcula con una celda de 0,20 m como máximo, para que se vea la sombra de una columna de 0,30 m. Si la resolución elegida era mayor, solo este mapa usa 0,20 m.
- Los adicionales por nivel de armadura son una convención del proyecto (ver arriba): el mapa muestra el efecto de esa convención, no una medición.

## Balance de enlace

$$P_{RX}\,[\mathrm{dBm}] = P_{TX} - L_{cable,TX} + G_{antena,TX} - L_0 - L_{materiales} + G_{antena,RX} - L_{cable,RX}$$

| Símbolo | Significado | Unidad |
| --- | --- | --- |
| $P_{TX}$ | Potencia del transmisor | dBm |
| $L_{cable,TX}$, $L_{cable,RX}$ | Pérdida del cable en transmisión y en recepción | dB |
| $G_{antena,TX}$, $G_{antena,RX}$ | Ganancia de antena en transmisión y en recepción | dBi |
| $P_{RX}$ | Potencia recibida; $\mathrm{RSSI}$ estimado $= P_{RX}$ | dBm |

Por qué: en escala logarítmica, las ganancias y las pérdidas de la cadena se suman y se restan.

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

## Referencias

1. ITU-R, Recomendación P.525-5 (11/2024), *Calculation of free-space attenuation*. Pérdida básica de transmisión en el espacio libre.
2. H. T. Friis, "A Note on a Simple Transmission Formula", *Proceedings of the IRE*, vol. 34, n.º 5, pp. 254–256, mayo de 1946. Ecuación de transmisión en el espacio libre.
3. W. Stallings, *Data and Computer Communications*, 10.ª ed., Pearson, 2014. Atenuación, decibeles y dBm.
4. T. S. Rappaport, *Wireless Communications: Principles and Practice*, 2.ª ed., Prentice Hall, 2002. Modelo de espacio libre (sec. 4.2) y pérdidas por tabiques en interiores (sec. 4.11.1).
5. ITU-R, Recomendación P.2040-4 (09/2025), *Effects of building materials and structures on radiowave propagation in the range of 1 MHz to 450 GHz*. Efecto de los materiales de construcción.

6. iBwave, *A closer look at attenuation across materials & the 2.4 GHz & 5 GHz bands* (2018). Pérdida por material: «Concrete – Heavy» 22,792 / 44,769 dB. https://blog.ibwave.com/a-closer-look-at-attenuation-across-materials-the-2-4ghz-5ghz-bands/
7. Interline, *Building Materials and Wi-Fi Signal Loss (2.4 GHz and 5 GHz)*. «Reinforced concrete»: 25–30 dB o más / 50 dB o más. https://interline.pl/Information-and-Tips/Building-Materials-and-Wi-Fi-Signal-Loss-2.4-GHz-and-5-GHz

Los valores de cada material, los adicionales de armadura y sus fuentes están en `config/materials.json`.
