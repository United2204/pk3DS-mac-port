# Validación contra dumps reales

Última validación: 17 de agosto de 2026.

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

Para Gen. VI, `.code.bin` puede conservarse comprimido con BLZ dentro del workspace. Los módulos que lo necesitan lo normalizan en memoria y generan una copia descomprimida y alineada a `0x200` en la salida. El origen no se modifica.

## Procedimiento manual

1. Ejecutar la aplicación y abrir **Workspace**.
2. Seleccionar `~/Proyectos/3DS-dumps/X-extracted` o `~/Proyectos/3DS-dumps/OR-extracted`.
3. Confirmar que la interfaz muestra `XY` u `ORAS` y que el workspace es válido.
4. Entrar a cada editor habilitado, abrir su tabla y comprobar que aparecen datos reales. En OWSE, cargar X/ORAS, abrir una zona con entidades y confirmar muebles, NPC, warps y triggers; verificar la grilla `mapGR`, abrir también un script Gen. VI y comprobar sus instrucciones hexadecimales. En UM, abrir un grupo `ZS` o `ZI`, comprobar sus instrucciones, el mapa padre y las posiciones `EP`, `EM` principal, `EB` tipo 2, `ES` tipo 4, `EA` tipo 5 y `ET` tipo 7 de la zona, y exportar una modificación conservando la cantidad de valores.
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
| OWSE propiedades de mapa Gen. VI | GARC `mapGR` |
| OWSE script Gen. VI | GARC `encdata` (`ZO`) |
| OWSE script Gen. VII | GARC `encdata` (`ZS`/`ZI`) |
| OWSE metadatos de zona Gen. VII | GARC `zonedata` (mapa padre) |
| OWSE entidades Gen. VII | GARC `encdata` (`ED`, posiciones `EP`/`EM`/`EB`/`ES`/`EA`/`ET`) |
| Static Encounters Gen. VII | GARC `encounterstatic` |

## Prueba realizada

Se consultaron los módulos de lectura de ambos workspaces reales y todos respondieron correctamente:

