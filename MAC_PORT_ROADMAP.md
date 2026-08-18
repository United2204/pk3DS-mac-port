# pk3DS para macOS: inventario de paridad

Este documento usa `pk3DS.WinForms` como especificación de comportamiento. Un módulo no se considera portado solo por aparecer en la interfaz: debe leer el mismo formato, aplicar las mismas reglas y generar una salida usable por Luma LayeredFS.

La matriz de perfiles confirmados de OWSE Gen. VII está en [OWSE_ED_STRUCTURE.md](OWSE_ED_STRUCTURE.md); las variantes no confirmadas permanecen en diagnóstico/raw hasta contar con un round-trip verificable.

## Estado actual de la web

La web cubre el randomizador de RomFS completo, la exportación ExeFS de TMs/HMs, más de quince editores individuales y el empaquetado standalone de RomFS/ExeFS. No equivale todavía a pk3DS para Windows: faltan otros módulos ExeFS/CRO y varias herramientas de proyecto.

> Mantené este documento sincronizado con el código **en el mismo commit** que agrega o completa un módulo. Ya se desincronizó una vez: cuatro editores figuraban como pendientes estando implementados.

## Work Breakdown Structure (WBS)

Desglose jerárquico del port completo. El detalle de cada módulo (formatos, exportación LayeredFS) está en las tablas de las secciones siguientes; esta sección es la vista de conjunto para seguimiento de avance.

- **1. Fundación de la app web** — Hecho
  - 1.1. Servidor local ASP.NET (`pk3DS.Mac.Web`) — Hecho
  - 1.2. Detección automática de juego y Title ID desde `exheader.bin` — Hecho
  - 1.3. Shell React con React Router, navegación por RomFS/ExeFS/CRO/Salida y puente de compatibilidad para editores existentes — Hecho (migración de formularios en curso)
  - 1.4. Exportación a ZIP con árbol LayeredFS (`romfs` y `exefs`) — Hecho
  - 1.5. Separación en librería agnóstica de plataforma (`pk3DS.Editors`) + host — Hecho
  - 1.6. Andamiaje común de exportación (`EditorSession`) reusado por todos los editores — Hecho

- **2. Randomizador de datos RomFS** — Parcial
  - 2.1. Personal Stats (habilidades, objetos, catch rate, tipos, egg groups, stats base, MT/MO, tutores) — Hecho
  - 2.2. Level Up Moves (cantidad, distribución, STAB, potencia, cuatro movimientos iniciales, exclusión de daño fijo) — Hecho
  - 2.3. Egg Moves — Hecho
  - 2.4. Evolutions (randomizar resultados, eliminar intercambios, evolución por nivel) — Hecho
  - 2.5. Move Stats — acciones globales (tipos, categorías, modo Metronome) — Hecho
  - 2.6. Wild Encounters — Hecho: randomización masiva de especies, formas, niveles y hordas homogéneas en Gen. VI; especies, formas, niveles y SOS/clima en Gen. VII, con una única exportación y copia OR/AS sincronizada
  - 2.7. Trainer Teams — Parcial: randomización masiva segura de especies, formas, niveles, clases, composición/cantidad con límites mínimo/máximo, objetos equipados, habilidades, movimientos, ataques potentes, naturalezas, shiny, temas por tipo, temas compartidos con entrenadores de gimnasio, formas Mega opcionales en Gen. VI, IA avanzada, IVs máximos y evolución final desde un nivel configurable en Gen. VI/VII; Gen. VI también incluye premios con probabilidad configurable y Gen. VII puede completar equipos importantes hasta seis Pokémon, junto con protección de clases especiales y filtro de combates individuales. Los slots nuevos conservan los bytes del último registro válido. Faltan reglas adicionales de entrenadores de Windows

