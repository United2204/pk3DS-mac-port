using pk3DS.Core;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>
/// Shared editor for Gen VI Battle Maison and Gen VII Battle Tree/Royal data. Both generations
/// use the same two-file shape: trainer records contain a class and a list of Pokémon indexes,
/// while each Pokémon record is a fixed 16-byte structure.
/// </summary>
public static class MaisonEditor
{
    private const int PokemonRecordSize = 0x10;

    public static MaisonCatalogResponse GetCatalog(MaisonCatalogRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        EnsureSupportedGame(config);
        var variant = NormalizeVariant(request.Variant);
        var trainers = GetTrainerGarc(config, variant);
        var pokemon = GetPokemonGarc(config, variant);
        return new MaisonCatalogResponse(config.Version.ToString(), variant,
            NamedTrainers(config, variant, trainers.FileCount),
            NamedEntries(pokemon.FileCount, "Pokémon"),
            Catalogs.TrainerClasses(config), Catalogs.Species(config), Catalogs.Items(config),
            Catalogs.Moves(config), Natures(config));
    }

    public static MaisonTrainerResponse GetTrainer(MaisonTrainerRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        EnsureSupportedGame(config);
        var variant = NormalizeVariant(request.Variant);
        var trainers = GetTrainerGarc(config, variant);
        var index = RequireIndex(trainers.FileCount, request.TrainerIndex, "entrenador");
        return new MaisonTrainerResponse(variant, index, DescribeTrainer(config, trainers.Files[index]));
    }

    public static MaisonPokemonResponse GetPokemon(MaisonPokemonRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        EnsureSupportedGame(config);
        var variant = NormalizeVariant(request.Variant);
        var pokemon = GetPokemonGarc(config, variant);
        var index = RequireIndex(pokemon.FileCount, request.PokemonIndex, "Pokémon");
        return new MaisonPokemonResponse(variant, index, DescribePokemon(config, pokemon.Files[index]));
    }

    public static ExportResult ExportTrainer(MaisonTrainerExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "maison-trainer", [$"maisontr{NormalizeVariant(request.Variant)}", $"maisonpk{NormalizeVariant(request.Variant)}"], config =>
            {
                EnsureSupportedGame(config);
                var variant = NormalizeVariant(request.Variant);
                var garc = GetTrainerGarc(config, variant);
                var index = RequireIndex(garc.FileCount, request.TrainerIndex, "entrenador");
                var entry = ValidateTrainer(request.Entry, Catalogs.TrainerClasses(config).Length, GetPokemonGarc(config, variant).FileCount);
                var data = WriteTrainer(config, entry);
                garc.SetFile(index, data);
                garc.Save();
                return [config.GetGARCFileName($"maisontr{variant}")];
            });

    public static ExportResult ExportPokemon(MaisonPokemonExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "maison-pokemon", [$"maisonpk{NormalizeVariant(request.Variant)}"], config =>
            {
                EnsureSupportedGame(config);
                var variant = NormalizeVariant(request.Variant);
                var garc = GetPokemonGarc(config, variant);
                var index = RequireIndex(garc.FileCount, request.PokemonIndex, "Pokémon");
                var entry = ValidatePokemon(request.Entry, Catalogs.SpeciesCount(config), Catalogs.ItemCount(config), Catalogs.MoveCount(config));
                garc.SetFile(index, WritePokemon(config, entry, garc.Files[index]));
                garc.Save();
                return [config.GetGARCFileName($"maisonpk{variant}")];
            });

    internal static string NormalizeVariant(string? variant) => variant?.Trim().ToLowerInvariant() switch
    {
        "normal" or "n" or "tree" => "N",
        "special" or "s" or "super" or "royal" => "S",
        _ => throw new WorkspaceException("La variante debe ser Normal/Super o Tree/Royal."),
    };

    internal static MaisonTrainerEntry ValidateTrainer(MaisonTrainerEntry? entry, int classCount, int pokemonCount)
    {
        if (entry is null || entry.TrainerClass < 0 || entry.TrainerClass >= classCount
            || entry.Choices is null || entry.Choices.Length > ushort.MaxValue
            || entry.Choices.Any(choice => choice < 0 || choice >= pokemonCount))
            throw new WorkspaceException("Los datos del entrenador de Maison no son válidos.");
        return entry;
    }

    internal static MaisonPokemonEntry ValidatePokemon(MaisonPokemonEntry? entry, int speciesCount, int itemCount, int moveCount)
    {
        if (entry is null || entry.Species < 0 || entry.Species >= speciesCount
            || entry.Form is < 0 or > ushort.MaxValue
            || entry.Nature is < 0 or >= 25
            || entry.Item < 0 || entry.Item >= itemCount
            || entry.Moves is not { Length: 4 } || entry.Moves.Any(move => move < 0 || move >= moveCount)
            || entry.EVs is not { Length: 6 })
            throw new WorkspaceException("Los datos del Pokémon de Maison no son válidos.");
        return entry;
    }

    private static GARCFile GetTrainerGarc(GameConfig config, string variant) => config.GetGARCData($"maisontr{variant}");
    private static GARCFile GetPokemonGarc(GameConfig config, string variant) => config.GetGARCData($"maisonpk{variant}");

