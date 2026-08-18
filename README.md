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

Interfaz local para macOS basada en `pk3DS.Core`. Arranca en `http://127.0.0.1:38473` y abre el navegador predeterminado. El servidor sólo escucha en tu Mac: ningún archivo se sube a internet.

## Uso

1. Extraé una copia propia y desencriptada del juego hasta tener una carpeta `RomFS` completa (debe contener `a`) y conservá el archivo `exheader.bin` junto a esa carpeta.
2. Abrí `run-mac.command` con doble clic. Si macOS bloquea el archivo, usá clic derecho → **Abrir** la primera vez.
3. Pulsá **Examinar…** y elegí la carpeta extraída del juego. pk3DS detectará el juego y el Title ID desde `exheader.bin`.
4. Marcá las opciones que quieras aplicar; al pasar el mouse sobre una de ellas se muestra una explicación breve.
5. Pulsá **Exportar** y elegí la carpeta donde guardar el ZIP.
6. Descomprimí el ZIP resultante en la raíz de la SD de la consola. Activa *Enable game patching* en Luma y usá la actualización del juego que corresponda a tu dump.

La salida inicial genera el árbol `luma/titles/<title-id>/romfs`. El randomizador no reconstruye `.cia` ni `.cxi`, ni altera el RomFS de origen.

La pantalla **Herramientas de proyecto** permite seleccionar un workspace extraído y construir `romfs.bin`, `exefs.bin` o ambos en una carpeta de salida separada. Esta operación tampoco modifica el origen.

La misma pantalla puede extraer archivos `.cxi` y `.3ds` a un workspace nuevo con `RomFS`, `ExeFS` y `exheader.bin`, y reconstruir una ROM `.3ds` recortada o con padding de tarjeta. La reconstrucción y la conversión a `.cia` mediante el `makerom` incluido fueron ejecutadas correctamente contra workspaces reales de Pokémon X y Omega Ruby; todavía falta validar la instalación y el arranque del CIA.

También puede crear el contenido de un parche de redirección: actualiza las rutas seleccionadas en `.code.bin` y copia los GARCs al árbol `a0/`. La creación nativa y firma del contenedor `.cia` sigue pendiente, pero la conversión completa está disponible con el `makerom` macOS arm64 incluido o con un ejecutable externo compatible.

La misma pantalla permite desempaquetar y empaquetar archivos GARC desde carpetas numeradas, incluyendo la opción de no intentar descomprimir entradas LZSS durante la extracción.

También admite DARC con la estructura habitual de una sola capa de carpetas. Los archivos originales y las carpetas de entrada se conservan intactos.

También admite SARC con rutas raíz y anidadas, nombres UTF-8 y alineación de datos configurable. El desempaquetado rechaza rutas inseguras y el empaquetado trabaja sobre una copia de la carpeta de entrada.

También puede desempaquetar y empaquetar la variante FARC heredada con índice SIR0, nombres UTF-16 y rutas anidadas. Las variantes FARC indexadas por hash siguen siendo de solo lectura.

En **Herramientas de proyecto** también podés analizar la pantalla de título de X/Y y OR/AS. La herramienta lista los DARCs por juego e idioma, muestra una vista previa PNG de los BCLIM compatibles —incluidos ETC1 y ETC1A4—, exporta los recursos originales junto con un `manifest.json` y genera PNG para formatos compatibles; también acepta un PNG o BCLIM del mismo tamaño, genera un DARC nuevo o una copia completa del GARC con el recurso reemplazado y puede aplicar el cambio directamente al workspace, siempre creando primero un backup en `.pk3ds-backups` y conservando la compresión LZSS de OR/AS. Las salidas de revisión no modifican el workspace.

Para crear un `.cia`, la aplicación incluye `makerom` macOS arm64 y permite indicar otro ejecutable si se desea. Primero reconstruye una `.3ds` temporal y solo publica el CIA si la conversión termina correctamente; usa `-ignoresign` porque el NCCH reconstruido ya no conserva firmas retail válidas. El resultado se genera sin cifrado de contenido y con el NCCH marcado `NoCrypto`, por lo que es el formato esperado por Azahar. La generación fue probada con dumps reales de X y ORAS y la lectura de vuelta del CIA de X fue correcta; todavía falta validar la instalación y el arranque.

