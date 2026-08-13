using pk3DS.Core;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>
/// Trainers for Gen VI and Gen VII. A trainer is split across two GARCs: <c>trdata</c> for the
/// battle setup and <c>trpoke</c> for the team, and both must be written together.
/// </summary>
public static class TrainerEditor
{
    private static readonly string[] TrainerGarcs = ["trdata", "trpoke"];

    public static TrainerCatalogResponse GetCatalog(TrainerCatalogRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        EnsureSupported(config);
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
        EnsureSupported(config);
        var trdata = config.GetGARCData("trdata");
        var trpoke = config.GetGARCData("trpoke");
        var index = RequireTrainer(trdata.Files.Length, trpoke.Files.Length, request.TrainerIndex);
        var entry = config.Generation == 6
            ? Describe(ReadGen6(trdata.Files[index], trpoke.Files[index], config.ORAS))
            : Describe(new TrainerData7(trdata.Files[index], trpoke.Files[index]));
        entry = entry with
        {
            Name = TextValue(config, TextName.TrainerNames, index),
            ClassName = TextValue(config, TextName.TrainerClasses, entry.TrainerClass),
        };
        return new TrainerEntryResponse(index, entry);
    }

    public static ExportResult Export(TrainerExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "trainer", TrainerGarcs, config =>
            {
                EnsureSupported(config);
                var trdata = config.GetGARCData("trdata");
                var trpoke = config.GetGARCData("trpoke");
                var index = RequireTrainer(trdata.Files.Length, trpoke.Files.Length, request.TrainerIndex);
                var entry = Validate(request.Entry, config.GetText(TextName.TrainerClasses).Length,
                    Catalogs.SpeciesCount(config), Catalogs.ItemCount(config), Catalogs.MoveCount(config),
                    config.Generation == 6, config.ORAS ? 4 : 3);

                byte[] data;
                byte[] team;
                if (config.Generation == 6)
                {
                    var trainer = ReadGen6(trdata.Files[index], trpoke.Files[index], config.ORAS);
                    Apply(trainer, entry);
                    data = trainer.Write();
                    team = trainer.WriteTeam();
                }
                else
                {
                    var trainer = new TrainerData7(trdata.Files[index], trpoke.Files[index]);
                    Apply(trainer, entry);
                    trainer.Write(out data, out team);
                }
                trdata.SetFile(index, data);
                trpoke.SetFile(index, team);
                trdata.Save();
                trpoke.Save();

                var changed = new List<string>
                {
                    config.GetGARCFileName("trdata"),
                    config.GetGARCFileName("trpoke"),
                };
                if (entry.Name is not null || entry.ClassName is not null)
                {
                    WriteTrainerText(config, TextName.TrainerNames, index, entry.Name);
                    WriteTrainerText(config, TextName.TrainerClasses, entry.TrainerClass, entry.ClassName);
                    config.GARCGameText.Save();
                    changed.Add(config.GetGARCFileName("gametext"));
                }
                return changed;
            });

    private static TrainerEntry Describe(TrainerData7 trainer) => new(
        trainer.TrainerClass, (int)trainer.Mode,
        [trainer.Item1, trainer.Item2, trainer.Item3, trainer.Item4],
        trainer.AI, trainer.Flag, trainer.Money,
        trainer.Pokemon.Select(pokemon => new TrainerPokemonEntry(pokemon.Species, pokemon.Form, pokemon.Level, pokemon.Item,
            pokemon.Moves, pokemon.Ability, pokemon.Gender, pokemon.Nature, pokemon.Shiny, pokemon.IVs, pokemon.EVs)).ToArray());

    private static TrainerEntry Describe(TrainerData6 trainer) => new(
        trainer.Class, trainer.BattleType, trainer.Items.Select(item => (int)item).ToArray(),
        trainer.AI, trainer.Healer, trainer.Money,
        trainer.Team.Select(pokemon => new TrainerPokemonEntry(
            pokemon.Species, pokemon.Form, pokemon.Level, pokemon.Item,
            pokemon.Moves.Select(move => (int)move).ToArray(), pokemon.Ability, pokemon.Gender,
            Nature: 0, Shiny: false, IVs: [pokemon.IVs, 0, 0, 0, 0, 0], EVs: [0, 0, 0, 0, 0, 0])).ToArray(),
        HasItems: trainer.Item, HasMoves: trainer.Moves);

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

        trainer.NumPokemon = entry.Team.Length;
        while (trainer.Pokemon.Count < entry.Team.Length)
            trainer.Pokemon.Add(new TrainerPoke7());
        if (trainer.Pokemon.Count > entry.Team.Length)
            trainer.Pokemon.RemoveRange(entry.Team.Length, trainer.Pokemon.Count - entry.Team.Length);

        for (var index = 0; index < entry.Team.Length; index++)
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