- `X-extracted`: detectado como `XY`, Title ID `0004000000055D00`.
- `OR-extracted`: detectado como `ORAS`, Title ID `000400000011C400`.
- `UM-extracted`: detectado como `USUM`; OWSE Gen. VII leyó grupos `ZS`/`ZI` y su editor de instrucciones quedó habilitado para exportar cambios con cantidad fija.
- OWSE real: X detectó 718 grupos `ZO`, ORAS 1068 grupos y UM 672 grupos `ZS`/`ZI`.
- OWSE entidades Gen. VII: UM abrió la zona 000, expuso los 17 subbloques del contenedor `ED` (`EP`, `EM`, `EB`, `ES`, `EA`, `ET`, `EG`, `EI`, `FS` y `PR`) con sus tamaños y cantidades de entradas, además de las cabeceras estructurales de cada subentrada. El inventario real mostró EM tipo 3 (6+2+1 registros), EA tipo 6 (3+9+1), EI tipo 10 (4+6+3) y FS tipo 12 (4), mientras las variantes vacías también quedaron visibles sin habilitar edición. Leyó 17 posiciones `EP`, 20 posiciones del bloque `EM` principal, 24 posiciones `EB` tipo 2, 37 posiciones `ES` tipo 4, 13 posiciones `EA` tipo 5 y 11 posiciones `ET` tipo 7. Las variantes `EA` tipo 6 y `ET` tipo 9 permanecen en diagnóstico. Se modificó una coordenada EB, otra ES y otra EA en exportaciones separadas; sus hashes y rutas están registrados abajo. La validación ET modificó solo Z del primer registro y quedó en `/private/tmp/pk3ds-owse-real-gen7-et-validation-20260817/pk3ds-mac-owse-gen7-entities-20260817-211307-LayeredFS.zip` con hash ZIP `65d34eb90c9d2c11ef7169f783b82770689fe7632528cb22d094a55a4ad7ef62` y archivo parcheado `a508a1a002ba6b11aa5e31941a6d998f9999c0b182be25ad5f4f0486932e8669`; en todas las validaciones el hash del origen permaneció `863dc9d22132369e1f09c2bdd76996d625d11b38cf6c9a894cacd4c30af786d7`.
- OWSE script Gen. VII: UM abrió el grupo `ZS` del mundo 000 con 2443 instrucciones, exportó `/private/tmp/pk3ds-owse-real-gen7-script-validation-20260817/pk3DS-mac-owse-gen7-script-20260817-185750-LayeredFS.zip` y modificó solo `a/0/8/2`; el hash del origen permaneció `863dc9d22132369e1f09c2bdd76996d625d11b38cf6c9a894cacd4c30af786d7`, mientras el archivo del ZIP quedó con hash `c0076e583d094512c3c354b814ca53e284bd049cb45479e6bc34f359eb995c33`.
- OWSE metadatos Gen. VII: UM exportó el `ParentMap` de la zona 000 a `/private/tmp/pk3ds-owse-real-gen7-zone-validation-20260817/pk3ds-mac-owse-gen7-zone-20260817-190332-LayeredFS.zip`, modificando solo `a/0/7/7`; el hash del origen permaneció `66f9e6642adb34cf5f8ce512d035f843c899e843759fe470515a99ddfb31c279`, mientras el archivo del ZIP quedó con hash `eee17987ec78c7271635ce48a35c29fecaabc4935f50457d31baafa67a9872bd`.
- Static Encounters Gen. VII: UM abrió el encuentro fijo 000, expuso los campos avanzados y exportó cambios de shiny/mapa/aliados a `/private/tmp/pk3ds-static-real-validation-20260817/pk3ds-mac-static-20260817-191359-LayeredFS.zip`, modificando solo `a/1/5/9`; el hash del origen permaneció `1e9dbda36e2fee0fcc7cddacb6fa3f0ab69d713513915c81b00d503a06ef501a`, mientras el archivo del ZIP quedó con hash `c40c04acb77ca22b4845c8221da5b76f1f00f7341705908f198fcafe76ba36f4`.
- OWSE Gen. VI: la zona 001 de X se leyó con 5 muebles, 7 NPC, 1 warp y 4 triggers; se exportaron entidades y metadatos (matriz/clima) a parches reales en `/private/tmp` y el hash del `encdata` original permaneció igual.
- OWSE scripts Gen. VI: el script 001 de X se leyó con 76 instrucciones, se modificó una instrucción y se generó `/private/tmp/pk3ds-owse-real-script-validation/pk3DS-mac-owse-gen6-script-20260817-181859-LayeredFS.zip`; el archivo fuente `a/0/1/2` conservó el hash SHA-256 `dc0b7a0dd2764480eaebd4c1c057d1fa8e268ccdf8953945713bb091925190d2`.
- OWSE mapas Gen. VI: X y ORAS expusieron grillas `mapGR` de 40×40; X se exportó modificando una celda a `/private/tmp/pk3ds-owse-real-map-validation-20260817/pk3ds-mac-owse-gen6-map-20260817-183947-LayeredFS.zip`, con solo `a/0/4/1` modificado y hash fuente `ab5acd0599c1d31ef03f1fcdf2149127812272cc2c8f53497948ffa1fe513e8b` sin cambios.
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
- instalación del `.cia` resultante en un emulador o consola compatible;
- módulos que requieren parche RO/RSA para CRO modificados.

Ante un fallo de consola, conservar el ZIP, el workspace original y el `titleId` usado para poder reproducirlo.

## Conversión CIA con makerom incluido

La primera ejecución contra el workspace real de X confirmó que el binario incluido se detecta y se ejecuta, pero rechazó el `.3ds` reconstruido por las firmas retail del contenido (`AccessDesc Sigcheck Failed`). La conversión ahora usa `-ignoresign`, un RSF temporal con `EnableCrypt: false` y un NCCH marcado `NoCrypto`, porque el workspace contiene ExeFS/RomFS planos después de la extracción. La ejecución completa desde el endpoint terminó correctamente para X y ORAS, y el CIA de X pasó una lectura de vuelta con `makerom`; todavía falta validar la instalación y el arranque en Azahar o en una consola compatible.

Los CIA generados antes de esta corrección pueden mostrar en Azahar `Blocked unauthorized encrypted CIA installation`; deben descartarse para esta prueba y regenerarse desde la aplicación actualizada.