    private static MaisonTrainerEntry DescribeTrainer(GameConfig config, byte[] data)
    {
        if (data.Length < 4)
            throw new WorkspaceException("El registro de entrenador de Maison está incompleto.");
        var count = BitConverter.ToUInt16(data, 2);
        if (4 + (count * sizeof(ushort)) > data.Length)
            throw new WorkspaceException("El registro de entrenador de Maison está truncado.");
        if (config.Generation == 6)
        {
            var trainer = new Maison6.Trainer(data);
            return DescribeTrainerData(trainer.Class, trainer.Count, trainer.Choices, data.Length);
        }
        else
        {
            var trainer = new Maison7.Trainer(data);
            return DescribeTrainerData(trainer.Class, trainer.Count, trainer.Choices, data.Length);
        }
    }

    private static MaisonPokemonEntry DescribePokemon(GameConfig config, byte[] data)
    {
        if (data.Length < PokemonRecordSize)
            throw new WorkspaceException("El registro de Pokémon de Maison está incompleto.");
        if (config.Generation == 6)
        {
            var pokemon = new Maison6.Pokemon(data);
            return DescribePokemonData(pokemon.Species, pokemon.Form, pokemon.Nature, pokemon.Item, pokemon.Moves, pokemon.EVs);
        }
        else
        {
            var pokemon = new Maison7.Pokemon(data);
            return DescribePokemonData(pokemon.Species, pokemon.Form, pokemon.Nature, pokemon.Item, pokemon.Moves, pokemon.EVs);
        }
    }

    private static MaisonTrainerEntry DescribeTrainerData(ushort trainerClass, ushort count, ushort[] choices, int dataLength)
    {
        if (4 + (count * sizeof(ushort)) > dataLength)
            throw new WorkspaceException("El registro de entrenador de Maison está truncado.");
        return new MaisonTrainerEntry(trainerClass, choices.Select(choice => (int)choice).ToArray());
    }

    private static MaisonPokemonEntry DescribePokemonData(ushort species, ushort form, byte nature, ushort item, ushort[] moves, bool[] evs) =>
        new(species, form, nature, item, moves.Select(move => (int)move).ToArray(), evs.ToArray());

    private static byte[] WriteTrainer(GameConfig config, MaisonTrainerEntry entry)
    {
        var choices = entry.Choices.OrderBy(choice => choice).Select(choice => (ushort)choice).ToArray();
        if (choices.Length > ushort.MaxValue)
            throw new WorkspaceException("La lista de Pokémon del entrenador es demasiado larga.");
        if (config.Generation == 6)
        {
            var trainer = new Maison6.Trainer { Class = (ushort)entry.TrainerClass, Count = (ushort)choices.Length, Choices = choices };
            return trainer.Write();
        }
        else
        {
            var trainer = new Maison7.Trainer { Class = (ushort)entry.TrainerClass, Count = (ushort)choices.Length, Choices = choices };
            return trainer.Write();
        }
    }

    private static byte[] WritePokemon(GameConfig config, MaisonPokemonEntry entry, byte[] original)
    {
        if (config.Generation == 6)
        {
            var pokemon = new Maison6.Pokemon(original) { Species = (ushort)entry.Species, Form = (ushort)entry.Form, Nature = (byte)entry.Nature, Item = (ushort)entry.Item };
            ApplyPokemon(pokemon, entry);
            return pokemon.Write();
        }
        else
        {
            var pokemon = new Maison7.Pokemon(original) { Species = (ushort)entry.Species, Form = (ushort)entry.Form, Nature = (byte)entry.Nature, Item = (ushort)entry.Item };
            ApplyPokemon(pokemon, entry);
            return pokemon.Write();
        }
    }

    private static void ApplyPokemon(Maison6.Pokemon pokemon, MaisonPokemonEntry entry)
    {
        for (var i = 0; i < 4; i++) pokemon.Moves[i] = (ushort)entry.Moves[i];
        for (var i = 0; i < 6; i++) pokemon.EVs[i] = entry.EVs[i];
    }

    private static void ApplyPokemon(Maison7.Pokemon pokemon, MaisonPokemonEntry entry)
    {
        for (var i = 0; i < 4; i++) pokemon.Moves[i] = (ushort)entry.Moves[i];
        for (var i = 0; i < 6; i++) pokemon.EVs[i] = entry.EVs[i];
    }

    private static NamedEntry[] NamedTrainers(GameConfig config, string variant, int count)
    {
        var textName = config.Generation == 6
            ? (variant == "S" ? TextName.SuperTrainerNames : TextName.MaisonTrainerNames)
            : (variant == "S" ? TextName.BattleRoyalNames : TextName.BattleTreeNames);
        var names = config.GetText(textName);
        return Enumerable.Range(0, count).Select(index => new NamedEntry(index,
            index < names.Length && !string.IsNullOrWhiteSpace(names[index]) ? names[index] : $"Entrenador {index}" )).ToArray();
    }

    private static NamedEntry[] Natures(GameConfig config)
    {
        var names = config.GetText(TextName.Natures);
        return Enumerable.Range(0, 25).Select(index => new NamedEntry(index,
            index < names.Length && !string.IsNullOrWhiteSpace(names[index]) ? names[index] : $"Naturaleza {index}" )).ToArray();
    }

    private static NamedEntry[] NamedEntries(int count, string fallback) =>
        Enumerable.Range(0, count).Select(index => new NamedEntry(index, $"{fallback} {index}" )).ToArray();

    private static int RequireIndex(int count, int index, string label) =>
        index >= 0 && index < count ? index : throw new WorkspaceException($"El {label} indicado no existe en Maison.");

    private static void EnsureSupportedGame(GameConfig config)
    {
        if (config.Version is not (GameVersion.XY or GameVersion.ORAS or GameVersion.SM or GameVersion.USUM))
            throw new WorkspaceException("Este editor solo está disponible para juegos de Gen. VI y VII.");
    }
}
