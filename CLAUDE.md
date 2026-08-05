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
   - Para archivos sueltos que no son GARC (como `DllField.cro`) está `ExportLooseFiles`.
4. Una línea en `pk3DS.Mac.Web/Endpoints.cs`.
5. Página y script en `wwwroot/`.

## Errores

Los editores lanzan `WorkspaceException` con un mensaje **en español, dirigido al usuario**;
`WorkspaceExceptionMiddleware` lo convierte en 400 con ese texto. Cualquier otra excepción es un
bug: se registra con stack trace y el usuario recibe un mensaje genérico. Los endpoints no llevan
`try/catch`.

## Invariantes de datos

- **El dump de origen nunca se modifica.** Se copia a un RomFS temporal, se muta la copia y se
  empaqueta solo lo que cambió.
- La salida es `luma/titles/<TITLE-ID>/romfs` dentro de un ZIP. No se reconstruyen `.cia` ni `.cxi`.
- El Title ID sale de `exheader.bin`; sin ese archivo no se puede exportar.
- Las rutas relativas que llegan en un request pasan por `EditorSession.GetChildPath`, que rechaza
  cualquier cosa que se escape del root. Es un límite de confianza: no lo puentees.

## Tests

Sin un dump real no se pueden probar los caminos que abren GARCs. Lo que sí está cubierto y hay
que mantener cubierto: empaquetado de bytes (los slots de encuentros de Gen VI), offsets
hard-codeados, guardas de validación y resolución de rutas. Son los lugares donde un error
corrompe un archivo en silencio en vez de fallar.

## Estado del port

`MAC_PORT_ROADMAP.md` tiene el inventario de paridad y el WBS. **Si agregás o completás un
módulo, actualizalo en el mismo commit**: ya se desincronizó una vez.
