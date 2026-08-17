# pk3DS para macOS: inventario de paridad

Este documento usa `pk3DS.WinForms` como especificación de comportamiento. Un módulo no se considera portado solo por aparecer en la interfaz: debe leer el mismo formato, aplicar las mismas reglas y generar una salida usable por Luma LayeredFS.

## Estado actual de la web

La web cubre el randomizador de RomFS completo, la exportación ExeFS de TMs/HMs, más de quince editores individuales y el empaquetado standalone de RomFS/ExeFS. No equivale todavía a pk3DS para Windows: faltan otros módulos ExeFS/CRO y varias herramientas de proyecto.

> Mantené este documento sincronizado con el código **en el mismo commit** que agrega o completa un módulo. Ya se desincronizó una vez: cuatro editores figuraban como pendientes estando implementados.

## Work Breakdown Structure (WBS)

Desglose jerárquico del port completo. El detalle de cada módulo (formatos, exportación LayeredFS) está en las tablas de las secciones siguientes; esta sección es la vista de conjunto para seguimiento de avance.

- **1. Fundación de la app web** — Hecho
  - 1.1. Servidor local ASP.NET (`pk3DS.Mac.Web`) — Hecho
  - 1.2. Detección automática de juego y Title ID desde `exheader.bin` — Hecho
  - 1.3. Interfaz multi-página (randomizador + editores dedicados por módulo) — Hecho
  - 1.4. Exportación a ZIP con árbol LayeredFS (`romfs` y `exefs`) — Hecho
  - 1.5. Separación en librería agnóstica de plataforma (`pk3DS.Editors`) + host — Hecho
  - 1.6. Andamiaje común de exportación (`EditorSession`) reusado por todos los editores — Hecho

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
  - 3.4. Trainers Gen VI/VII: datos, equipo y nombres (`trdata`, `trpoke`, `gametext`) — Hecho
  - 3.5. Static Encounters Gen VII (regalos, fijos, intercambios) — Parcial (faltan campos avanzados del formato)
  - 3.6. Personal Stats — editor individual por especie — Hecho
  - 3.7. Evolutions — editor individual por especie — Hecho
  - 3.8. Move Stats — editor individual por movimiento — Hecho
  - 3.9. Item Stats — editor individual por objeto — Hecho
  - 3.10. Battle Maison / Royal / Tree — Hecho
  - 3.11. Pickup Gen VII — Hecho
  - 3.12. Title Screen Gen VI — Parcial: inventario de DARC por juego/idioma, vista previa PNG de BCLIM compatibles, exportación raw/PNG y reemplazo PNG/BCLIM con salida a un DARC o copia GARC nueva; falta ETC1 y la inserción persistente en el workspace
  - 3.13. OWSE / scripts (mapas, scripts, texto) — Pendiente

- **4. Editores ExeFS / CRO** — Parcial
  - 4.1. TMs / HMs — Hecho: Gen VI/VII, lectura por firma y exportación de `code.bin` a ExeFS LayeredFS
  - 4.2. Move Tutors — Hecho: Gen VI en `code.bin` y Gen VII en `Shop.cro`, con listas variables/precios y exportación LayeredFS
  - 4.3. Poké Mart — Hecho: Gen VI en `code.bin` y Gen VII en `Shop.cro`, con inventarios normales/BP y exportación LayeredFS
  - 4.4. Pickup Gen VI — Hecho: listas común/rara y exportación de `code.bin` a ExeFS LayeredFS
  - 4.5. O-Powers Gen VI — Hecho: 65 registros editables en `code.bin`, con exportación ExeFS LayeredFS
  - 4.6. Shiny Rate Gen VI/VII — Hecho: rerolls con el catálogo de instrucciones original y opción de todo shiny, exportación de `code.bin` a ExeFS LayeredFS
  - 4.7. Starter Pokémon Gen VI (requiere CRO) — Hecho: grupos XY y ORAS, edición de especies y exportación conjunta de `DllPoke3Select.cro` / `DllField.cro`
  - 4.8. Type Chart — Hecho: Gen VI desde `DllBattle.cro` y Gen VII desde `code.bin`, con matriz 18×18 y exportación LayeredFS
  - 4.9. Gift Pokémon Gen VI (requiere CRO) — Hecho: entradas XY/ORAS, campos comunes e IVs, edición web y exportación de `DllField.cro`
  - 4.10. Static Encounters Gen VI (`DllField.cro`) — Parcial (edición individual lista; falta parche RO de Luma para usarlo)
  - 4.11. CRO / CRR patching — Parcial: los exports de CRO recalculan hashes internos y generan `.crr/static.crr`; falta el parche RO de verificación RSA en consola

