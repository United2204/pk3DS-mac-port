# pk3DS Mac Web

Interfaz local para macOS basada en `pk3DS.Core`. Arranca en `http://127.0.0.1:38473` y abre el navegador predeterminado. El servidor sólo escucha en tu Mac: ningún archivo se sube a internet.

## Uso

1. Extraé una copia propia y desencriptada del juego hasta tener una carpeta `RomFS` completa (debe contener `a`).
2. Abrí `run-mac.command` con doble clic. Si macOS bloquea el archivo, usá clic derecho → **Abrir** la primera vez.
3. Pegá la ruta de `RomFS`, comprobala, elegí las opciones y pulsá **Crear LayeredFS**.
4. Descomprimí el ZIP resultante en la raíz de la SD de la consola. Activa *Enable game patching* en Luma y usá la actualización del juego que corresponda a tu dump.

La salida inicial genera el árbol `luma/titles/<title-id>/romfs`. No reconstruye `.cia` ni `.cxi`, ni altera el RomFS de origen.

## Opciones activas hoy

- habilidades, objetos llevados, ratio de captura, tipos, grupos huevo y estadísticas base;
- compatibilidad de MT/MO y tutores;
- learnsets configurables: cantidad, distribución por nivel, STAB, potencia, cuatro movimientos iniciales y exclusión de daño fijo;
- evoluciones: conservar, randomizar resultados con filtros de BST/EXP/tipo, eliminar intercambios o modo de evolución por nivel.

Los adaptadores de encuentros, entrenadores, iniciales, estáticos, tiendas y movesets de entrenadores están deliberadamente fuera de esta primera iteración: la interfaz no los muestra como activos ni produce una salida parcial sin avisar.

El Title ID predeterminado `000400000011C400` es el de Omega Ruby USA. Cambialo si tu dump usa otra región.
