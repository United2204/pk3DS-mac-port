# pk3DS para macOS: inventario de paridad

Este documento usa `pk3DS.WinForms` como especificación de comportamiento. Un módulo no se considera portado solo por aparecer en la interfaz: debe leer el mismo formato, aplicar las mismas reglas y generar una salida usable por Luma LayeredFS.

## Estado actual de la web

La web solo cubre una parte del randomizador de datos personales, movimientos por nivel y evoluciones. No equivale todavía a pk3DS para Windows.

## Work Breakdown Structure (WBS)

Desglose jerárquico del port completo. El detalle de cada módulo (formatos, exportación LayeredFS) está en las tablas de las secciones siguientes; esta sección es la vista de conjunto para seguimiento de avance.

- **1. Fundación de la app web** — Hecho
  - 1.1. Servidor local ASP.NET (`pk3DS.Mac.Web`) — Hecho
  - 1.2. Detección automática de juego y Title ID desde `exheader.bin` — Hecho
  - 1.3. Interfaz multi-página (randomizador + editores dedicados por módulo) — Hecho
  - 1.4. Exportación a ZIP con árbol LayeredFS (`luma/titles/<title-id>/romfs`) — Hecho

- **2. Randomizador de datos RomFS** — Parcial
  - 2.1. Personal Stats (habilidades, objetos, catch rate, tipos, egg groups, stats base, MT/MO, tutores) — Hecho
  - 2.2. Level Up Moves (cantidad, distribución, STAB, potencia, cuatro movimientos iniciales, exclusión de daño fijo) — Hecho
  - 2.3. Egg Moves — Hecho
  - 2.4. Evolutions (randomizar resultados, eliminar intercambios, evolución por nivel) — Hecho
  - 2.5. Move Stats — acciones globales (tipos, categorías, modo Metronome) — Hecho

- **3. Editores individuales RomFS** — Parcial
  - 3.1. Game Text / Story Text (tabla, línea, búsqueda, exportación LayeredFS) — Hecho
  - 3.2. Mega Evolutions — Hecho
  - 3.3. Wild Encounters Gen VI/VII (`encdata`) — Hecho
  - 3.4. Trainers Gen VII: datos y equipo (`trdata`, `trpoke`) — Parcial (falta Gen VI y edición de nombres/clases)
  - 3.5. Static Encounters Gen VII (regalos, fijos, intercambios) — Parcial (faltan campos avanzados del formato)
  - 3.6. Personal Stats — editor individual por especie — Pendiente
  - 3.7. Evolutions — editor individual por especie — Pendiente
  - 3.8. Move Stats — editor individual por movimiento — Pendiente
  - 3.9. Item Stats — Pendiente
  - 3.10. Battle Maison / Royal / Tree — Pendiente
  - 3.11. Pickup Gen VII — Pendiente
  - 3.12. Title Screen Gen VI — Pendiente
  - 3.13. OWSE / scripts (mapas, scripts, texto) — Pendiente

- **4. Editores ExeFS / CRO** — Pendiente
  - 4.1. TMs / HMs — Pendiente
  - 4.2. Move Tutors — Pendiente
  - 4.3. Poké Mart — Pendiente
  - 4.4. Pickup Gen VI — Pendiente
  - 4.5. O-Powers Gen VI — Pendiente
  - 4.6. Shiny Rate Gen VI — Pendiente
  - 4.7. Starter Pokémon Gen VI (requiere CRO) — Pendiente
  - 4.8. Type Chart (Gen VI requiere CRO) — Pendiente
  - 4.9. Gift Pokémon Gen VI (requiere CRO) — Pendiente
  - 4.10. Static Encounters Gen VI (`DllField.cro`) — Parcial (edición individual lista; falta parche RO de Luma para usarlo)
  - 4.11. CRO / CRR patching — Pendiente

- **5. Herramientas de proyecto** — Pendiente
  - 5.1. Extracción de CXI/3DS — Pendiente
  - 5.2. Empaquetado de RomFS/ExeFS — Pendiente
  - 5.3. Reconstrucción de ROM — Pendiente
  - 5.4. Creación de parches — Pendiente
  - 5.5. Edición de imágenes — Pendiente
  - 5.6. Herramientas GARC/DARC — Pendiente

- **6. Verificación y QA** — Pendiente
  - 6.1. Pruebas de regresión: comparar archivos generados en macOS contra Windows con el mismo dump y semilla — Pendiente

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
| Trainers | Gen 6/7 | `trclass`, `trdata`, `trpoke` | Parcial: editor individual Gen. VII de datos y equipo, con exportación LayeredFS de `trdata` y `trpoke`; falta Gen. VI y edición de nombres/clases |
| Battle Maison / Royal / Tree | Gen 6/7 | `maisontr*`, `maisonpk*` | Pendiente |
| Item Stats | Gen 6/7 | `item` | Pendiente |
| Move Stats | Gen 6/7 | `move` | Parcial: acciones globales; falta editor individual |
| Static Encounters | Gen 7 | `encounterstatic` | Parcial: regalos, encuentros fijos e intercambios; edición de especie, forma, nivel, objeto y campos avanzados disponibles en el formato |
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
| Static Encounters | Gen 6 | Parcial: edición individual en `DllField.cro`; requiere parche RO de Luma para usar CRO modificado |
| CRO / CRR patching | Gen 6/7 según módulo | Pendiente |

## Herramientas de proyecto

También forman parte de pk3DS Windows: extracción de CXI/3DS, empaquetado de RomFS/ExeFS, reconstrucción de ROM, creación de parches, edición de imágenes y herramientas GARC/DARC. Se portarán después de que la edición y salida LayeredFS de los módulos sea verificable.
