# pk3DS Mac Port

Port a macOS del editor/randomizador de ROMs pk3DS para juegos Pokémon de 3DS.

Este repositorio es un **port nativo a macOS** de [pk3DS](https://github.com/kwsch/pk3DS), el editor de ROMs para juegos Pokémon de 3DS creado originalmente por kwsch y su comunidad. La versión original está pensada para Windows Forms; este proyecto adapta el núcleo (`pk3DS.Core`) a una interfaz web local que corre en Mac, sin depender de Windows ni de Wine.

## Tabla de contenidos

- [Qué es esto](#qué-es-esto)
- [Uso](#uso)
- [Opciones activas hoy](#opciones-activas-hoy)
- [Estado del port](#estado-del-port)
- [Créditos](#créditos)

## Qué es esto

Interfaz local para macOS basada en `pk3DS.Core`. Arranca normalmente en `http://127.0.0.1:38473` y abre el navegador predeterminado; si ese puerto ya está ocupado por otra instancia, busca automáticamente uno libre cercano. El servidor sólo escucha en tu Mac: ningún archivo se sube a internet.

## Uso

1. Extraé una copia propia y desencriptada del juego hasta tener una carpeta `RomFS` completa (debe contener `a`) y conservá el archivo `exheader.bin` junto a esa carpeta. Desde **Herramientas de proyecto** también podés extraer directamente un CXI, 3DS o CIA desencriptado; en el caso del CIA se toma su primer contenido NCCH.
2. Abrí `run-mac.command` con doble clic. Si macOS bloquea el archivo, usá clic derecho → **Abrir** la primera vez.
3. Pulsá **Examinar…** y elegí la carpeta extraída del juego. pk3DS detectará el juego y el Title ID desde `exheader.bin`.
4. Marcá las opciones que quieras aplicar; al pasar el mouse sobre una de ellas se muestra una explicación breve.
5. Pulsá **Exportar** y elegí la carpeta donde guardar el ZIP.
6. Descomprimí el ZIP resultante en la raíz de la SD de la consola. Activa *Enable game patching* en Luma y usá la actualización del juego que corresponda a tu dump.

La salida inicial genera el árbol `luma/titles/<title-id>/romfs`. El randomizador no reconstruye `.cia` ni `.cxi`, ni altera el RomFS de origen.

La pantalla **Herramientas de proyecto** permite seleccionar un workspace extraído y construir `romfs.bin`, `exefs.bin` o ambos en una carpeta de salida separada. Esta operación tampoco modifica el origen.

Desde **Herramientas de proyecto** en la navegación React también podés desempaquetar `romfs.bin` a su árbol de carpetas y volver a empaquetarlo, o desempaquetar un `exefs.bin` independiente a archivos `.bin` y volver a empaquetar hasta diez archivos ExeFS. RomFS valida la cabecera IVFC, tablas, nombres, rutas y rangos, y publica la extracción desde una carpeta temporal; ExeFS valida nombres de ocho bytes, offsets, padding y hashes SHA-256. Ninguna operación modifica el origen ni sobrescribe archivos existentes.

La misma pantalla extrae archivos `.cxi`, `.3ds` y `.cia` desencriptados a un workspace nuevo con `RomFS`, `ExeFS` y `exheader.bin`, y reconstruye una ROM `.3ds` recortada o con padding de tarjeta. Para un CIA se toma su primer contenido NCCH. La extracción web corre en una tarea de fondo y muestra las etapas y el progreso por bytes mientras copia o desempaqueta RomFS. El contrato local usa `POST /api/workspace/extract/jobs` con `inputPath` y `outputDirectory` opcional, devuelve `jobId` y `statusUrl`, y permite consultar ese URL con `GET` hasta obtener `completed` o `failed`; la respuesta incluye mensaje, progreso y el resultado o error final. Devuelve `409` si ya hay otra tarea de extracción activa. La reconstrucción y la conversión a `.cia` mediante el `makerom` incluido fueron ejecutadas correctamente contra workspaces reales de Pokémon X y Omega Ruby; la instalación y apertura del CIA se validaron manualmente en Azahar.

También puede crear el contenido de un parche de redirección: muestra el catálogo de GARCs del juego con selección múltiple y controles para marcar todos o ninguno, además de permitir agregar nombres o rutas de RomFS. El contrato `POST /api/workspace/redirect-patch/options` recibe `{ "workspacePath": "…" }` y devuelve `gameVersion` y `archives`; `POST /api/workspace/redirect-patch` recibe los nombres seleccionados en `garcNames`, rutas adicionales opcionales y los ajustes de idioma/salida. El parche actualiza las rutas seleccionadas en `.code.bin` y copia los GARCs al árbol `a0/`. La aplicación no conserva firmas retail del contenido modificado, pero la conversión completa está disponible con el `makerom` macOS arm64 incluido o con un ejecutable externo compatible.

La misma pantalla permite desempaquetar y empaquetar archivos GARC desde carpetas numeradas, incluyendo la opción de no intentar descomprimir entradas LZSS durante la extracción. Antes de extraer valida las etiquetas, tablas, cantidad de archivos y rangos del bloque FIMB; si el archivo está truncado no crea una salida parcial.

También admite DARC con carpetas anidadas y conserva el árbol completo al desempaquetar y volver a empaquetar. Al empaquetar se puede indicar una plantilla DARC existente para conservar su envoltura —incluidos los bytes previos y posteriores al DARC declarado— y su estructura; el modo automático busca una plantilla vecina de una carpeta "_d" y crea una salida "-repacked" si el nombre original ya existe. Los nombres se validan para impedir rutas inseguras; los archivos originales y las carpetas de entrada se conservan intactos.
El desempaquetado automático también recorre los contenedores Mini anidados, hasta ocho niveles, y ofrece una opción para dejar los bloques internos como archivos raw.

También admite SARC con rutas raíz y anidadas, nombres UTF-8 y alineación de datos configurable. Antes de desempaquetar verifica cabeceras, tablas, referencias de nombres y rangos de datos; rechaza rutas inseguras y el empaquetado trabaja sobre una copia de la carpeta de entrada. El detector compartido también reconoce SARC y FARC válidos al renombrar o identificar archivos sin extensión.

También puede desempaquetar GAR legacy —distinto de GARC— validando sus tablas de nombres y offsets. La variante FARC heredada usa índice SIR0 y nombres UTF-16/rutas anidadas. Las variantes indexadas por hash se pueden desempaquetar con nombres sintéticos `hash-XXXXXXXX.bin` y volver a empaquetar seleccionando el índice CRC32/hash; esos nombres sintéticos conservan la clave aunque el archivo no contenga los nombres originales. Los contenedores ALYT se pueden desempaquetar y empaquetar validando sus tablas, extrayendo o envolviendo el SARC embebido y conservando etiquetas/símbolos opcionales. También se pueden desempaquetar Shuffle ARC, validando la tabla de offsets y conservando cada fragmento raw como archivo numerado aunque no sea un ZIP válido.

La sección **Convertir imágenes** previsualiza BCLIM/BFLIM como PNG en memoria y convierte PNG↔BCLIM/BFLIM sin depender de System.Drawing. La vista previa admite hasta 4 megapíxeles. PNG se puede codificar como BCLIM o BFLIM RGBA8, ETC1 o ETC1A4, y BCLIM/BFLIM se exportan a PNG conservando sus dimensiones. La conversión genera un archivo nuevo y nunca sobrescribe la entrada ni una salida existente.

La sección **Icono SMDH** lee `ExeFS/icon.bin` cuando existe, muestra los metadatos disponibles de sus 16 slots AppInfo y previsualiza los iconos internos de 24×24 y 48×48 píxeles. Exporta una copia de `icon.bin` y PNG que incluyen los cambios actuales del formulario, sin aplicarlos al workspace. Al aplicar textos, iconos o los ajustes conocidos de `ApplicationSettings` —ratings regionales, bloqueo regional, flags, EULA, MatchMaker y StreetPass— o importar un SMDH completo crea primero un backup en `.pk3ds-backups` y actualiza el workspace de forma atómica; la importación también puede crear `icon.bin` si todavía no existe.

La tarea **Respaldar / restaurar originales** de Herramientas de proyecto guarda una copia inicial de los GARCs configurados para el idioma de datos seleccionado, todos los archivos ExeFS y los CRO/CRS/CRR del workspace. Las copias existentes no se sobrescriben; podés restaurar una selección, incluso si borraste alguno de esos archivos, y cada restauración guarda primero el estado actual en una carpeta de seguridad dentro de `.pk3ds-backups`.

Las secciones **Comprimir / descomprimir LZ11** y **Comprimir / descomprimir BLZ** procesan archivos sueltos con los codecs usados por los recursos de 3DS. Permiten elegir la operación y una salida nueva; validan la cabecera al descomprimir y conservan siempre el original. BLZ incluye además compresión optimizada y modo ARM9.

En **Herramientas de proyecto** también podés analizar la pantalla de título de X/Y y OR/AS. La herramienta lista los DARCs por juego e idioma, muestra una vista previa PNG de los BCLIM compatibles —incluidos ETC1 y ETC1A4—, exporta los recursos originales junto con un `manifest.json` y genera PNG para formatos compatibles; también acepta un PNG o BCLIM del mismo tamaño. Se pueden agregar varios recursos a una cola y generar un solo GARC o aplicar todos los cambios al workspace con una única copia de seguridad. Si reemplazás con PNG, conserva el formato BCLIM original, incluido ETC1/ETC1A4, y conserva la envoltura retail que aparezca antes o después de la cabecera `darc`, tanto en GARC X/Y como en las entradas OR/AS comprimidas. La aplicación conserva la compresión LZSS de OR/AS. Desde la misma pantalla se pueden listar y restaurar las copias; antes de restaurar se guarda otra copia del GARC actual. Las salidas de revisión no modifican el workspace.

Para crear un `.cia`, la aplicación incluye `makerom` macOS arm64 y permite indicar otro ejecutable si se desea. Primero reconstruye una `.3ds` temporal y valida la cabecera, el primer NCCH y los rangos de exheader, ExeFS y RomFS antes de publicar el CIA; usa `-ignoresign` porque el NCCH reconstruido ya no conserva firmas retail válidas. Se puede seleccionar una carpeta de parche de redirección creada desde la misma pantalla; su manifiesto se valida contra la versión del juego, el Title ID cuando se creó con `exheader.bin`, y los hashes del `code.bin` y de los archivos originales. Sus archivos se integran en una copia temporal aislada del workspace. En APFS usa clones copy-on-write; en otros volúmenes recurre a copias normales. El resultado se genera sin cifrado de contenido y con el NCCH marcado `NoCrypto`, por lo que es el formato esperado por Azahar. La generación normal fue probada con dumps reales de X y ORAS, la lectura de vuelta del CIA de X fue correcta y el archivo se instaló/abrió manualmente en Azahar; todavía falta validar un CIA con parche y probarlo en una consola real.

La validación central informa por separado si encontró `RomFS`, `ExeFS`, `exheader.bin`, `icon.bin` y `code.bin`, comprueba que `icon.bin` sea un SMDH válido y que `code.bin` sea legible y quede alineado a `0x200` bytes, y verifica los GARCs/CRO concretos que necesita cada módulo. Si viene comprimido con BLZ, los editores ExeFS lo descomprimen en memoria sin modificar el workspace; si es inválido o falta una fuente específica, esa función queda deshabilitada y el diagnóstico explica el motivo. El Title ID se obtiene automáticamente desde `exheader.bin`, por lo que la carpeta que elijas debe incluirlo. Si solo disponés de `RomFS`, la herramienta puede revisarlo, pero no podrá crear un LayeredFS con el Title ID correcto.

En **GAR / GARC / Mini / ALYT / Shuffle ARC / DARC / SARC / FARC** también se pueden desempaquetar archivos GAR, desempaquetar y empaquetar archivos Mini/BinLinker identificados por dos letras (`WD`, `ZO`, `EP`, etc.), desempaquetar o empaquetar ALYT extrayendo o envolviendo su SARC interno, con etiquetas y símbolos opcionales, extraer Shuffle ARC en fragmentos raw numerados y reordenar referencias FATB de un GARC en una copia con semilla reproducible. El shuffler detecta el perfil y bloquea los mismos GARCs sensibles que Windows: lista ORAS para ORAS y lista XY como alternativa para los demás perfiles; si el archivo independiente no permite identificar el juego, informa que esa protección no se aplicó. Las entradas **Desempaquetado automático** y **Empaquetado automático** reproducen las convenciones de Windows: detectan la firma del archivo o el sufijo de la carpeta (`_g`, `_d`, `_XX`) y derivan a la operación correcta. Cuando no se indica una salida, GARC/DARC/Mini usan las carpetas `_g`, `_d` y `_XX`, y los demás contenedores usan una carpeta `<nombre>-unpacked`. Los bloques se ordenan de forma determinista, se validan los offsets y el archivo original o la carpeta de entrada quedan intactos.

El selector global de idioma conserva la elección entre sesiones y la aplica a los catálogos de los editores embebidos; inglés es el valor inicial y X/Y u OR/AS limitan la lista a los idiomas que esos juegos contienen.

## Opciones activas hoy

- habilidades, objetos llevados, ratio de captura, tipos, grupos huevo y estadísticas base;
- compatibilidad de MT/MO y tutores;
- learnsets configurables: cantidad, distribución por nivel, STAB, potencia, cuatro movimientos iniciales y exclusión de daño fijo;
- movimientos huevo configurables;
- editor de objetos de Pickup de Gen. VII, incluyendo sus probabilidades por banda de nivel;
- editor de Battle Maison / Battle Tree / Battle Royal para los registros de entrenadores y Pokémon;
- acciones globales de Move Stats: tipos, categorías físico/especial y modo Metronome;
- evoluciones: conservar, randomizar resultados con filtros de BST/EXP/tipo, eliminar intercambios o modo de evolución por nivel.
- encuentros salvajes: randomizar especies, formas y niveles en Gen. VI/VII; las hordas homogéneas quedan disponibles en Gen. VI y los filtros de BST, legendarios y singulares se aplican desde el mismo exportador.
- Game Text / Story Text: búsqueda y edición de líneas, inserción/eliminación, importación/exportación del TXT completo de pk3DS para Windows y randomización smart o pura de una tabla o de todo el archivo. Si una tabla no se puede codificar, se conserva su original, se exportan los cambios válidos y se pueden descargar `text_errata.txt` y `text_failed.txt`. Las exportaciones generan UTF-16LE o ZIP LayeredFS y conservan intacto el workspace.
- equipos de entrenadores: pool por generación (I–VI/VII), especies, formas, niveles y clases; los límites de equipo se aplican como en Windows, las hordas Gen. VI quedan intactas y los equipos importantes se pueden completar a seis Pokémon en ambas generaciones. Incluye objetos, habilidades, movimientos aleatorios, por nivel o Metronome, filtros configurables para movimientos dañinos/STAB y daño fijo, ataques potentes, naturalezas, shiny, temas por tipo, Megas de historia y formas Mega opcionales en Gen. VI/VII, IA avanzada, IVs máximos y evolución final configurable; los legendarios y singulares se habilitan por defecto, como en Windows, y Shedinja está siempre disponible en Gen. VI. Gen. VII conserva los pares de Megapiedras y solo cambia objetos o naturalezas cuando sus opciones están activadas. En Gen. VI, las plazas añadidas conservan los campos originales no editados; en Gen. VII comienzan en cero, igual que en Windows; las clases especiales se pueden proteger. Gen. VI también permite randomizar premios con una probabilidad configurable.

- encuentros estáticos de Gen. VI: Randomize All sobre la tabla de `DllField.cro`, con pools de generaciones I–VI, legendarios y singulares, BST, Megas, objetos, niveles, habilidades, shiny locks, evolución final y semilla reproducible; la edición individual sigue disponible en la misma pantalla.
- regalos de Gen. VI: Randomize All sobre la tabla de `DllField.cro`, con pools de generaciones I–VI, míticos y legendarios, BST, reemplazo de regalos con Megapiedra, formas Mega, objetos, niveles, habilidades, shiny locks, evolución final y semilla reproducible. Los regalos con Megapiedra se conservan completos si no se activa su reemplazo; el parche se entrega como ZIP LayeredFS y requiere el parche RO de Luma para usar el CRO modificado en consola.
- starters de Gen. VI: Randomize All para los grupos de X/Y y Rubí Omega / Zafiro Alfa, con iniciales básicos o especies filtradas por generación, legendarios, míticos/eventos y semilla reproducible; exporta juntos `DllPoke3Select.cro` y `DllField.cro` como parche LayeredFS.
- encuentros estáticos de Gen. VII: acciones separadas para randomizar los tres iniciales o regalos, encuentros fijos e intercambios; incluye pools por generación, legendarios/singulares, BST, objetos, niveles, shiny locks, habilidades, formas, evolución final, movimientos especiales, auras de tótem, Metronome y semilla reproducible. Al randomizar los iniciales se puede actualizar `storytext` para el idioma seleccionado.

El randomizador admite una semilla entera con signo de 32 bits para repetir las selecciones con las mismas opciones y archivos; si se deja vacía, usa el RNG aleatorio habitual.

Además hay editores individuales para texto de juego e historia, movimientos por nivel, movimientos huevo, evoluciones, datos personales, movimientos, objetos, TMs/HMs (Gen. VI y VII), Move Tutors (Gen. VI, cuya edición no tiene efecto en juego confirmado, y Gen. VII), Poké Mart (Gen. VI y VII), O-Powers (Gen. VI), Shiny Rate (Gen. VI y VII), Type Chart (Gen. VI y VII), Starter Pokémon (Gen. VI), Gift Pokémon (Gen. VI), Pickup (Gen. VI y VII), Battle Maison / Tree / Royal, megaevoluciones, encuentros salvajes (Gen. VI y VII), encuentros estáticos (Gen. VI y VII) y entrenadores (Gen. VI y VII). La página **OWSE / Scripts** inspecciona `ZO` de Gen. VI y `ZS`/`ZI` de Gen. VII, y permite editar/exportar metadatos, entidades de zona Gen. VI (muebles, NPC, warps y triggers), exportar zonas y entidades, mapas PNG y reportes JSON masivos de scripts Gen. VI y VII, el inventario estructural `ED` con tamaños y cabeceras de subentradas, exportar el ED descomprimido con sus bloques y un `manifest.json`, las posiciones `EP`, `EM` tipos 1/3, `EI` tipo 10, `PR` tipos 203/204/364, `EB` tipos 2/11, `EG` tipo 8, `ES` tipo 4, `EA` tipos 5/6 y `ET` tipos 7/9 de entidades Gen. VII; también muestra como raw la tabla de descriptores `ES` tipo 0, en USUM inspecciona y exporta en crudo cinco bloques candidatos de mapa por mundo y sus subarchivos Mini con offsets validados, junto con un manifiesto, el mapa padre de `zonedata`, propiedades de movimiento de `mapGR`, entradas interpretadas de la matriz `MM` e instrucciones hexadecimales de scripts preservando bytes no interpretados.

## Estado del port

OWSE muestra el inventario detallado de `ES` tipo 0: cada descriptor incluye su offset absoluto, las tres palabras raw y el tamaño/límite del payload; los tipos 0–3 añaden los conteos y longitudes que sí están confirmados. Esta vista es de solo lectura y no interpreta esos campos como coordenadas del juego.

La vista OWSE Gen. VI también genera una previsualización PNG de la matriz `MM` a partir de las entradas Mini `GR` referenciadas, cuando el dump permite interpretarlas; incluye una capa visual opcional para muebles, NPC, warps, triggers y el destino FlyTo. La exportación por lotes guarda como PNG todos los mapas interpretables y genera un manifiesto con las zonas omitidas y sus diagnósticos. También exporta un JSON por zona Gen. VI con metadatos, entidades interpretadas y hex crudo de cada registro para conservar los campos desconocidos; los registros sin semántica confirmada se identifican solo como hex. Además genera informes JSON por cada script de zona y de mapa, con la cabecera, instrucciones separadas entre script y movimientos, líneas interpretadas, representación hexadecimal y diagnóstico; el manifiesto registra los scripts que no se pudieron extraer. Si faltan contenedores o el formato no coincide, conserva el diagnóstico sin fabricar datos.

OWSE Gen. VII también exporta en lote un JSON por cada script `ZS`/`ZI`, con la cabecera, offsets, instrucciones de script y movimientos separados, líneas interpretadas, hex y bytes originales. El manifiesto registra los grupos que no se pudieron leer y cualquier archivo final de `encdata` que no complete un grupo de mundo.

El editor de encuentros Gen. VI puede exportar informes JSON de todas las áreas XY u ORAS. Cada tabla válida conserva sus grupos y slots interpretados; el manifiesto identifica archivos vacíos, tablas sin encuentros y áreas con encabezados u offsets truncados. La salida se escribe en una carpeta nueva y no modifica `encdata`.

En Gen. VII, el informe masivo genera un JSON por área con sus pares de tablas día/noche, niveles, probabilidades, slots SOS y clima. Cada tabla incluye hex crudo para preservar bytes que el editor no interpreta; el manifiesto registra áreas sin tablas, áreas dañadas y archivos finales de `encdata` que no completan un grupo.

El editor Gen. VII también permite copiar especie y forma de los diez slots regulares de Día o Noche a sus siete grupos SOS. La acción deja intactos los niveles, las probabilidades y los slots de clima.

Al volver a empaquetar un Mini/BinLinker, se puede indicar el archivo original como plantilla para conservar cabeceras retail con padding no estándar; el autodetectado de carpetas también lo busca junto a la carpeta `_XX`.

Los metadatos `ZoneData` de OWSE Gen. VI incluyen también BGM estacionales, flags de movimiento, cámara y coordenadas de entrada/salida; los campos opcionales se exportan manteniendo los bits no expuestos.

Para cargar CRO modificados en consola hace falta eludir la verificación RSA del módulo RO mediante firmware personalizado. La interfaz avisa este requisito antes de los editores CRO; Windows también exige el paso externo y no lo instala, así que no es una funcionalidad pendiente del port. La extracción asíncrona con progreso de CXI/3DS/CIA desencriptados, el empaquetado de RomFS/ExeFS desde workspaces y el desempaquetado/empaquetado standalone de RomFS y ExeFS, la reconstrucción `.3ds`, la conversión `.cia` mediante `makerom`, la generación del contenido de parches de redirección, la conversión PNG↔BCLIM, la exportación BFLIM, la inspección, edición, importación, exportación y restauración protegida de backups SMDH de `icon.bin` —incluidos los ajustes conocidos de `ApplicationSettings`—, el procesamiento LZ11/BLZ, las herramientas GAR/GARC/Mini/ALYT/Shuffle ARC/DARC/SARC/FARC para variantes con nombres y rutas validadas, además del inventario, exportación raw/PNG y reemplazo a DARC de salida de Title Screen, ya están disponibles desde Herramientas de proyecto. Esa pantalla también puede verificar y reconstruir de forma independiente los hashes internos de los CRO y `RomFS/.crr/static.crr`, publicando solo los archivos modificados como parche LayeredFS. OWSE permite editar metadatos, entidades, propiedades de `mapGR` e instrucciones de scripts Gen. VI, y también instrucciones `ZS`/`ZI` de Gen. VII; en Gen. VII ahora muestra el inventario estructural detallado de los subbloques `ED`, identifica cada bloque, permite exportar el contenedor ED descomprimido, cada bloque y subentrada junto a un manifiesto, y expone un prefijo hexadecimal de las variantes aún no interpretadas; también permite editar/exportar el mapa padre, el área de encuentros asociada en `worlddata/WD` y las posiciones `EP`, `EM` tipos 1/3, `EI` tipo 10, `PR` tipos 203/204/364, `EB` tipos 2/11, `EG` tipo 8, `ES` tipo 4, `EA` tipos 5/6 y `ET` tipos 7/9; `ES` tipo 0 sigue en lectura raw, mientras los recursos candidatos de mapa Gen. VII sólo se inspeccionan y exportan en crudo (cinco slots contrastados en 336 mundos retail de Ultra Sun), sin interpretar geometría; la previsualización/edición 3D, el texto específico de mapa, la semántica editable del payload `ES` tipo 0, otras variantes ES y los demás campos de entidades siguen como investigación adicional fuera de la paridad con Windows, que no ofrece esas herramientas Gen. VII. Los exports ya recalculan el CRR cuando el dump lo incluye. El inventario de paridad y su estado real está en [MAC_PORT_ROADMAP.md](MAC_PORT_ROADMAP.md).

## Building

Requiere [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) y un compilador compatible con C# 14.

La interfaz nueva usa React + React Router y se compila desde `pk3DS.Mac.Web/frontend`. El shell conserva el workspace y los campos de los editores en el almacenamiento local del navegador; los archivos generados siguen siendo locales y se vuelven a comprobar desde el workspace. Las pantallas HTML anteriores permanecen disponibles bajo `/legacy` como puente de migración, pero ya no forman parte de la navegación principal.

La primera compilación necesita Node.js y npm. `make build`, `make run` y `run-mac.command` generan automáticamente `wwwroot/app`; después, el servidor abre la interfaz en `/app/`.

```sh
make build   # compila pk3DS.Mac.slnx
make frontend-build # compila solo React/Vite
make test    # corre la suite de tests
make run     # levanta el servidor en http://127.0.0.1:38473
```

Si querés fijar explícitamente un puerto local, podés usar por ejemplo `PK3DS_PORT=38474 make run`.

En macOS hay que usar `pk3DS.Mac.slnx`, no `pk3DS.slnx`: esta última incluye la app original de WinForms, que solo compila en Windows. El detalle está en [CLAUDE.md](CLAUDE.md).

## Créditos

Todo el trabajo de ingeniería inversa de los formatos de datos, la lógica de los editores y los randomizadores pertenece al proyecto original [pk3DS](https://github.com/kwsch/pk3DS) de kwsch y su comunidad de colaboradores. Este repositorio es un fork enfocado exclusivamente en llevar esa herramienta a macOS mediante una interfaz web local.
