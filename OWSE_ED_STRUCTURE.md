# OWSE Gen. VII: perfiles estructurales de ED

El inventario de OWSE lee el contenedor `ED` descomprimido de `encdata` y publica, por cada subentrada, la cabecera `count`/`kind`, el tamaño, un prefijo hexadecimal y el perfil estructural que se pudo confirmar.

| Bloque y tipo | Perfil | Edición desde la web |
| --- | --- | --- |
| `EP` | stride `0x3C`, posición en `0x08` | Sí |
| `EM` tipo `1` | stride `0x78`, posición en `0x08` | Sí |
| `EM` tipo `3` | Tabla anidada de longitud variable; no confirmada | No, diagnóstico/raw |
| `EB` tipo `2` | stride `0x3C`, posición en `0x08` | Sí |
| `ES` tipo `4` | stride retail `0x38`, posición en `0x08` | Sí |
| `ES` corto u otro tipo | No confirmado | No, diagnóstico/raw |
| `EA` tipo `5` | stride `0x3C`, posición en `0x08` | Sí |
| `EA` tipo `6` | descriptores con punteros a payloads de `0x30`, posición en `payload + 0x08` | Sí |
| `ET` tipo `7` | stride `0x54`, posición en `0x08` | Sí |
| `ET` tipo `9` | tabla variable de descriptores; cada tabla de puntos comienza con cabecera de `0x08` y contiene triples XYZ de `0x0C` | Sí |
| `EI` tipo `10` | contador seguido de registros de stride `0x5C`; `kind=10` en el primer registro y posición en `0x08` de cada registro | Sí |
| `PR` tipos `203`/`204` | un registro por entrada, posición XYZ fija en `0x08` y payload variable posterior preservado | Sí |
| `FS` tipo `12` | grupos internos variables dentro de contenedores cuyo tamaño superficial sigue `4 + count × 0x90`; no hay un offset XYZ único | No, diagnóstico/raw |
| `FS` tipo `13` | payload variable entre zonas y cantidades; no confirmado | No, diagnóstico/raw |

Los demás bloques (`EG`, `PR` tipo `364` y variantes futuras) se conservan intactos y pueden exportarse con **Exportar ED crudo**. No se habilita edición por similitud de tamaño: para pasar una variante a editable hay que confirmar su cabecera, stride, offset y round-trip contra datos reales.

La referencia de los campos confirmados vive en `OverworldEditor.cs`, y los tests sintéticos cubren que el perfil detectado coincida con el stride/offset usado por el exportador.