- **3. Editores individuales RomFS** — Parcial
  - 3.1. Game Text / Story Text (tabla, línea, búsqueda, exportación LayeredFS) — Hecho
  - 3.2. Mega Evolutions — Hecho
  - 3.3. Wild Encounters Gen VI/VII (`encdata`) — Hecho
  - 3.4. Trainers Gen VI/VII: datos, equipo y nombres (`trdata`, `trpoke`, `gametext`) — Hecho
  - 3.5. Static Encounters Gen VII (regalos, fijos, intercambios) — Hecho: especies, forma, nivel, objetos, género, habilidad, naturaleza, shiny, mapa, aliados SOS, movimientos relearn, IVs/EVs y datos de OT/intercambios con exportación LayeredFS
  - 3.6. Personal Stats — editor individual por especie — Hecho
  - 3.7. Evolutions — editor individual por especie — Hecho
  - 3.8. Move Stats — editor individual por movimiento — Hecho
  - 3.9. Item Stats — editor individual por objeto — Hecho
  - 3.10. Battle Maison / Royal / Tree — Hecho
  - 3.11. Pickup Gen VII — Hecho
  - 3.12. Title Screen Gen VI — Parcial: inventario de DARC por juego/idioma, vista previa PNG de BCLIM compatibles incluyendo ETC1/ETC1A4, exportación raw/PNG, reemplazo PNG/BCLIM conservando el formato original —incluido ETC1/ETC1A4— y la envoltura retail alrededor de `darc`, con salida a un DARC o copia GARC nueva, aplicación al workspace con backup y listado/restauración protegida de backups; faltan otros flujos avanzados
  - 3.13. OWSE / scripts (mapas, scripts, texto) — Parcial: inspector de scripts Gen. VI `ZO` y Gen. VII `ZS`/`ZI`; Gen. VI permite editar metadatos, entidades de zona, propiedades de movimiento `uint32` de `mapGR` e instrucciones `uint32` de scripts, y Gen. VII permite editar/exportar el mapa padre de `zonedata` y el área de encuentros de la tabla `WD` asociada al mundo, inventariar `ED` hasta sus subentradas y cabeceras `count`/`kind`, exportar el contenedor ED descomprimido por bloque/subentrada con manifiesto, incluyendo bloque, tamaño y prefijo hexadecimal de variantes aún no interpretadas, editar/exportar posiciones `EP`, `EM` principal, `EI` tipo 10, `PR` tipos 203/204, `EB` tipo 2, `ES` tipo 4, `EA` tipos 5/6 y `ET` tipos 7/9 confirmadas e instrucciones manteniendo la cantidad y preservando bytes desconocidos; faltan edición de mapas 3D/texto de alto nivel, el esquema EM tipo 3, `PR` tipo 364, las variantes ES cortas y los demás campos de entidades Gen. VII

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
  - 4.11. CRO / CRR patching — Parcial: los exports de CRO y la herramienta independiente de proyecto recalculan hashes internos y generan solo los cambios necesarios de `.crr/static.crr` como LayeredFS; falta el parche RO de verificación RSA en consola

