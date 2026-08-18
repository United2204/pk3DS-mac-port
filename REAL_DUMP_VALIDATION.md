# Validación contra dumps reales

Última validación: 18 de agosto de 2026.

## Condiciones de entrada

Cada juego debe estar extraído en una carpeta propia, con esta estructura mínima:

```text
X-extracted/
├── RomFS/
├── ExeFS/
│   └── .code.bin
└── exheader.bin
```

El workspace debe provenir de un dump completo y desencriptado. La aplicación detecta automáticamente el juego mediante `exheader.bin`; no se debe cambiar manualmente el `titleId`.

La herramienta de extracción también acepta un CIA desencriptado y completo. En ese caso toma el primer contenido NCCH y genera el mismo workspace; un CIA retail/encriptado no es una entrada válida para este flujo.

Para Gen. VI, `.code.bin` puede conservarse comprimido con BLZ dentro del workspace. Los módulos que lo necesitan lo normalizan en memoria y generan una copia descomprimida y alineada a `0x200` en la salida. El origen no se modifica.

## Procedimiento manual

1. Ejecutar la aplicación y abrir **Workspace**.
2. Seleccionar `~/Proyectos/3DS-dumps/X-extracted` o `~/Proyectos/3DS-dumps/OR-extracted`.
3. Confirmar que la interfaz muestra `XY` u `ORAS` y que el workspace es válido.
4. Entrar a cada editor habilitado, abrir su tabla y comprobar que aparecen datos reales. En OWSE, cargar X/ORAS, abrir una zona con entidades y confirmar muebles, NPC, warps y triggers; verificar la grilla `mapGR` y la matriz `MM`, abrir también un script Gen. VI y comprobar sus instrucciones hexadecimales. En UM, abrir un grupo `ZS` o `ZI`, comprobar sus instrucciones, el mapa padre y las posiciones `EP`, `EM` principal, `EB` tipo 2, `ES` tipo 4, `EA` tipos 5/6 y `ET` tipos 7/9 de la zona, exportar una modificación conservando la cantidad de valores y probar **Exportar ED crudo** para generar el contenedor, sus bloques, subentradas y `manifest.json`.
5. Elegir una carpeta de salida nueva y exportar. No usar como salida la carpeta del dump.
6. Aplicar el ZIP generado como LayeredFS y conservar el workspace original como respaldo.

## Resultado esperado

Una exportación correcta genera un ZIP `*-LayeredFS.zip` y enumera los archivos modificados. Según el editor, los cambios esperados son:

| Editor | Archivos de salida esperados |
| --- | --- |
| Type Chart | `romfs/DllBattle.cro` |
| Starters | `romfs/DllPoke3Select.cro`, `romfs/DllField.cro` |
| Poké Mart Gen. VI | `exefs/.code.bin` |
| TMs/HMs | `exefs/.code.bin` |
| Static Encounters Gen. VI | `romfs/DllField.cro` |
| Gift Pokémon Gen. VI | `romfs/DllField.cro` |
| OWSE Gen. VI | GARC `encdata` (`ZO`) |
| OWSE mapa Gen. VI | GARCs `mapGR` y `mapMatrix` |
| OWSE script Gen. VI | GARC `encdata` (`ZO`) |
| OWSE script Gen. VII | GARC `encdata` (`ZS`/`ZI`) |
| OWSE metadatos de zona Gen. VII | GARC `zonedata` (mapa padre) |
| OWSE entidades Gen. VII | GARC `encdata` (`ED`, posiciones `EP`/`EM`/`EI`/`PR`/`EB`/`ES`/`EA`/`ET`) |
| OWSE ED crudo Gen. VII | Carpeta de diagnóstico con `ed.bin`, bloques/subentradas y `manifest.json`; no es un parche |
| Static Encounters Gen. VII | GARC `encounterstatic` |

## Prueba realizada

Se consultaron los módulos de lectura de ambos workspaces reales y todos respondieron correctamente:

