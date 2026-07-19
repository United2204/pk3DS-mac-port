# pk3DS Mac Web

Interfaz local para macOS basada en `pk3DS.Core`. Arranca en `http://127.0.0.1:38473` y abre el navegador predeterminado. El servidor sólo escucha en tu Mac: ningún archivo se sube a internet.

## Uso

1. Extraé una copia propia y desencriptada del juego hasta tener una carpeta `RomFS` completa (debe contener `a`) y conservá el archivo `exheader.bin` junto a esa carpeta.
2. Abrí `run-mac.command` con doble clic. Si macOS bloquea el archivo, usá clic derecho → **Abrir** la primera vez.
3. Pulsá **Examinar…** y elegí la carpeta extraída del juego. pk3DS detectará el juego y el Title ID desde `exheader.bin`.
4. Marcá las opciones que quieras aplicar; al pasar el mouse sobre una de ellas se muestra una explicación breve.
5. Pulsá **Exportar** y elegí la carpeta donde guardar el ZIP.
6. Descomprimí el ZIP resultante en la raíz de la SD de la consola. Activa *Enable game patching* en Luma y usá la actualización del juego que corresponda a tu dump.

La salida inicial genera el árbol `luma/titles/<title-id>/romfs`. No reconstruye `.cia` ni `.cxi`, ni altera el RomFS de origen.

## Opciones activas hoy

- habilidades, objetos llevados, ratio de captura, tipos, grupos huevo y estadísticas base;
- compatibilidad de MT/MO y tutores;
- learnsets configurables: cantidad, distribución por nivel, STAB, potencia, cuatro movimientos iniciales y exclusión de daño fijo;
- movimientos huevo configurables;
- acciones globales de Move Stats: tipos, categorías físico/especial y modo Metronome;
- evoluciones: conservar, randomizar resultados con filtros de BST/EXP/tipo, eliminar intercambios o modo de evolución por nivel.
- editor de texto de juego e historia: selección de tabla y línea, búsqueda y exportación LayeredFS.

Los adaptadores de encuentros, entrenadores, iniciales, estáticos, tiendas y movesets de entrenadores todavía están en desarrollo. El inventario de paridad y su estado real están en `MAC_PORT_ROADMAP.md`.

El Title ID se obtiene automáticamente desde `exheader.bin`, por lo que la carpeta que elijas debe incluirlo. Si solo disponés de `RomFS`, la herramienta puede revisarlo, pero no podrá crear un LayeredFS con el Title ID correcto.