- **5. Herramientas de proyecto** — Parcial
  - 5.1. Extracción de CXI/3DS/CIA — Parcial: extracción headless desde la web a un workspace nuevo, con selector nativo de archivos; los CIA deben estar desencriptados y completos y se extrae su primer contenido NCCH. La validación central diagnostica RomFS, ExeFS, exheader, icon.bin SMDH y code.bin, incluyendo BLZ y alineación 0x200, comprueba los GARCs/CRO específicos de cada editor y deshabilita las funciones cuya fuente no está disponible
  - 5.2. Empaquetado de RomFS/ExeFS — Parcial: la web construye `romfs.bin` y/o `exefs.bin` desde un workspace extraído, con validación de rutas y sin tocar el origen
  - 5.3. Reconstrucción de ROM — Parcial: reconstrucción headless de `.3ds` y conversión a `.cia` mediante `makerom` macOS arm64 incluido ejecutadas correctamente contra dumps reales de X y ORAS; el CIA de X se instaló y abrió manualmente en Azahar, y falta validar en consola real. Los editores también fueron probados hasta la salida LayeredFS; ver `REAL_DUMP_VALIDATION.md`
  - 5.4. Creación de parches — Parcial: parche de redirección de GARCs y `.code.bin` portado; falta ensamblaje/firma CIA
  - 5.5. Edición de imágenes — Parcial: BCLIM y BFLIM compatibles, incluidos ETC1/ETC1A4, se decodifican, previsualizan y convierten a PNG sin System.Drawing; Herramientas de proyecto convierte PNG↔BCLIM/BFLIM y permite elegir RGBA8, ETC1 o ETC1A4; el reemplazo de imágenes de Title Screen conserva el formato BCLIM original y ya codifica ETC1/ETC1A4 de forma portable; PNG/BCLIM también se pueden reemplazar en un DARC o copia GARC de salida, o aplicar al GARC del workspace con backup y LZSS; faltan otros formatos de edición
  - 5.6. Icono SMDH — Parcial: lectura portable de `ExeFS/icon.bin`, metadatos de los 16 slots AppInfo, previsualización de iconos 24×24/48×48, exportación de `icon.bin` más PNG, edición de textos/iconos y de los campos conocidos de `ApplicationSettings` (ratings, regiones, flags, EULA, MatchMaker, animación y StreetPass), importación de un SMDH completo con backup y escritura atómica, y listado/restauración protegida de esos backups; quedan sólo variantes o campos reservados no documentados
  - 5.7. Herramientas GAR/GARC/Mini/ALYT/Shuffle ARC/DARC/SARC/FARC — Parcial: desempaquetado seguro del GAR legacy con sus tablas de nombres y offsets, desempaquetado y empaquetado GARC, reordenamiento reproducible de referencias FATB de GARC en una copia, Mini/BinLinker con identificadores de dos letras, desempaquetado y empaquetado ALYT con validación de tablas, etiquetas/símbolos opcionales y extracción o envoltura del SARC embebido, desempaquetado seguro de Shuffle ARC en fragmentos raw numerados, DARC con carpetas anidadas y SARC portados; GAR valida el tamaño declarado, las tablas, los nombres terminados en NUL, los rangos y las rutas, GARC valida cabeceras, tablas, cantidad de archivos y rangos de FIMB, Mini valida firma, offsets y cobertura completa, DARC valida los rangos del árbol, los nombres y los offsets de datos, SARC valida cabeceras, tablas, referencias de nombres y rangos de datos, y Shuffle ARC valida cabecera, tabla, límites y solapamientos antes de escribir la salida; FARC tiene desempaquetado/empaquetado SIR0 con nombres UTF-16 y validación de rutas, además de desempaquetado/reempaquetado por CRC32 usando nombres sintéticos; el detector compartido también identifica SARC y FARC válidos sin depender de la extensión; la pantalla ofrece desempaquetado automático por firma y empaquetado automático por las convenciones de carpetas `_g`, `_d` y `_XX`; LZ11 y BLZ se pueden comprimir/descomprimir como operaciones independientes; faltan otros contenedores

  - 5.8. Paridad DARC — Hecho: el empaquetador manual acepta una plantilla original y el modo automático detecta plantillas vecinas para conservar la envoltura completa alrededor del DARC y evitar sobrescrituras
  - 5.9. Mini anidados — Hecho: el desempaquetado automático abre contenedores Mini internos hasta ocho niveles, con opción raw plana y preservación del origen
  - 5.10. Previsualización OWSE Gen. VI — Hecho: la matriz `MM` puede reconstruirse visualmente desde sus entradas Mini `GR` y entregarse como PNG embebido, con diagnóstico parcial o seguro cuando faltan entradas o el formato no es interpretable
  - 5.11. Metadatos ZoneData OWSE — Hecho: además de mapa, matriz, texto, script, padre y clima, se editan BGM estacionales, flags de movimiento, cámara y coordenadas con preservación de bits no expuestos
  - 5.12. Cabeceras Mini retail — Hecho: el empaquetador conserva mediante plantilla el primer offset de archivos BinLinker con padding no estándar; el autodetectado encuentra automáticamente la plantilla adyacente y la interfaz permite seleccionarla manualmente