- **5. Herramientas de proyecto** — Parcial
  - 5.1. Extracción de CXI/3DS — Parcial: extracción headless desde la web a un workspace nuevo, con selector nativo de archivos
  - 5.2. Empaquetado de RomFS/ExeFS — Parcial: la web construye `romfs.bin` y/o `exefs.bin` desde un workspace extraído, con validación de rutas y sin tocar el origen
  - 5.3. Reconstrucción de ROM — Parcial: reconstrucción headless de `.3ds` desde un workspace completo, con modo recortado o padding de tarjeta; conversión a `.cia` implementada mediante `makerom` externo, pendiente validar con un dump real
  - 5.4. Creación de parches — Parcial: parche de redirección de GARCs y `.code.bin` portado; falta ensamblaje/firma CIA
  - 5.5. Edición de imágenes — Parcial: BCLIM compatibles se decodifican, se previsualizan y exportan a PNG sin System.Drawing; PNG/BCLIM se pueden convertir y reemplazar en un DARC o copia GARC de salida; falta inserción persistente en el workspace y soporte ETC1
  - 5.6. Herramientas GARC/DARC/SARC/FARC — Parcial: desempaquetado y empaquetado GARC, DARC de una capa y SARC portados; FARC tiene desempaquetado seguro de solo lectura y todavía no tiene empaquetador

- **6. Verificación y QA** — Parcial
  - 6.1. Pruebas de regresión: comparar archivos generados en macOS contra Windows con el mismo dump y semilla — Pendiente
  - 6.2. Suite de tests unitarios (`pk3DS.Editors.Tests`) — Hecho: empaquetado de bytes, offsets, guardas de validación y resolución de rutas
  - 6.3. CI en GitHub Actions (macOS y Linux) — Hecho
  - 6.4. Fixtures de GARC para probar lectura/escritura sin un dump completo — Hecho: `SyntheticXyWorkspace` arma un workspace X/Y con GARCs reales, y los editores se prueban de punta a punta hasta inspeccionar el ZIP LayeredFS
  - 6.5. Fixture de Gen. VII (`SyntheticSunMoonWorkspace`) — Hecho: cubre entrenadores, encuentros estáticos, encuentros salvajes, TMs en ExeFS, tutores en `Shop.cro`, movimientos en mini-archivo y el randomizador sobre Sol/Luna
  - 6.6. Fixture ExeFS/CRO de Gen. VI (`SyntheticXyWorkspace`) — Hecho: firmas sintéticas para TMs/HMs, Pickup, Shiny Rate, O-Powers, tutores y tiendas, más `DllBattle.cro` para Type Chart y `DllField.cro` para Starter/Gift, con comparación de las salidas
  - 6.7. Fixture de encuentros salvajes Gen. VII (`Area7`) — Hecho: tablas día/noche en mini-archivo `EA`, con zonedata y worlddata sintéticos
  - 6.8. Los tests comparan la salida contra el dump de origen, no sólo la presencia del archivo en el ZIP — Hecho: también cubre el destino ExeFS de `code.bin`

## Módulos RomFS

