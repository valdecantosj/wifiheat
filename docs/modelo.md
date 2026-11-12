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

Por qué: cada muro que cruza el trayecto directo atenúa la señal. Se cuenta una sola vez por muro, y el espesor no cambia el valor.

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

## Referencias

1. ITU-R, Recomendación P.525-5 (11/2024), *Calculation of free-space attenuation*. Pérdida básica de transmisión en el espacio libre.
2. H. T. Friis, "A Note on a Simple Transmission Formula", *Proceedings of the IRE*, vol. 34, n.º 5, pp. 254–256, mayo de 1946. Ecuación de transmisión en el espacio libre.
3. W. Stallings, *Data and Computer Communications*, 10.ª ed., Pearson, 2014. Atenuación, decibeles y dBm.
4. T. S. Rappaport, *Wireless Communications: Principles and Practice*, 2.ª ed., Prentice Hall, 2002. Modelo de espacio libre (sec. 4.2) y pérdidas por tabiques en interiores (sec. 4.11.1).
5. ITU-R, Recomendación P.2040-4 (09/2025), *Effects of building materials and structures on radiowave propagation in the range of 1 MHz to 450 GHz*. Efecto de los materiales de construcción.

Los valores de cada material y sus fuentes están en `config/materials.json`.
