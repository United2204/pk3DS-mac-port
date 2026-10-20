using pk3DS.Core;
using pk3DS.Core.Randomizers;

namespace pk3DS.Editors;

/// <summary>
/// Randomizes the Gen. VI starter triplets and writes the same choices to both CRO tables.
/// </summary>
public static class StarterGen6Randomizer
{
    private const string PokeFile = "DllPoke3Select.cro";
    private const string FieldFile = "DllField.cro";

    private static readonly int[] XyGroupGenerations = [6, 1];
    private static readonly int[] OrasGroupGenerations = [3, 2, 4, 5];

    public static ExportResult Randomize(StarterRandomizeRequest request)
    {
        ValidateRequest(request);

        var workspace = GameWorkspace.Open(request.WorkspacePath);
        var sourceConfig = EditorSession.OpenReadOnly(workspace, request.Language);
        Guard.Gen6(sourceConfig);
        RequireFiles(workspace);

        return EditorSession.ExportLooseFiles(request.WorkspacePath, request.OutputDirectory, request.TitleId,
            request.Language, "starter6-randomize", [PokeFile, FieldFile], (_, config, scratchRomFs) =>
            {
                Guard.Gen6(config);
                var pokePath = Path.Combine(scratchRomFs, PokeFile);
                var fieldPath = Path.Combine(scratchRomFs, FieldFile);
                var poke = File.ReadAllBytes(pokePath);
                var field = File.ReadAllBytes(fieldPath);

                RandomizerService.WithRandomizerSeed(request.Seed, () =>
                {
                    var groups = StarterEditor.ReadGroups(poke, config);
                    RandomizeGroups(groups, config, request);
                    StarterEditor.ApplyGroups(poke, field, config, groups);
                    return 0;
                });

                File.WriteAllBytes(pokePath, poke);
                File.WriteAllBytes(fieldPath, field);
            });
    }

    private static void RandomizeGroups(StarterGroup[] groups, GameConfig config, StarterRandomizeRequest options)
    {
        var generations = config.ORAS ? OrasGroupGenerations : XyGroupGenerations;
        if (groups.Length != generations.Length)
            throw new WorkspaceException("La tabla de starters no coincide con el juego detectado.");

        var personal = config.Personal.Table;
        foreach (var group in groups)
        foreach (var species in group.Species)
        {
            if (species <= 0 || species >= personal.Length || personal[species].BST <= 0)
                throw new WorkspaceException("La tabla de starters contiene una especie original no válida.");
        }

        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var generation = generations[groupIndex];
            var originalSpecies = (int[])groups[groupIndex].Species.Clone();
            var speciesRandomizer = options.BasicStarter
                ? null
                : CreateSpeciesRandomizer(config, generation, options);
            var basicSpecies = options.KeepSameGen
                ? Legal.BasicStarters[generation - 1]
                : Legal.BasicStarters_6;
            if (options.BasicStarter && basicSpecies.Length == 0)
                throw new WorkspaceException("No hay especies iniciales válidas para el grupo seleccionado.");

            for (var starterIndex = 0; starterIndex < originalSpecies.Length; starterIndex++)
            {
                groups[groupIndex].Species[starterIndex] = options.BasicStarter
                    ? basicSpecies[RandomIndex(basicSpecies.Length)]
                    : speciesRandomizer!.GetRandomSpecies(originalSpecies[starterIndex]);
            }
        }
    }

    private static SpeciesRandomizer CreateSpeciesRandomizer(GameConfig config, int generation,
        StarterRandomizeRequest options)
    {
        var keepSameGen = options.KeepSameGen;
        var randomizer = new SpeciesRandomizer(config)
        {
            G1 = !keepSameGen || generation == 1,
            G2 = !keepSameGen || generation == 2,
            G3 = !keepSameGen || generation == 3,
            G4 = !keepSameGen || generation == 4,
            G5 = !keepSameGen || generation == 5,
            G6 = !keepSameGen || generation == 6,
            G7 = false,
            E = options.IncludeMythical,
            L = options.IncludeLegendary,
            Shedinja = false,
            rBST = true,
        };
        randomizer.Initialize();
        return randomizer;
    }

    private static int RandomIndex(int length) => (int)(Util.Random32() % (uint)length);

    private static void RequireFiles(GameWorkspace workspace)
    {
        foreach (var file in new[] { PokeFile, FieldFile })
        {
            if (!File.Exists(Path.Combine(workspace.RomFsPath, file)))
                throw new WorkspaceException($"Falta {file} en el RomFS. Es necesario para randomizar starters.");
        }
    }

    private static void ValidateRequest(StarterRandomizeRequest request)
    {
        if (request is null)
            throw new WorkspaceException("La solicitud para randomizar starters no es válida.");
    }
}