    private static void Apply(TrainerData6 trainer, TrainerEntry entry)
    {
        trainer.Class = entry.TrainerClass;
        trainer.BattleType = (byte)entry.Mode;
        trainer.NumPokemon = (byte)entry.Team.Length;
        trainer.Items = entry.Items.Select(item => (ushort)item).ToArray();
        trainer.AI = (byte)entry.AI;
        trainer.Healer = entry.Flag;
        trainer.Money = (byte)entry.Money;
        trainer.Item = entry.HasItems ?? trainer.Item;
        trainer.Moves = entry.HasMoves ?? trainer.Moves;
        Array.Resize(ref trainer.Team, entry.Team.Length);

        for (var index = 0; index < entry.Team.Length; index++)
        {
            var source = entry.Team[index];
            var target = trainer.Team[index] ??= new TrainerData6.Pokemon(new byte[100], trainer.Item, trainer.Moves);
            target.Species = (ushort)source.Species;
            target.Form = (ushort)source.Form;
            target.Level = (ushort)source.Level;
            target.Item = (ushort)source.Item;
            target.Moves = source.Moves.Select(move => (ushort)move).ToArray();
            target.Ability = source.Ability;
            target.Gender = source.Gender;
            target.IVs = (byte)source.IVs[0];
        }
    }

    /// <summary>Returns the validated entry so callers get a non-null value to apply.</summary>
    internal static TrainerEntry Validate(TrainerEntry? entry, int classCount, int speciesCount, int itemCount, int moveCount,
        bool generation6 = false, int maxMode = 2)
    {
        if (entry is null
            || entry.TrainerClass < 0 || entry.TrainerClass >= classCount
            || entry.Mode < 0 || entry.Mode > maxMode
            || entry.Items is not { Length: 4 }
            || entry.Items.Any(item => item < 0 || item >= itemCount)
            || entry.AI is < 0 or > byte.MaxValue
            || entry.Money is < 0 or > byte.MaxValue
            || entry.Team is null || entry.Team.Length is < 1 or > 6
            || entry.Team.Any(pokemon => IsOutOfRange(pokemon, speciesCount, itemCount, moveCount, generation6)))
            throw new WorkspaceException("Los datos del entrenador no son válidos.");
        return entry;
    }

    private static bool IsOutOfRange(TrainerPokemonEntry pokemon, int speciesCount, int itemCount, int moveCount, bool generation6) =>
        pokemon.Species < 0 || pokemon.Species >= speciesCount
        || pokemon.Form < 0 || (generation6 ? pokemon.Form > ushort.MaxValue : pokemon.Form > byte.MaxValue)
        || pokemon.Level is < 1 or > 100
        || pokemon.Item < 0 || pokemon.Item >= itemCount
        || pokemon.Moves is not { Length: 4 }
        || pokemon.Moves.Any(move => move < 0 || move >= moveCount)
        || pokemon.Ability < 0 || pokemon.Ability > (generation6 ? 15 : 3)
        || pokemon.Gender < 0 || pokemon.Gender > (generation6 ? 7 : 3)
        || (!generation6 && pokemon.Nature is < 0 or > 25)
        || pokemon.IVs is not { Length: 6 }
        || pokemon.IVs.Any(iv => iv < 0 || iv > (generation6 ? byte.MaxValue : 31))
        || pokemon.EVs is not { Length: 6 }
        || pokemon.EVs.Any(ev => ev is < 0 or > byte.MaxValue);

    private static void EnsureSupported(GameConfig config)
    {
        if (config.Generation is not (6 or 7))
            throw new WorkspaceException("Este editor requiere un juego de Gen. VI o Gen. VII.");
    }

    private static string TextValue(GameConfig config, TextName table, int index)
    {
        var values = config.GetText(table);
        return index >= 0 && index < values.Length ? values[index] : string.Empty;
    }

    private static void WriteTrainerText(GameConfig config, TextName table, int index, string? value)
    {
        if (value is null)
            return;
        var reference = config.GameText.Single(text => text.Name == table);
        var text = new TextFile(config, config.GARCGameText.Files[reference.Index], remapChars: true);
        if (index < 0 || index >= text.Lines.Length)
            throw new WorkspaceException("El índice de texto del entrenador no existe.");
        var lines = text.Lines;
        lines[index] = value;
        text.Lines = lines;
        config.GARCGameText.SetFile(reference.Index, text.Data);
    }

    private static TrainerData6 ReadGen6(byte[] trdata, byte[] trpoke, bool oras)
    {
        try
        {
            return new TrainerData6(trdata, trpoke, oras);
        }
        catch (Exception ex) when (ex is ArgumentException or EndOfStreamException or IndexOutOfRangeException or DivideByZeroException)
        {
            throw new WorkspaceException("El registro de entrenador de Gen. VI está incompleto o corrupto.");
        }
    }

    private static int RequireTrainer(int trdataCount, int trpokeCount, int trainerIndex) =>
        trainerIndex >= 0 && trainerIndex < trdataCount && trainerIndex < trpokeCount
            ? trainerIndex
            : throw new WorkspaceException("El entrenador indicado no existe.");
}
