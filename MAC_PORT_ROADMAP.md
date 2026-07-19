# pk3DS para macOS: inventario de paridad

Este documento usa `pk3DS.WinForms` como especificación de comportamiento. Un módulo no se considera portado solo por aparecer en la interfaz: debe leer el mismo formato, aplicar las mismas reglas y generar una salida usable por Luma LayeredFS.

## Estado actual de la web

La web solo cubre una parte del randomizador de datos personales, movimientos por nivel y evoluciones. No equivale todavía a pk3DS para Windows.

## Módulos RomFS

| Módulo de Windows | Juegos | Datos principales | Estado Mac |
| --- | --- | --- | --- |
| Game Text | Gen 6/7 | `gametext` | Portado: editor de tablas y exportación LayeredFS |
| Story Text | Gen 6/7 | `storytext` | Portado: editor de tablas y exportación LayeredFS |
| Personal Stats | Gen 6/7 | `personal` | Parcial: randomizador y cambios masivos; falta editor individual |
| Evolutions | Gen 6/7 | `evolution` | Parcial: randomizador; falta editor individual |
| Level Up Moves | Gen 6/7 | `levelup` | Portado: randomizador y editor individual |
| Wild Encounters | Gen 6/7 | `encdata`, `zonedata`, `worlddata` | Portado: editor individual Gen. VI/VII y exportación LayeredFS de `encdata` |
| Mega Evolutions | Gen 6/7 | `megaevo` | Portado: editor individual y exportación LayeredFS |
| Egg Moves | Gen 6/7 | `eggmove` | Portado: randomizador y editor individual |
| Trainers | Gen 6/7 | `trclass`, `trdata`, `trpoke` | Pendiente |
| Battle Maison / Royal / Tree | Gen 6/7 | `maisontr*`, `maisonpk*` | Pendiente |
| Item Stats | Gen 6/7 | `item` | Pendiente |
| Move Stats | Gen 6/7 | `move` | Parcial: acciones globales; falta editor individual |
| Static Encounters | Gen 7 | `encounterstatic` | Parcial: regalos, encuentros fijos e intercambios; edición de especie, forma, nivel y objeto |
| Pickup | Gen 7 | `pickup` | Pendiente |
| Title Screen | Gen 6 | `titlescreen` | Pendiente |
| OWSE / scripts | Gen 6/7 | mapas, scripts y texto | Pendiente; módulo de desarrollo |

## Módulos ExeFS y CRO

Estos módulos necesitan un workspace extraído completo (RomFS + ExeFS y, cuando corresponda, CRO). No deben prometerse con una entrada que solo contiene RomFS.

| Módulo de Windows | Juegos | Estado Mac |
| --- | --- | --- |
| TMs / HMs | Gen 6/7 | Pendiente |
| Move Tutors | Gen 6/7 | Pendiente |
| Poké Mart | Gen 6/7 | Pendiente |
| Pickup | Gen 6 | Pendiente |
| O-Powers | Gen 6 | Pendiente |
| Shiny Rate | Gen 6 | Pendiente |
| Starter Pokémon | Gen 6 | Pendiente; requiere CRO |
| Type Chart | Gen 6/7 | Pendiente; Gen 6 requiere CRO |
| Gift Pokémon | Gen 6 | Pendiente; requiere CRO |
| Static Encounters | Gen 6 | Pendiente; requiere CRO |
| CRO / CRR patching | Gen 6/7 según módulo | Pendiente |

## Herramientas de proyecto

También forman parte de pk3DS Windows: extracción de CXI/3DS, empaquetado de RomFS/ExeFS, reconstrucción de ROM, creación de parches, edición de imágenes y herramientas GARC/DARC. Se portarán después de que la edición y salida LayeredFS de los módulos sea verificable.

## Orden de implementación

1. Workspace completo, registro de módulos y exportador capaz de incluir RomFS, ExeFS y CRO modificados.
2. Randomizador fiel: personal, level-up, egg moves, evoluciones, encuentros, entrenadores, movimientos, MT y tiendas.
3. Editores RomFS individuales.
4. Editores ExeFS/CRO y herramientas de reconstrucción.
5. Pruebas de regresión comparando los archivos generados por macOS contra Windows con el mismo dump y semilla.