| Módulo de Windows | Juegos | Datos principales | Estado Mac |
| --- | --- | --- | --- |
| Game Text | Gen 6/7 | `gametext` | Portado: editor de tablas y exportación LayeredFS |
| Story Text | Gen 6/7 | `storytext` | Portado: editor de tablas y exportación LayeredFS |
| Personal Stats | Gen 6/7 | `personal` | Portado: randomizador, cambios masivos y editor individual |
| Evolutions | Gen 6/7 | `evolution` | Portado: randomizador y editor individual |
| Level Up Moves | Gen 6/7 | `levelup` | Portado: randomizador y editor individual |
| Wild Encounters | Gen 6/7 | `encdata`, `zonedata`, `worlddata` | Portado: editor individual Gen. VI/VII y exportación LayeredFS de `encdata` |
| Mega Evolutions | Gen 6/7 | `megaevo` | Portado: editor individual y exportación LayeredFS |
| Egg Moves | Gen 6/7 | `eggmove` | Portado: randomizador y editor individual |
| Trainers | Gen 6/7 | `trclass`, `trdata`, `trpoke`, `gametext` | Portado: editor individual Gen. VI/VII de datos, equipo, nombre de entrenador y clase, con exportación LayeredFS conjunta |
| Battle Maison / Royal / Tree | Gen 6/7 | `maisontr*`, `maisonpk*` | Portado: variantes normal/super o Tree/Royal, edición de entrenadores y Pokémon, exportación LayeredFS |
| Item Stats | Gen 6/7 | `item` | Portado: editor individual y exportación LayeredFS |
| Move Stats | Gen 6/7 | `move` | Portado: acciones globales y editor individual |
| Static Encounters | Gen 7 | `encounterstatic` | Parcial: regalos, encuentros fijos e intercambios; edición de especie, forma, nivel, objeto y campos avanzados disponibles en el formato |
| Pickup | Gen 7 | `pickup` | Portado: tabla de objetos y probabilidades por banda de nivel, con exportación LayeredFS |
| Title Screen | Gen 6 | `titlescreen` | Parcial: inventario DARC/BCLIM, vista previa, exportación raw/PNG y reemplazo a DARC/copia GARC; falta ETC1 e integración persistente |
| OWSE / scripts | Gen 6/7 | mapas, scripts y texto | Pendiente; módulo de desarrollo |

## Módulos ExeFS y CRO

Estos módulos necesitan un workspace extraído completo (RomFS + ExeFS y, cuando corresponda, CRO). No deben prometerse con una entrada que solo contiene RomFS.

| Módulo de Windows | Juegos | Estado Mac |
| --- | --- | --- |
| TMs / HMs | Gen 6/7 | Portado: editor web, validación de IDs, detección por firma y exportación ExeFS de `code.bin` |
| Move Tutors | Gen 6/7 | Portado: Gen. VI en `code.bin`; Gen. VII en `Shop.cro`, con validación y exportación LayeredFS |
| Poké Mart | Gen 6/7 | Portado: Gen. VI en `code.bin`; Gen. VII en `Shop.cro`, con inventarios normales/BP y exportación LayeredFS |
| Pickup | Gen 6 | Portado: listas de 18 objetos comunes y 11 raros, detección por firma y exportación ExeFS de `code.bin` |
| O-Powers | Gen 6 | Portado: editor web de 65 registros, validación de rangos y exportación ExeFS de `code.bin` |
| Shiny Rate | Gen 6/7 | Portado: rerolls con instrucciones ARM soportadas, opción todo shiny, detección por firma y exportación ExeFS de `code.bin` |
| Starter Pokémon | Gen 6 | Portado: grupos de X/Y y OR/AS, editor web y exportación conjunta de ambos CRO |
| Type Chart | Gen 6/7 | Portado: matriz 18×18, Gen. VI en `DllBattle.cro`, Gen. VII en `code.bin`, editor web y exportación LayeredFS |
| Gift Pokémon | Gen 6 | Portado: campos de regalo, IVs, editor web y exportación de `DllField.cro` |
| Static Encounters | Gen 6 | Parcial: edición individual en `DllField.cro`; requiere parche RO de Luma para usar CRO modificado |
| CRO / CRR patching | Gen 6/7 según módulo | Parcial: rehash headless y CRR en exports; falta parche RO/RSA de consola |

## Herramientas de proyecto

También forman parte de pk3DS Windows: extracción de CXI/3DS, empaquetado de RomFS/ExeFS, reconstrucción de ROM, creación de parches, edición de imágenes y herramientas GARC/DARC/SARC/FARC. En Mac ya se puede extraer, empaquetar y reconstruir `.3ds` desde **Herramientas de proyecto**, y solicitar la conversión a `.cia` mediante un `makerom` externo; esa conversión aún requiere validación con un dump real. La pantalla también crea el contenido del parche de redirección (`.code.bin` y árbol `a0/`), desempaqueta/empaqueta GARCs, DARCs de una capa y SARC, desempaqueta FARC en modo de solo lectura e inventaría, previsualiza y exporta los recursos DARC/BCLIM de Title Screen; además convierte PNG/BCLIM y genera un DARC o copia GARC nueva con un recurso reemplazado, manteniendo LZSS cuando corresponde. Siguen pendientes ETC1, la inserción persistente en el workspace, el empaquetador FARC y otros contenedores.
