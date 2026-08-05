using pk3DS.Core;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>
/// Trainers for Gen VII. A trainer is split across two GARCs: <c>trdata</c> for the battle setup
/// and <c>trpoke</c> for the team, and both must be written together.
/// </summary>
public static class TrainerEditor
{
    private static readonly string[] TrainerGarcs = ["trdata", "trpoke"];

    public static TrainerCatalogResponse GetCatalog(TrainerCatalogRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen7(config, "entrenadores");
        var trainers = config.GetGARCData("trdata");
        var names = config.GetText(TextName.TrainerNames);
        return new TrainerCatalogResponse(
            // Index 0 is the placeholder trainer the game never battles.
            trainers.Files.Select((_, index) => new TrainerSummary(index,
                index < names.Length && !string.IsNullOrWhiteSpace(names[index]) ? names[index] : $"Entrenador {index}")).Skip(1).ToArray(),
            Catalogs.TrainerClasses(config), Catalogs.Species(config), Catalogs.Items(config), Catalogs.Moves(config));
    }

    public static TrainerEntryResponse GetEntry(TrainerEntryRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        Guard.Gen7(config, "entrenadores");
        var trdata = config.GetGARCData("trdata");
        var trpoke = config.GetGARCData("trpoke");
        var index = RequireTrainer(trdata.Files.Length, trpoke.Files.Length, request.TrainerIndex);
        return new TrainerEntryResponse(index, Describe(new TrainerData7(trdata.Files[index], trpoke.Files[index])));
    }

    public static ExportResult Export(TrainerExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "trainer", TrainerGarcs, config =>
            {
                Guard.Gen7(config, "entrenadores");
                var trdata = config.GetGARCData("trdata");
                var trpoke = config.GetGARCData("trpoke");
                var index = RequireTrainer(trdata.Files.Length, trpoke.Files.Length, request.TrainerIndex);
                var entry = Validate(request.Entry, config.GetText(TextName.TrainerClasses).Length,
                    Catalogs.SpeciesCount(config), Catalogs.ItemCount(config), Catalogs.MoveCount(config));

                var trainer = new TrainerData7(trdata.Files[index], trpoke.Files[index]);
                Apply(trainer, entry);
                trainer.Write(out var data, out var team);
                trdata.SetFile(index, data);
                trpoke.SetFile(index, team);
                trdata.Save();
                trpoke.Save();
                return [config.GetGARCFileName("trdata"), config.GetGARCFileName("trpoke")];
            });

    private static TrainerEntry Describe(TrainerData7 trainer) => new(
        trainer.TrainerClass, (int)trainer.Mode,
        [trainer.Item1, trainer.Item2, trainer.Item3, trainer.Item4],
        trainer.AI, trainer.Flag, trainer.Money,
        trainer.Pokemon.Select(pokemon => new TrainerPokemonEntry(pokemon.Species, pokemon.Form, pokemon.Level, pokemon.Item,
            pokemon.Moves, pokemon.Ability, pokemon.Gender, pokemon.Nature, pokemon.Shiny, pokemon.IVs, pokemon.EVs)).ToArray());

    private static void Apply(TrainerData7 trainer, TrainerEntry entry)
    {
        trainer.TrainerClass = entry.TrainerClass;
        trainer.Mode = (BattleMode)entry.Mode;
        trainer.Item1 = entry.Items[0];
        trainer.Item2 = entry.Items[1];
        trainer.Item3 = entry.Items[2];
        trainer.Item4 = entry.Items[3];
        trainer.AI = entry.AI;
        trainer.Flag = entry.Flag;
        trainer.Money = entry.Money;

        // The team size is fixed by the existing record; only the slots already present are rewritten.
        for (var index = 0; index < trainer.Pokemon.Count; index++)
        {
            var source = entry.Team[index];
            var target = trainer.Pokemon[index];
            target.Species = source.Species;
            target.Form = source.Form;
            target.Level = source.Level;
            target.Item = source.Item;
            target.Moves = source.Moves;
            target.Ability = source.Ability;
            target.Gender = source.Gender;
            target.Nature = source.Nature;
            target.Shiny = source.Shiny;
            target.IVs = source.IVs;
            target.EVs = source.EVs;
        }
    }

    /// <summary>Returns the validated entry so callers get a non-null value to apply.</summary>
    internal static TrainerEntry Validate(TrainerEntry? entry, int classCount, int speciesCount, int itemCount, int moveCount)
    {
        if (entry is null
            || entry.TrainerClass < 0 || entry.TrainerClass >= classCount
            || entry.Mode is < 0 or > 2
            || entry.Items is not { Length: 4 }
            || entry.Items.Any(item => item < 0 || item >= itemCount)
            || entry.AI is < 0 or > byte.MaxValue
            || entry.Money is < 0 or > byte.MaxValue
            || entry.Team is null || entry.Team.Length is < 1 or > 6
            || entry.Team.Any(pokemon => IsOutOfRange(pokemon, speciesCount, itemCount, moveCount)))
            throw new WorkspaceException("Los datos del entrenador no son válidos.");
        return entry;
    }

    private static bool IsOutOfRange(TrainerPokemonEntry pokemon, int speciesCount, int itemCount, int moveCount) =>
        pokemon.Species < 0 || pokemon.Species >= speciesCount
        || pokemon.Form is < 0 or > byte.MaxValue
        || pokemon.Level is < 1 or > 100
        || pokemon.Item < 0 || pokemon.Item >= itemCount
        || pokemon.Moves is not { Length: 4 }
        || pokemon.Moves.Any(move => move < 0 || move >= moveCount)
        || pokemon.Ability is < 0 or > 3
        || pokemon.Gender is < 0 or > 3
        || pokemon.Nature is < 0 or > 25
        || pokemon.IVs is not { Length: 6 }
        || pokemon.IVs.Any(iv => iv is < 0 or > 31)
        || pokemon.EVs is not { Length: 6 }
        || pokemon.EVs.Any(ev => ev is < 0 or > byte.MaxValue);

    private static int RequireTrainer(int trdataCount, int trpokeCount, int trainerIndex) =>
        trainerIndex >= 0 && trainerIndex < trdataCount && trainerIndex < trpokeCount
            ? trainerIndex
            : throw new WorkspaceException("El entrenador indicado no existe.");
}
