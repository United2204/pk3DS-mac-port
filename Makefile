# Build tasks for the macOS port. Requires the .NET 10 SDK.
#
# pk3DS.Mac.slnx deliberately excludes pk3DS.WinForms: that project needs the net10.0-windows
# target, which pk3DS.Core only produces when building on Windows.

SOLUTION = pk3DS.Mac.slnx
WEB      = pk3DS.Mac.Web/pk3DS.Mac.Web.csproj
TESTS    = pk3DS.Editors.Tests/pk3DS.Editors.Tests.csproj
CONFIG   = Debug

.PHONY: build test run publish clean

build:
	dotnet build $(SOLUTION) -c $(CONFIG)

test:
	dotnet test $(TESTS) -c $(CONFIG)

# Serves on http://127.0.0.1:38473 and opens the default browser.
# Set PK3DS_NO_BROWSER=1 to skip opening it.
run:
	dotnet run --project $(WEB) -c $(CONFIG)

publish:
	dotnet publish $(WEB) -c Release -o publish

clean:
	dotnet clean $(SOLUTION) -c $(CONFIG)
	rm -rf publish
	rm -rf pk3DS.*/bin pk3DS.*/obj
