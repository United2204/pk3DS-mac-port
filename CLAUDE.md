# pk3DS Mac Port

Port a macOS de [pk3DS](https://github.com/kwsch/pk3DS) (editor/randomizador de ROMs de Pokémon
3DS). La versión original es WinForms; este repositorio reusa su núcleo y lo expone mediante una
interfaz web local.

## Proyectos

| Proyecto | Qué es | Depende de |
| --- | --- | --- |
| `pk3DS.Core` | Núcleo heredado de upstream: formatos GARC, estructuras y randomizadores. **No modificar salvo para adaptar a otra plataforma.** | — |
| `pk3DS.Editors` | Lógica de editores y randomizado del port. Agnóstica de plataforma. | `pk3DS.Core` |
| `pk3DS.Mac.Web` | Host: servidor ASP.NET local + frontend HTML/JS en `wwwroot`. | `pk3DS.Editors` |
| `pk3DS.Editors.Tests` | Tests de xUnit. | `pk3DS.Editors` |
| `pk3DS.WinForms` | App original de Windows. **No compila en macOS** (ver abajo). | `pk3DS.Core` |

## Comandos

```sh
make build     # dotnet build pk3DS.Mac.slnx
make test      # dotnet test
make run       # servidor en http://127.0.0.1:38473, abre el navegador
```

`PK3DS_NO_BROWSER=1` evita que `make run` abra el navegador (útil para pruebas con `curl`).
Para usuarios finales existe `run-mac.command`, que se abre con doble clic.

## Soluciones: por qué hay dos

- **`pk3DS.Mac.slnx`** — todo menos WinForms. **Es la que hay que usar en macOS.**
- **`pk3DS.slnx`** — incluye además `pk3DS.WinForms`. Solo compila en Windows.

`pk3DS.WinForms` requiere el target `net10.0-windows`, y `pk3DS.Core` solo lo genera cuando el SO
de compilación es Windows (ver el `Condition="'$(OS)' == 'Windows_NT'"` en `pk3DS.Core.csproj`).
En macOS, `pk3DS.Core` compila la variante headless que incluye `Platform/HeadlessWinForms.cs`,
con stubs de `ProgressBar` y `RichTextBox` que colisionan con los tipos reales de
`System.Windows.Forms`. No es un bug: WinForms es Windows y punto.

## Reglas de arquitectura

**`pk3DS.Editors` no puede depender de la plataforma ni del host.** Nada de ASP.NET, nada de
`osascript`, nada de rutas de macOS. Ese límite es lo que mantiene viable un host futuro para
Android/iOS: el frontend es HTML/JS contra una API JSON, así que llegar a móvil es envolverlo en
un WebView reusando `Editors` y `Core` tal cual. La CI compila en Linux además de macOS
justamente para que ese límite no se rompa sin que nos enteremos.

Lo específico de plataforma va detrás de una interfaz: hoy solo `IFolderPicker`, implementado por
`MacFolderPicker` (osascript) en el host.

## Cómo se agrega un editor

1. Una clase en `pk3DS.Editors/Editors/`, estática, con `GetX` / `Export`.
2. Los DTO en `pk3DS.Editors/Contracts/EditorContracts.cs`.
3. **Toda exportación pasa por `EditorSession.Export`**, que abre el workspace, resuelve el Title
   ID, arma un RomFS temporal, copia los GARCs, ejecuta la mutación y empaqueta el ZIP LayeredFS.
   Solo hay que aportar la mutación y declarar los GARCs extra que el editor lee.
   - **El argumento `extraGarcs` importa**: si el editor lee un GARC que no está en
     `EditorSession.RequiredGarcs`, hay que declararlo o el archivo no existirá en el RomFS
     temporal. Item Stats y Mega Evolutions estaban rotos exactamente por esto.
   - En Gen. VII, `encdata` se copia siempre aunque el editor no lo toque: `GameConfig.GetGameData`
     le hace `stat` para elegir entre la tabla GARC de Sun y la de Moon, así que sin él
     `Initialize` falla sobre el RomFS temporal. Esto rompía **todas** las exportaciones de Gen. VII.
   - Para archivos sueltos que no son GARC (como `DllField.cro`) está `ExportLooseFiles`.
4. **Para escribir en un GARC usá `garc.SetFile(index, data)` o `garc.PatchFile(...)`, nunca
   `garc.Files[i] = data`.** El getter de `MemGARC.Files` reconstruye el array entero y copia cada
   entrada en cada lectura, así que indexar ese resultado escribe en un array temporal: la edición
   se descarta en silencio y el ZIP sale con el archivo original. Esto tenía rotos a los ocho
   editores individuales; el randomizador se salvaba sólo porque `GarcWriter` ya tomaba el array
   una vez y lo reasignaba.
5. Una línea en `pk3DS.Mac.Web/Endpoints.cs`.
6. Página y script en `wwwroot/`.