- `X-extracted`: detectado como `XY`, Title ID `0004000000055D00`.
- `OR-extracted`: detectado como `ORAS`, Title ID `000400000011C400`.
- `UM-extracted`: detectado como `USUM`; OWSE Gen. VII leyó grupos `ZS`/`ZI` y su editor de instrucciones quedó habilitado para exportar cambios con cantidad fija.
- Randomizador de equipos contra dumps reales: X y UM ejecutaron correctamente la capa de clases/composición, generando ZIPs LayeredFS nuevos con `trdata` + `trpoke` (`a/0/3/8` + `a/0/4/0` en X; `a/1/0/6` + `a/1/0/7` en UM). Las salidas de smoke test quedaron en `/private/tmp/pk3ds-real-smoke-x-0818/` y `/private/tmp/pk3ds-real-smoke-um-0818/`; no se escribió dentro de los workspaces originales.
- Randomizador de equipos contra dumps reales (IVs/evolución final): X y UM volvieron a generar correctamente una salida usando IVs máximos y evolución final desde nivel 1. Las salidas quedaron en `/private/tmp/pk3ds-real-smoke-x-0818-next/` y `/private/tmp/pk3ds-real-smoke-um-0818-next/`, con los dos GARCs de entrenadores modificados en cada juego; no se escribió dentro de los workspaces originales.
- Premios de entrenadores Gen. VI contra dump real: X generó correctamente un ZIP con `trdata` + `trpoke` usando una probabilidad de premio del 100%; la salida quedó en `/private/tmp/pk3ds-real-smoke-x-0818-prizes/` y el origen no se modificó.
- Equipos importantes Gen. VII contra dump real: UM generó correctamente un ZIP con `trdata` + `trpoke` activando el relleno de equipos importantes hasta seis Pokémon; la salida quedó en `/private/tmp/pk3ds-real-smoke-um-0818-important/` y el origen no se modificó.
- Ataques potentes contra dump real: UM generó correctamente un ZIP con `trdata` + `trpoke` usando la selección de movimientos de mayor potencia desde nivel 1; la salida quedó en `/private/tmp/pk3ds-real-smoke-um-0818-high-power/` y el origen no se modificó.
- Naturalezas y shiny contra dump real: UM generó correctamente un ZIP con `trdata` + `trpoke` usando naturaleza aleatoria y 100% de probabilidad shiny; la salida quedó en `/private/tmp/pk3ds-real-smoke-um-0818-nature-shiny/` y el origen no se modificó.
- Temas por tipo contra dumps reales: X y UM generaron correctamente un ZIP con `trdata` + `trpoke` usando especies compatibles con el tipo elegido por grupo en Gen. VI y por equipo en Gen. VII. Las salidas quedaron en `/private/tmp/pk3ds-real-smoke-x-0818-type-themes/` y `/private/tmp/pk3ds-real-smoke-um-0818-type-themes/`; los workspaces originales no se modificaron.
- Formas Mega opcionales contra dump real: X aceptó el nuevo campo de randomización y generó correctamente un ZIP con `trdata` + `trpoke` permitiendo formas Mega al asignar formas válidas. La salida quedó en `/private/tmp/pk3ds-real-smoke-x-0818-mega-forms/`; el workspace original no se modificó.
- Temas de gimnasios contra dump real: X aceptó el campo adicional de temas compartidos para entrenadores comunes de gimnasio y generó correctamente un ZIP con `trdata` + `trpoke`; la salida quedó en `/private/tmp/pk3ds-real-smoke-x-0818-gym-themes/` y el workspace original no se modificó.
- OWSE real: X detectó 718 grupos `ZO`, ORAS 1068 grupos y UM 672 grupos `ZS`/`ZI`.
- OWSE entidades Gen. VII: UM abrió la zona 000, expuso los 17 subbloques del contenedor `ED` (`EP`, `EM`, `EB`, `ES`, `EA`, `ET`, `EG`, `EI`, `FS` y `PR`) con sus tamaños y cantidades de entradas, además de las cabeceras estructurales de cada subentrada. El inventario real mostró EM tipo 3 (6+2+1 registros), EA tipo 6 (3+9+1), EI tipo 10 (4+6+3) y FS tipo 12 (4), mientras las variantes vacías también quedaron visibles sin habilitar edición. Leyó 17 posiciones `EP`, 20 posiciones del bloque `EM` principal, 24 posiciones `EB` tipo 2, 37 posiciones `ES` tipo 4, 13 posiciones `EA` tipo 5 y 11 posiciones `ET` tipo 7. La variante `EA` tipo 6 quedó confirmada como una tabla de descriptores con punteros a payloads de `0x30` bytes y posición en `payload + 0x08`; la exportación real del mundo 002 leyó 10 posiciones EA (8 tipo 5 y 2 tipo 6), modificó una coordenada tipo 6 y generó un parche LayeredFS, modificando solo `a/0/8/2`; el hash del origen permaneció `863dc9d22132369e1f09c2bdd76996d625d11b38cf6c9a894cacd4c30af786d7`. La variante `ET` tipo 9 quedó confirmada en lectura contra mundos reales; su tabla variable contiene descriptores absolutos y tablas de puntos XYZ. El esquema EM tipo 3 permanece en diagnóstico.
- Perfil estructural ED real: UM mundo 000 mostró `EP` stride `0x3C`/offset `0x08`, `EM` tipo 1 stride `0x78`/offset `0x08`, `EB` tipo 2 stride `0x3C`/offset `0x08`, `ES` tipo 4 stride retail `0x38`/offset `0x08`, `EA` tipo 5 stride `0x3C`/offset `0x08`, `EA` tipo 6 con descriptores y payloads de `0x30`/offset `0x08`, `ET` tipo 7 stride `0x54`/offset `0x08`, `ET` tipo 9 con tablas variables de puntos XYZ, `EI` tipo 10 con stride `0x5C`/offset `0x08` y `PR` tipos 203/204 con un vector XYZ único en `0x08`; `EM` tipo 3, `PR` tipo 364, `FS` tipo 12 y `ES` corta quedaron identificados como diagnóstico sin edición. La consulta fue sólo lectura y no modificó el dump.
- Round-trip real de `EI` tipo 10: UM mundo 002 expuso 5 posiciones (4 en una entrada de 372 bytes y 1 en una de 96 bytes). Se incrementó la coordenada X de la primera, la lectura del ZIP confirmó el nuevo valor, se modificó únicamente `a/0/8/2` y el hash SHA-256 del origen permaneció `863dc9d22132369e1f09c2bdd76996d625d11b38cf6c9a894cacd4c30af786d7`. La salida quedó en `/private/tmp/pk3ds-owse-real-ei-validation/pk3ds-mac-owse-gen7-entities-20260818-083921-LayeredFS.zip`.
- Auditoría de `ET` tipo 9 en UM: la lectura real terminó sin diagnósticos en mundos 002, 003, 030, 048, 056, 057, 066, 109, 128, 261 y 277, con 20, 44, 28, 24, 14, 3, 19, 12, 41, 31 y 35 puntos respectivamente. Además, el mundo 002 se exportó modificando una coordenada de una trayectoria; el ZIP `/private/tmp/pk3ds-owse-real-et9-validation/pk3ds-mac-owse-gen7-entities-20260818-075159-LayeredFS.zip` modificó únicamente `a/0/8/2`, la lectura de vuelta confirmó el nuevo valor y el SHA-256 del origen permaneció `863dc9d22132369e1f09c2bdd76996d625d11b38cf6c9a894cacd4c30af786d7`.
- Exportación ED cruda real: UM mundo 000 produjo sin diagnósticos `ed.bin` de 36.356 bytes, 17 bloques y 49 subentradas (67 archivos de diagnóstico contando el contenedor), con `manifest.json`; el origen no se modifica y la salida quedó fuera del workspace en `/private/tmp/pk3ds-owse-real-validation/`.
- Auditoría EM tipo 3 real: el inventario completo de UM encontró 309 entradas, con cantidades de registros entre 1 y 14, 13 cantidades distintas y 32 tamaños distintos entre 120 y 1.676 bytes. Aunque varias entradas comienzan con coordenadas plausibles en `0x08`, sus tablas internas y referencias cambian de longitud; no se confirmó un stride ni un límite de registros que permita editar sólo XYZ sin tocar datos anidados. Por eso siguen disponibles en el inventario y en **Exportar ED crudo**, pero no se exponen como posiciones editables.
- Auditoría de variantes ED restantes: UM contiene 21 entradas `FS` tipo 12; aunque su tamaño superficial coincide con `4 + count × 0x90` para cantidades de 1 a 6, cada grupo contiene subregistros internos variables y los triples XYZ no ocupan un offset único. Las 23 entradas `FS` tipo 13 también varían entre 228 y 880 bytes, y las 149 entradas `ES` tipo 0 varían entre 8 y 17.116 bytes. Se mantienen en diagnóstico/raw para evitar editar bytes anidados por una coincidencia superficial de tamaño.
- Round-trip real de `PR`: UM contiene 7 entradas tipo 203 y 3 tipo 204, todas con un único vector XYZ en `0x08`; se modificó una coordenada en los mundos 026 (tipo 203) y 079 (tipo 204), la lectura de vuelta confirmó ambos valores, el payload posterior permaneció byte a byte igual, se modificó únicamente `a/0/8/2` y el hash SHA-256 del origen permaneció `863dc9d22132369e1f09c2bdd76996d625d11b38cf6c9a894cacd4c30af786d7`. Las variantes `PR` tipo 364 siguen en diagnóstico porque contienen dos registros con offsets internos variables. Las salidas quedaron en `/private/tmp/pk3ds-owse-real-pr-validation/`.
- GARC Shuffler real: X `RomFS/a/0/0/0` generó `/private/tmp/pk3ds-garc-real-shuffle.garc` con semilla `23`; detectó 6 entradas, cambió 4 referencias FATB y conservó el FIMB completo byte a byte. El origen mantuvo SHA-256 `2bec71b25beb14c75378d226ee0cc6575d44edb53a405c59bc07d07b65da21b4` y la copia quedó en `6198bed6b0d3c7bd4355147208c903c33a8ba70b85c8692be1214fae0257c5b8`.
- Desempaquetado automático real: X `RomFS/a/0/0/0` fue detectado como `GARC` sin usar su extensión, generó `/private/tmp/pk3ds-auto-real-validation-20260818` con 6 archivos y 49.716 bytes usando `skipDecompression=true`; el origen conservó SHA-256 `2bec71b25beb14c75378d226ee0cc6575d44edb53a405c59bc07d07b65da21b4`.
- Empaquetado automático real: las seis entradas anteriores se copiaron a la carpeta con sufijo `_g` `/private/tmp/pk3ds-auto-real-validation-20260818_g`; se detectó GARC, se generó `/private/tmp/pk3ds-auto-real-validation-20260818-repacked.garc` con 6 archivos y 49.920 bytes, y al desempaquetarlo de nuevo a `/private/tmp/pk3ds-auto-real-validation-20260818-roundtrip` las seis entradas coincidieron byte a byte con la extracción original.
- CRO/CRR real: X revisó 133 CRO de la raíz y `RomFS/.crr/static.crr`; los 133 hashes internos y las 133 entradas del CRR ya estaban correctos (`rehashedCros=0`, `crrChanged=false`). Generó correctamente el ZIP vacío `/private/tmp/pk3ds-crr-real-validation/pk3ds-mac-crr-20260818-035334-LayeredFS.zip` con el árbol LayeredFS, sin modificar el origen.
- OWSE script Gen. VII: UM abrió el grupo `ZS` del mundo 000 con 2443 instrucciones, exportó `/private/tmp/pk3ds-owse-real-gen7-script-validation-20260817/pk3DS-mac-owse-gen7-script-20260817-185750-LayeredFS.zip` y modificó solo `a/0/8/2`; el hash del origen permaneció `863dc9d22132369e1f09c2bdd76996d625d11b38cf6c9a894cacd4c30af786d7`, mientras el archivo del ZIP quedó con hash `c0076e583d094512c3c354b814ca53e284bd049cb45479e6bc34f359eb995c33`.
- OWSE metadatos Gen. VII: UM exportó el `ParentMap` de la zona 000 a `/private/tmp/pk3ds-owse-real-gen7-zone-validation-20260817/pk3ds-mac-owse-gen7-zone-20260817-190332-LayeredFS.zip`, modificando solo `a/0/7/7`; el hash del origen permaneció `66f9e6642adb34cf5f8ce512d035f843c899e843759fe470515a99ddfb31c279`, mientras el archivo del ZIP quedó con hash `eee17987ec78c7271635ce48a35c29fecaabc4935f50457d31baafa67a9872bd`.
- Static Encounters Gen. VII: UM abrió el encuentro fijo 000, expuso los campos avanzados y exportó cambios de shiny/mapa/aliados a `/private/tmp/pk3ds-static-real-validation-20260817/pk3ds-mac-static-20260817-191359-LayeredFS.zip`, modificando solo `a/1/5/9`; el hash del origen permaneció `1e9dbda36e2fee0fcc7cddacb6fa3f0ab69d713513915c81b00d503a06ef501a`, mientras el archivo del ZIP quedó con hash `c40c04acb77ca22b4845c8221da5b76f1f00f7341705908f198fcafe76ba36f4`.
- OWSE Gen. VI: la zona 001 de X se leyó con 5 muebles, 7 NPC, 1 warp y 4 triggers; se exportaron entidades y metadatos (matriz/clima) a parches reales en `/private/tmp` y el hash del `encdata` original permaneció igual.
- OWSE scripts Gen. VI: el script 001 de X se leyó con 76 instrucciones, se modificó una instrucción y se generó `/private/tmp/pk3ds-owse-real-script-validation/pk3DS-mac-owse-gen6-script-20260817-181859-LayeredFS.zip`; el archivo fuente `a/0/1/2` conservó el hash SHA-256 `dc0b7a0dd2764480eaebd4c1c057d1fa8e268ccdf8953945713bb091925190d2`.
- OWSE mapas Gen. VI: X y ORAS expusieron grillas `mapGR` de 40×40; X se exportó modificando una celda a `/private/tmp/pk3ds-owse-real-map-validation-20260817/pk3ds-mac-owse-gen6-map-20260817-183947-LayeredFS.zip`, con solo `a/0/4/1` modificado y hash fuente `ab5acd0599c1d31ef03f1fcdf2149127812272cc2c8f53497948ffa1fe513e8b` sin cambios. La matriz `MM` real de la zona 000 también quedó validada en ambos juegos: X leyó `mapMatrix` 009 y ORAS `mapMatrix` 000, ambas de 1×1; se modificó una entrada en cada una, se generaron `/private/tmp/pk3ds-real-map-roundtrip-x/pk3ds-mac-owse-gen6-map-20260818-081016-LayeredFS.zip` y `/private/tmp/pk3ds-real-map-roundtrip-oras/pk3ds-mac-owse-gen6-map-20260818-081017-LayeredFS.zip`, cada uno con un único archivo cambiado (`a/0/4/2` y `a/0/4/0`), el valor exportado volvió a leerse correctamente y el hash del origen permaneció igual.
- 21 catálogos/tablas por juego: lectura correcta.
- 6 exportaciones por juego: ZIP LayeredFS generado correctamente.
- Los hashes SHA-256 de `.code.bin`, `DllBattle.cro` y `DllPoke3Select.cro` del origen permanecieron iguales antes y después.
- Reconstrucción `.3ds` desde ambos workspaces: correcta; la cabecera `NCSD` apareció en `0x100`.
- Tamaños reconstruidos: X `1.801.068.544` bytes; ORAS `1.890.316.288` bytes.
- Conversión CIA desde ambos workspaces mediante el `makerom` arm64 incluido: HTTP 200 y archivos generados correctamente.
- Tamaños CIA generados: X `1.801.081.792` bytes; ORAS `1.890.329.536` bytes.
- Los CIA nuevos se generan sin cifrado de contenido (`EnableCrypt: false`) y la ROM intermedia marca el NCCH con `NoCrypto`; `makerom -ciatocci` volvió a leer correctamente el CIA de X.