- **6. Verificación y QA** — Parcial
  - 6.1. Pruebas de regresión: comparar archivos generados en macOS contra Windows con el mismo dump y semilla — Parcial: lectura y exportación round-trip verificadas contra dumps reales de X y ORAS, incluyendo lectura OWSE Gen. VI; falta comparación byte a byte con Windows y arranque en consola (la apertura en Azahar ya fue validada; ver `REAL_DUMP_VALIDATION.md`)
  - 6.2. Suite de tests unitarios (`pk3DS.Editors.Tests`) — Hecho: empaquetado de bytes, offsets, guardas de validación y resolución de rutas
  - 6.3. CI en GitHub Actions (macOS y Linux) — Hecho
  - 6.4. Fixtures de GARC para probar lectura/escritura sin un dump completo — Hecho: `SyntheticXyWorkspace` arma un workspace X/Y con GARCs reales, y los editores se prueban de punta a punta hasta inspeccionar el ZIP LayeredFS
  - 6.5. Fixture de Gen. VII (`SyntheticSunMoonWorkspace`) — Hecho: cubre entrenadores, encuentros estáticos, encuentros salvajes, scripts OWSE `ZS`/`ZI` y metadatos de zona, TMs en ExeFS, tutores en `Shop.cro`, movimientos en mini-archivo y el randomizador sobre Sol/Luna
  - 6.6. Fixture ExeFS/CRO de Gen. VI (`SyntheticXyWorkspace`) — Hecho: firmas sintéticas para TMs/HMs, Pickup, Shiny Rate, O-Powers, tutores y tiendas, `encdata`/`ZO` con cabecera de entidades para OWSE, más `DllBattle.cro` y `DllField.cro`; `SyntheticOrasWorkspace` cubre además el offset OR/AS de `encdata` y Title Screen comprimido
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
| Static Encounters | Gen 7 | `encounterstatic` | Portado: regalos, encuentros fijos e intercambios; campos comunes y avanzados del formato, con exportación LayeredFS |
| Pickup | Gen 7 | `pickup` | Portado: tabla de objetos y probabilidades por banda de nivel, con exportación LayeredFS |
| Title Screen | Gen 6 | `titlescreen` | Parcial: inventario DARC/BCLIM, vista previa ETC1/ETC1A4, exportación raw/PNG, reemplazo a DARC/copia GARC conservando la envoltura retail, aplicación persistente y restauración protegida de backups |
| OWSE / scripts | Gen 6/7 | mapas, scripts y texto | Parcial: lectura de `ZO` y `ZS`/`ZI`; edición/exportación Gen. VI de metadatos, entidades (muebles, NPC, warps y triggers), grilla de propiedades `mapGR`, entradas interpretadas de la matriz `MM` e instrucciones `uint32`, más edición/exportación Gen. VII del mapa padre de `zonedata`, el área de encuentros de `worlddata/WD`, inventario estructural `ED` hasta sus cabeceras `count`/`kind`, exportación raw de ED por bloque/subentrada con manifiesto, posiciones `EP`, `EM` principal, `EI` tipo 10, `PR` tipos 203/204, `EB` tipo 2, `ES` tipo 4, `EA` tipos 5/6 y `ET` tipos 7/9 confirmadas e instrucciones `uint32`; faltan mapas 3D/texto de alto nivel, el esquema EM tipo 3, `PR` tipo 364, las variantes ES cortas y los demás campos editables de entidades Gen. VII |

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

También forman parte de pk3DS Windows: extracción de CXI/3DS/CIA, empaquetado de RomFS/ExeFS, reconstrucción de ROM, creación de parches, edición de imágenes y herramientas GAR/GARC/Mini/ALYT/Shuffle ARC/DARC/SARC/FARC. En Mac ya se puede extraer un CXI, 3DS o CIA desencriptado —del CIA se toma el primer NCCH—, empaquetar y reconstruir `.3ds` desde **Herramientas de proyecto**, y solicitar la conversión a `.cia` mediante el `makerom` macOS arm64 incluido; ambas operaciones fueron ejecutadas con dumps reales de X y ORAS, y el CIA de X se instaló/abrió manualmente en Azahar, aunque aún falta validar en consola real. La pantalla también crea el contenido del parche de redirección (`.code.bin` y árbol `a0/`), desempaqueta GAR legacy y GARC, empaqueta GARC, reordena referencias FATB de GARC en copias con semilla, Mini/BinLinker, ALYT con su SARC embebido, extrae Shuffle ARC en fragmentos raw numerados, DARCs con carpetas anidadas, SARC y FARC SIR0 con nombres UTF-16 o índices CRC32/hash con nombres sintéticos, convierte PNG↔BCLIM, exporta BFLIM a PNG/BCLIM, inspecciona y exporta `ExeFS/icon.bin` como SMDH/PNG y comprime/descomprime LZ11/BLZ de forma independiente, e inventaría, previsualiza y exporta los recursos DARC/BCLIM de Title Screen —incluidos ETC1/ETC1A4—; además genera un DARC o copia GARC nueva con un recurso reemplazado, puede actualizar el GARC del workspace con backup y LZSS y permite listar/restaurar backups con una copia de seguridad previa del estado actual. Siguen pendientes otros contenedores.