Las herramientas de proyecto siguen el mismo límite: `ProjectTools.ExtractProject` extrae CXI/3DS
a un workspace nuevo, `ProjectTools.BuildFileSystems` construye `romfs.bin` y/o `exefs.bin` en una
carpeta de salida separada, y `ProjectTools.CreateRedirectPatch` genera el `.code.bin` y el árbol
`a0/` del parche de redirección sin tocar el origen. La operación usa el empaquetador legacy bajo un
lock porque `RomFS.BuildRomFS` mantiene estado estático; nunca debe escribir dentro de `RomFS` o
`ExeFS` de origen. `CreateRedirectPatch` no debe presentarse como creador de `.cia`: todavía no
existe un ensamblador local de TMD/ticket/certificados y firma interna; `RebuildCia` solo puede
usar un `makerom` externo indicado por el usuario o colocado junto a la aplicación. `ProjectTools.PackGarc` debe copiar
la carpeta de entrada a una staging antes de llamar a `GARC.PackGARC`, porque el empaquetador legacy
puede comprimir y renombrar entradas `dec_` durante el proceso. `ProjectTools.PackDarc` y
`UnpackDarc` admiten solamente la estructura DARC de una capa; el núcleo heredado usa offsets
UTF-16 y la cabecera de datos debe quedar alineada después de toda la tabla de nombres. `ProjectTools.PackSarc`
y `UnpackSarc` usan `pk3DS.Core.CTR.SARC`, admiten archivos en la raíz y subcarpetas, codifican nombres
UTF-8, validan rutas antes de escribirlas y permiten elegir una alineación de datos potencia de dos.
`ProjectTools.UnpackFarc` usa el lector heredado de FARC, valida los offsets relativos y conserva los nombres
UTF-16; no hay que agregar un empaquetador hasta conocer los metadatos de variante que acompañan al formato.

`TitleScreenEditor` es intencionalmente headless: lee el GARC `titlescreen`, descomprime las
entradas LZSS de OR/AS, inspecciona los DARC esperados y exporta los payloads BCLIM y PNG compatibles
sin usar `System.Drawing`. `BCLIMPortable` cubre los formatos lineales y RGB5A1 con paleta, y
`PortablePng` permite el roundtrip RGBA; `TitleScreenEditor.Preview` genera una vista previa PNG
en memoria y `Replace` acepta PNG/BCLIM del mismo tamaño para escribir un DARC nuevo, mientras
`ReplaceGarc` genera una copia completa del GARC y mantiene LZSS en OR/AS, sin modificar el
workspace. ETC1 y la inserción persistente en el workspace quedan pendientes.

Los GARCs de Gen. VII que pueden contener entradas LZSS (como `pickup`) deben abrirse con
`GameConfig.GetlzGARCData`, asignar la entrada modificada y llamar a `Save()`. `GetGARCData` solo
entiende el contenido GARC sin esa compresión interna.

## Errores

Los editores lanzan `WorkspaceException` con un mensaje **en español, dirigido al usuario**;
`WorkspaceExceptionMiddleware` lo convierte en 400 con ese texto. Cualquier otra excepción es un
bug: se registra con stack trace y el usuario recibe un mensaje genérico. Los endpoints no llevan
`try/catch`.

## Invariantes de datos

- **El dump de origen nunca se modifica.** Se copia a un RomFS temporal, se muta la copia y se
  empaqueta solo lo que cambió.
- La salida de los editores es `luma/titles/<TITLE-ID>/romfs` o `exefs` dentro de un ZIP; no se reconstruyen `.cia` ni `.cxi` desde ese flujo. `ProjectTools` sí puede reconstruir un `.3ds` desde un workspace completo.
- El Title ID sale de `exheader.bin`; sin ese archivo no se puede exportar.
- Las rutas relativas que llegan en un request pasan por `EditorSession.GetChildPath`, que rechaza
  cualquier cosa que se escape del root. Es un límite de confianza: no lo puentees.

## Tests

`SyntheticXyWorkspace` y `SyntheticSunMoonWorkspace` arman en `TempPath` un workspace con GARCs
reales (empaquetados con `GARC.PackGARC`) y registros sintéticos. Eso permite probar los editores
de punta a punta —abrir, leer, exportar e inspeccionar el ZIP LayeredFS— sin un dump de varios GB.

Hay dos porque los formatos difieren de verdad: Gen. VII usa registros personales más grandes,
evoluciones de 8 bytes, movimientos empaquetados en un mini-archivo `WD` y egg moves con índice de
forma. La fixture también incluye un `code.bin` alineado con firmas sintéticas para probar TMs/HMs,
Pickup, Shiny Rate, O-Powers, tutores, tiendas y Type Chart, además de `DllBattle.cro` para Type Chart,
`DllField.cro` para Starter/Gift y un `Shop.cro` con tutores y tiendas Gen. VII. Los exports de CRO
recalculan hashes internos y `.crr/static.crr` sobre copias. `ProjectToolsTests` también verifica
que las salidas `romfs`/`exefs` se construyan sin modificar un dump de varios GB.

Al agregar un editor, sumale un caso a `EditorEndToEndTests.Exports` (o al equivalente de Gen. VII).
Ese test verifica que el ZIP contenga realmente el archivo que el editor dice haber cambiado, y es
lo que habría atrapado los bugs de Item Stats y Mega Evolutions. Comprobado: revertir cualquiera de
los dos fixes hace fallar exactamente los casos correspondientes.

El resto de la suite cubre empaquetado de bytes (slots de encuentros de Gen VI), round-trips de
cada estructura que escribe en la ROM, offsets hard-codeados, guardas de validación y resolución
de rutas. Son los lugares donde un error corrompe un archivo en silencio en vez de fallar.

## Estado del port

`MAC_PORT_ROADMAP.md` tiene el inventario de paridad y el WBS. **Si agregás o completás un
módulo, actualizalo en el mismo commit**: ya se desincronizó una vez.
