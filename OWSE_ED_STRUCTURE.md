# OWSE Gen. VII: perfiles estructurales de ED

El inventario de OWSE lee el contenedor `ED` descomprimido de `encdata` y publica, por cada subentrada, la cabecera `count`/`kind`, el tamaño, un prefijo hexadecimal y el perfil estructural que se pudo confirmar.

| Bloque y tipo | Perfil | Edición desde la web |
| --- | --- | --- |
| `EP` | stride `0x3C`, posición en `0x08` | Sí |
| `EM` tipo `1` | stride `0x78`, posición en `0x08` | Sí |
| `EM` tipo `3` | contador y `count` registros `0x58`; marcador `3` por registro, XYZ en `0x08 + index × 0x58`, punteros monotónicos a payload variable preservado | Sí, sólo XYZ |
| `EB` tipo `2` | stride `0x3C`, posición en `0x08` | Sí |
| `EB` tipo `11` | longitud exacta `4 + count × 0x44`; marcador `11` al inicio de cada registro y XYZ en `0x08 + index × 0x44` | Sí, sólo XYZ |
| `EG` tipo `8` | tabla de registros `0x4C` después del contador; marcador `8` por registro y XYZ en `0x08 + index × 0x4C`; cola variable opaca | Sí, sólo XYZ; cola intacta |
| `ES` tipo `4` | stride retail `0x38`, posición en `0x08` | Sí |
| `ES` tipo `0` | tabla de descriptores `0x10`; primer `uint32` es un offset absoluto de payload; offsets ordenados y dentro del archivo | No, payload raw; sin semántica de edición |
| `ES` vacío corto | contador cero de cuatro bytes sin marcador, o subentrada de cero bytes | No, paso intacto |
| `ES` otro tipo | No confirmado | No, diagnóstico/raw |
| `EA` tipo `5` | stride `0x3C`, posición en `0x08` | Sí |
| `EA` tipo `6` | descriptores con punteros a payloads de `0x30`, posición en `payload + 0x08` | Sí |
| `ET` tipo `7` | stride `0x54`, posición en `0x08` | Sí |
| `ET` tipo `9` | tabla variable de descriptores; cada tabla de puntos comienza con cabecera de `0x08` y contiene triples XYZ de `0x0C` | Sí |
| `EI` tipo `10` | contador seguido de registros de stride `0x5C`; `kind=10` en el primer registro y posición en `0x08` de cada registro | Sí |
| `PR` tipos `203`/`204` | un registro por entrada, posición XYZ fija en `0x08` y payload variable posterior preservado | Sí |
| `PR` tipo `364` | entrada `0x21C`; dos registros `0x34` marcados `364`/`365`; XYZ en `0x08` y `0x3C`; tres offsets ordenados por registro dentro de la cola raw | Sí, perfil retail estricto; cola intacta |
| `FS` tipo `12` | grupos internos variables dentro de contenedores cuyo tamaño superficial sigue `4 + count × 0x90`; no hay un offset XYZ único | No, diagnóstico/raw |
| `FS` tipo `13` | payload variable entre zonas y cantidades; no confirmado | No, diagnóstico/raw |

La muestra retail de Pokémon Ultra Sun (Europa) confirmó `EM` tipo `3` en 309 subentradas de 269 mundos (678 posiciones): cada registro empieza en `4 + index × 0x58`, repite el marcador `3`, y conserva un puntero de payload dentro del archivo. El exportador cambia únicamente los doce bytes XYZ por registro; valida marcadores, rangos y punteros, y mantiene intactos los campos restantes y payloads.

La misma muestra confirmó `ES` tipo `4` en 311 subentradas (1.427 posiciones). Su longitud es `4 + count × 0x38`: el contador ocupa los primeros cuatro bytes y cada registro repite el marcador `4` en `0x04 + index × 0x38`, con XYZ en `0x08 + index × 0x38`; las 311 entradas pasaron la validación completa de marcador, stride y rango.

El workspace retail de Ultra Sun confirmado por la muestra también contiene `EB` tipo `11` en 12 mundos: 12 subentradas con 14 registros (once entradas de uno y una entrada de tres). Cada entrada mide exactamente `4 + count × 0x44`; cada registro empieza con el marcador `11` y guarda XYZ en `+0x04`. Las 14 coordenadas son finitas y quedan dentro del límite admitido. El editor modifica sólo XYZ y conserva los demás bytes; el round-trip y el comportamiento en juego siguen pendientes.

La muestra también contiene `EG` tipo `8` en 24 subentradas de 22 mundos, con 116 registros. Cada registro empieza en `0x04 + index × 0x4C`, repite el marcador `8` y guarda XYZ en `0x08 + index × 0x4C`. Después de la tabla hay una cola opaca variable observada entre 96 y 1.100 bytes (20 tamaños distintos). El editor exige que toda la tabla quepa en la entrada y que cada marcador y vector XYZ sea válido; modifica únicamente los vectores y conserva la cola completa sin interpretarla. Falta round-trip de exportación y validación en juego.

La muestra también confirmó 149 subentradas `ES` tipo `0` con 400 descriptores de 16 bytes; el primer campo de cada descriptor apunta a un segmento variable ordenado y dentro del archivo. La interfaz muestra cada descriptor, sus tres palabras raw, el offset absoluto, el límite/tamaño del payload y los perfiles estructurales confirmados de tipos `0`–`3`; los tipos `1` y `3` incluyen el conteo y el inicio de sus tríos float. El payload completo permanece intacto y sin edición porque la semántica de juego de esos campos no está confirmada. El resto de las 716 subentradas `ES` de esta muestra son 56 entradas de cuatro bytes con contador cero sin marcador y 200 entradas vacías de cero bytes; no apareció otra variante corta.

La única `PR` tipo `364` de la muestra aparece en el mundo 284: entrada de `0x21C` bytes con contador 2, dos registros `0x34` marcados `364` y `365`, y vectores XYZ en `0x08` y `0x3C`. Cada registro contiene tres offsets ordenados hacia la cola, que comienza en `0x6C`; el orden se reinicia en el segundo registro. El perfil editable comprueba la longitud exacta de la entrada, esos límites y modifica únicamente los dos vectores. La cola completa permanece intacta. La lectura estructural coincide con el patrón retail, pero el cambio aún requiere validación de comportamiento en juego.

Las demás variantes de `EG` y `ES` se conservan intactas y pueden exportarse con **Exportar ED crudo**. No se habilita edición por similitud de tamaño: para pasar una variante a editable hay que confirmar su cabecera, stride, offset y cola contra datos reales.

La referencia de los campos confirmados vive en `OverworldEditor.cs`. Al exportar posiciones Gen. VII, el empaquetador conserva también los bytes reservados entre la tabla y el primer payload, y los bytes posteriores al último offset de cada Mini ED modificado. EM tipo 3, EB tipo 11, EG tipo 8 y PR tipo 364 se identificaron en muestras retail; todavía falta un round-trip de exportación dedicado para esos perfiles y validar los cambios en juego.
