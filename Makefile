# Build tasks for the macOS port. Requires the .NET 10 SDK.
#
# pk3DS.Mac.slnx deliberately excludes pk3DS.WinForms: that project needs the net10.0-windows
# target, which pk3DS.Core only produces when building on Windows.

SOLUTION = pk3DS.Mac.slnx
WEB      = pk3DS.Mac.Web/pk3DS.Mac.Web.csproj
TESTS    = pk3DS.Editors.Tests/pk3DS.Editors.Tests.csproj
CONFIG   = Debug

.PHONY: build frontend-build test run publish clean

FRONTEND = pk3DS.Mac.Web/frontend

frontend-build:
	cd $(FRONTEND) && if [ ! -d node_modules ]; then npm install; fi && npm run build

build:
	$(MAKE) frontend-build
	dotnet build $(SOLUTION) -c $(CONFIG) -p:BuildInParallel=false

test:
	dotnet test $(TESTS) -c $(CONFIG)

# Serves on http://127.0.0.1:38473 when available and opens the default browser.
# If that port is busy, the host chooses a nearby free port. Set PK3DS_NO_BROWSER=1 to skip
# opening the browser or PK3DS_PORT to choose an explicit local port.
run:
	$(MAKE) frontend-build
	dotnet run --project $(WEB) -c $(CONFIG) -p:BuildInParallel=false

publish:
	$(MAKE) frontend-build
	dotnet publish $(WEB) -c Release -o publish -p:BuildInParallel=false

clean:
	dotnet clean $(SOLUTION) -c $(CONFIG)
	rm -rf publish
	rm -rf pk3DS.*/bin pk3DS.*/obj
