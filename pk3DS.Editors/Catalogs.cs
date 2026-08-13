using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>
/// Builds the id/name lists the editors hand to the UI. Names come from the game's own text
/// archives, so a missing or blank entry falls back to a numbered placeholder rather than an
/// empty dropdown row.
/// </summary>
internal static class Catalogs
{
    public static NamedEntry[] Species(GameConfig config) => Named(config.GetText(TextName.SpeciesNames), "Especie");
    public static NamedEntry[] Items(GameConfig config) => Named(config.GetText(TextName.ItemNames), "Objeto");
    public static NamedEntry[] Moves(GameConfig config) => Named(config.GetText(TextName.MoveNames), "Movimiento");
    public static NamedEntry[] Types(GameConfig config) => Named(config.GetText(TextName.Types), "Tipo");
    public static NamedEntry[] Natures(GameConfig config) => Named(config.GetText(TextName.Natures), "Naturaleza");
    public static NamedEntry[] TrainerClasses(GameConfig config) => Named(config.GetText(TextName.TrainerClasses), "Clase");

    public static int SpeciesCount(GameConfig config) => config.GetText(TextName.SpeciesNames).Length;
    public static int ItemCount(GameConfig config) => config.GetText(TextName.ItemNames).Length;
    public static int MoveCount(GameConfig config) => config.GetText(TextName.MoveNames).Length;

    private static NamedEntry[] Named(string[] names, string fallback) => names
        .Select((name, id) => new NamedEntry(id, string.IsNullOrWhiteSpace(name) ? $"{fallback} {id}" : name))
        .ToArray();
}

/// <summary>Generation checks, kept together so the wording stays consistent across editors.</summary>
internal static class Guard
{
    public static void Gen7(GameConfig config, string editor)
    {
        if (config.Generation != 7)
            throw new WorkspaceException($"El editor inicial de {editor} está disponible primero para Gen. VII.");
    }

    public static void Gen6(GameConfig config)
    {
        if (config.Generation != 6 || (!config.XY && !config.ORAS))
            throw new WorkspaceException("Este editor es solo para X/Y y OR/AS.");
    }
}