El Title ID se obtiene automáticamente desde `exheader.bin`, por lo que la carpeta que elijas debe incluirlo. Si solo disponés de `RomFS`, la herramienta puede revisarlo, pero no podrá crear un LayeredFS con el Title ID correcto.

## Opciones activas hoy

- habilidades, objetos llevados, ratio de captura, tipos, grupos huevo y estadísticas base;
- compatibilidad de MT/MO y tutores;
- learnsets configurables: cantidad, distribución por nivel, STAB, potencia, cuatro movimientos iniciales y exclusión de daño fijo;
- movimientos huevo configurables;
- editor de objetos de Pickup de Gen. VII, incluyendo sus probabilidades por banda de nivel;
- editor de Battle Maison / Battle Tree / Battle Royal para los registros de entrenadores y Pokémon;
- acciones globales de Move Stats: tipos, categorías físico/especial y modo Metronome;
- evoluciones: conservar, randomizar resultados con filtros de BST/EXP/tipo, eliminar intercambios o modo de evolución por nivel.

Además hay editores individuales para texto de juego e historia, movimientos por nivel, movimientos huevo, evoluciones, datos personales, movimientos, objetos, TMs/HMs (Gen. VI y VII), Move Tutors (Gen. VI y VII), Poké Mart (Gen. VI y VII), O-Powers (Gen. VI), Shiny Rate (Gen. VI y VII), Type Chart (Gen. VI y VII), Starter Pokémon (Gen. VI), Gift Pokémon (Gen. VI), Pickup (Gen. VI y VII), Battle Maison / Tree / Royal, megaevoluciones, encuentros salvajes (Gen. VI y VII), encuentros estáticos (Gen. VI y VII) y entrenadores (Gen. VI y VII). La página **OWSE / Scripts** inspecciona `ZO` de Gen. VI y `ZS`/`ZI` de Gen. VII, y permite editar/exportar metadatos, entidades de zona Gen. VI (muebles, NPC, warps y triggers), el inventario estructural `ED` con tamaños y cabeceras de subentradas, las posiciones `EP`, `EM` principal, `EB` tipo 2, `ES` tipo 4, `EA` tipo 5 y `ET` tipo 7 de entidades Gen. VII, el mapa padre de `zonedata`, propiedades de movimiento de `mapGR` e instrucciones hexadecimales de scripts preservando bytes no interpretados.

## Estado del port

Sigue pendiente el parche RO/RSA del sistema necesario para que algunos CRO modificados funcionen directamente en consola. La extracción de CXI/3DS, el empaquetado standalone de RomFS/ExeFS, la reconstrucción `.3ds`, la conversión `.cia` mediante `makerom`, la generación del contenido de parches de redirección, las herramientas GARC/DARC/SARC/FARC para la variante SIR0 con nombres y rutas validadas, además del inventario, exportación raw/PNG y reemplazo a DARC de salida de Title Screen, ya están disponibles desde Herramientas de proyecto. OWSE permite editar metadatos, entidades, propiedades de `mapGR` e instrucciones de scripts Gen. VI, y también instrucciones `ZS`/`ZI` de Gen. VII; en Gen. VII ahora muestra el inventario estructural detallado de los subbloques `ED` y permite editar/exportar las posiciones `EP`, `EM` principal, `EB` tipo 2, `ES` tipo 4, `EA` tipo 5 y `ET` tipo 7, mientras los mapas 3D, el texto de alto nivel y los demás campos de esas entidades siguen en modo inspección. Los exports ya recalculan el CRR cuando el dump lo incluye. El inventario de paridad y su estado real está en [MAC_PORT_ROADMAP.md](MAC_PORT_ROADMAP.md).

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

En macOS hay que usar `pk3DS.Mac.slnx`, no `pk3DS.slnx`: esta última incluye la app original de WinForms, que solo compila en Windows. El detalle está en [CLAUDE.md](CLAUDE.md).

## Créditos

Todo el trabajo de ingeniería inversa de los formatos de datos, la lógica de los editores y los randomizadores pertenece al proyecto original [pk3DS](https://github.com/kwsch/pk3DS) de kwsch y su comunidad de colaboradores. Este repositorio es un fork enfocado exclusivamente en llevar esa herramienta a macOS mediante una interfaz web local.