También se comprobó específicamente el caso que motivó esta validación: los `.code.bin` reales estaban comprimidos y no tenían tamaño múltiplo de `0x200`; Poké Mart y TMs/HMs exportaron correctamente su copia normalizada sin exigir una conversión manual.

## Lo que todavía no está validado

Esta prueba confirma lectura y exportación round-trip, pero no el arranque en una consola. Falta validar:

- instalación del ZIP en Luma LayeredFS;
- arranque real de X y ORAS con cada parche;
- instalación y arranque del `.cia` en una consola compatible;
- módulos que requieren parche RO/RSA para CRO modificados.

> La instalación y apertura del CIA generado se validaron manualmente en Azahar. Esa prueba confirma el formato para el emulador, pero no reemplaza la validación en hardware ni mide el rendimiento de emulación.

Ante un fallo de consola, conservar el ZIP, el workspace original y el `titleId` usado para poder reproducirlo.

## Conversión CIA con makerom incluido

La primera ejecución contra el workspace real de X confirmó que el binario incluido se detecta y se ejecuta, pero rechazó el `.3ds` reconstruido por las firmas retail del contenido (`AccessDesc Sigcheck Failed`). La conversión ahora usa `-ignoresign`, un RSF temporal con `EnableCrypt: false` y un NCCH marcado `NoCrypto`, porque el workspace contiene ExeFS/RomFS planos después de la extracción. La ejecución completa desde el endpoint terminó correctamente para X y ORAS, el CIA de X pasó una lectura de vuelta con `makerom` y su instalación/apertura se validó manualmente en Azahar.

Los CIA generados antes de esta corrección pueden mostrar en Azahar `Blocked unauthorized encrypted CIA installation`; deben descartarse para esta prueba y regenerarse desde la aplicación actualizada.
