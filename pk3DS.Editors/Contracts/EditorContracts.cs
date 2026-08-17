namespace pk3DS.Editors;

// Text ------------------------------------------------------------------------
public enum TextArchiveKind { Game, Story }
public sealed record TextCatalogRequest(string WorkspacePath, TextArchiveKind Kind = TextArchiveKind.Game, int? Language = null);
public sealed record TextTableRequest(string WorkspacePath, TextArchiveKind Kind, int TableIndex, int? Language = null);
public sealed record TextExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, TextArchiveKind Kind, int TableIndex, string[] Lines, int? Language = null);
public sealed record TextTableSummary(int Index, string Name, int LineCount);
public sealed record TextCatalogResponse(string GameVersion, TextArchiveKind Kind, TextTableSummary[] Tables);
public sealed record TextTableResponse(TextArchiveKind Kind, int TableIndex, string[] Lines);

// Level up moves --------------------------------------------------------------
public sealed record LearnsetCatalogRequest(string WorkspacePath, int? Language = null);
public sealed record LearnsetTableRequest(string WorkspacePath, int SpeciesIndex, int? Language = null);
public sealed record LearnsetExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int SpeciesIndex, LearnsetEntry[] Entries, int? Language = null);
public sealed record LearnsetSpeciesSummary(int Id, string Name, int MoveCount);
public sealed record LearnsetEntry(int Level, int MoveId);
public sealed record LearnsetCatalogResponse(string GameVersion, LearnsetSpeciesSummary[] Species, NamedEntry[] Moves);
public sealed record LearnsetTableResponse(int SpeciesIndex, LearnsetEntry[] Entries);

// Egg moves -------------------------------------------------------------------
public sealed record EggMoveTableRequest(string WorkspacePath, int SpeciesIndex, int? Language = null);
public sealed record EggMoveExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int SpeciesIndex, int[] Moves, int? FormTableIndex = null, int? Language = null);
public sealed record EggMoveTableResponse(int SpeciesIndex, int[] Moves, int FormTableIndex);

// Evolutions ------------------------------------------------------------------
public sealed record EvolutionTableRequest(string WorkspacePath, int SpeciesIndex, int? Language = null);
public sealed record EvolutionExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int SpeciesIndex, EvolutionEntry[] Entries, int? Language = null);
public sealed record EvolutionEntry(int Method, int Argument, int Species, int Form, int Level);
public sealed record EvolutionTableResponse(int SpeciesIndex, EvolutionEntry[] Entries);

// Personal stats --------------------------------------------------------------
public sealed record PersonalEntryRequest(string WorkspacePath, int SpeciesIndex, int? Language = null);
public sealed record PersonalExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int SpeciesIndex, int[] Stats, int[] Types, int CatchRate, int[] Abilities, int[] Items, int[] EggGroups, int? Language = null);
public sealed record PersonalEntryResponse(int SpeciesIndex, int[] Stats, int[] Types, int CatchRate, int[] Abilities, int[] Items, int[] EggGroups);

// Move stats ------------------------------------------------------------------
public sealed record MoveEntryRequest(string WorkspacePath, int MoveIndex, int? Language = null);
public sealed record MoveExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int MoveIndex, int Type, int Category, int Power, int Accuracy, int PP, int Priority, int? Language = null);
public sealed record MoveEntryResponse(int MoveIndex, int Type, int Category, int Power, int Accuracy, int PP, int Priority);

// Item stats ------------------------------------------------------------------
public sealed record ItemEntryRequest(string WorkspacePath, int ItemIndex, int? Language = null);
public sealed record ItemExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int ItemIndex, int BuyPrice, int HeldEffect, int HeldArgument, int FlingPower, int EffectField, int EffectBattle, int HealValue, int? Language = null);
public sealed record ItemEntryResponse(int ItemIndex, int BuyPrice, int HeldEffect, int HeldArgument, int FlingPower, int EffectField, int EffectBattle, int HealValue);

// Mega evolutions -------------------------------------------------------------
public sealed record MegaTableRequest(string WorkspacePath, int SpeciesIndex, int? Language = null);
public sealed record MegaExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int SpeciesIndex, MegaEntry[] Entries, int? Language = null);
public sealed record MegaEntry(int Form, int Method, int Argument, int Auxiliary);
public sealed record MegaTableResponse(int SpeciesIndex, MegaEntry[] Entries);

// Wild encounters, Gen VII ----------------------------------------------------
public sealed record WildAreaCatalogRequest(string WorkspacePath, int? Language = null);
public sealed record WildAreaSummary(int FileNumber, string Name, int TableCount);
public sealed record WildAreaCatalogResponse(WildAreaSummary[] Areas);
public sealed record WildTableRequest(string WorkspacePath, int FileNumber, int TableIndex, int? Language = null);
public sealed record WildEncounterSlot(int Species, int Form, int Rate);
public sealed record WildEncounterCompanionSlot(int Species, int Form);
public sealed record WildEncounterTable(int MinLevel, int MaxLevel, WildEncounterSlot[] Slots, WildEncounterCompanionSlot[][]? SosSlots = null, WildEncounterCompanionSlot[]? WeatherSlots = null);
public sealed record WildTableResponse(int FileNumber, string AreaName, int TableIndex, WildEncounterTable Day, WildEncounterTable Night, NamedEntry[] Species);
public sealed record WildExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int FileNumber, int TableIndex, WildEncounterTable Day, WildEncounterTable Night, int? Language = null);

// OWSE / scripts del mundo, Gen VI/VII (solo lectura) ------------------------
public sealed record OverworldCatalogRequest(string WorkspacePath, int? Language = null);
public sealed record OverworldScriptGroupSummary(string Id, string Name, int WorldIndex, string LocationName, int ScriptCount, long RawBytes);
public sealed record OverworldCatalogResponse(string GameVersion, OverworldScriptGroupSummary[] Groups);
public sealed record OverworldScriptEntryRequest(string WorkspacePath, string Group, int WorldIndex, int ScriptIndex, int? Language = null);
public sealed record OverworldScriptEntryResponse(
    string Group, int WorldIndex, int ScriptIndex, string LocationName, int RawBytes,
    uint Magic, bool Debug, int ScriptInstructionStart, int ScriptMovementStart,
    int FinalOffset, int AllocatedMemory, int CompressedBytes, int DecompressedBytes,
    uint[] Instructions, string[] ParsedLines, string? ParseError, string[] RawHex);

// Wild encounters, Gen VI -----------------------------------------------------
public sealed record WildGen6CatalogRequest(string WorkspacePath, int? Language = null);
public sealed record WildGen6AreaSummary(int FileIndex, int LocationIndex, string Name, bool HasEncounters);
public sealed record WildGen6CatalogResponse(string Game, WildGen6AreaSummary[] Areas);
public sealed record WildGen6TableRequest(string WorkspacePath, int FileIndex, int? Language = null);
public sealed record WildGen6Slot(int Species, int Form, int MinLevel, int MaxLevel);
public sealed record WildGen6Group(string Name, WildGen6Slot[] Slots);
public sealed record WildGen6TableResponse(int FileIndex, string AreaName, WildGen6Group[] Groups, NamedEntry[] Species);
public sealed record WildGen6ExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int FileIndex, WildGen6Group[] Groups, int? Language = null);

// Static encounters, Gen VII --------------------------------------------------
public sealed record StaticCatalogRequest(string WorkspacePath, int? Language = null);
public sealed record StaticGroupSummary(string Id, string Name, int Count);
public sealed record StaticCatalogResponse(StaticGroupSummary[] Groups, NamedEntry[] Species, NamedEntry[] Items, NamedEntry[] Moves);
public sealed record StaticEntryRequest(string WorkspacePath, string Group, int EntryIndex, int? Language = null);
public sealed record StaticEntry(int Species, int Form, int Level, int HeldItem, int? Gender = null, int? Ability = null, int? Nature = null, bool? ShinyLock = null, bool? IsEgg = null, int? SpecialMove = null, int[]? RelearnMoves = null, int[]? IVs = null, int[]? EVs = null, int? Aura = null, int? Ally1 = null, int? Ally2 = null, int? TradeRequestSpecies = null, int? TID = null);
public sealed record StaticEntryResponse(string Group, int EntryIndex, StaticEntry Entry);
public sealed record StaticExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, string Group, int EntryIndex, StaticEntry? Entry, int? Language = null);

// Static encounters, Gen VI ---------------------------------------------------
public sealed record StaticGen6CatalogRequest(string WorkspacePath, int? Language = null);
public sealed record StaticGen6CatalogResponse(string Game, int Count, NamedEntry[] Species, NamedEntry[] Items, string Warning);
public sealed record StaticGen6EntryRequest(string WorkspacePath, int EntryIndex, int? Language = null);
public sealed record StaticGen6Entry(int Species, int Form, int Level, int HeldItem, int Gender, int Ability, bool ShinyLock, bool IV3);
public sealed record StaticGen6EntryResponse(int EntryIndex, StaticGen6Entry Entry);
public sealed record StaticGen6ExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int EntryIndex, StaticGen6Entry? Entry, int? Language = null);

// Gift Pokémon, Gen VI CRO ----------------------------------------------------
public sealed record GiftGen6CatalogRequest(string WorkspacePath, int? Language = null);
public sealed record GiftGen6CatalogResponse(string Game, int Count, NamedEntry[] Species, NamedEntry[] Items, NamedEntry[] Natures, string Warning);
public sealed record GiftGen6EntryRequest(string WorkspacePath, int EntryIndex, int? Language = null);
public sealed record GiftGen6Entry(int Species, int Form, int Level, int HeldItem, int Gender, int Ability, int Nature, bool ShinyLock, int[] IVs);
public sealed record GiftGen6EntryResponse(int EntryIndex, GiftGen6Entry Entry);
public sealed record GiftGen6ExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int EntryIndex, GiftGen6Entry? Entry, int? Language = null);

// Move Tutors, Gen VI ExeFS ---------------------------------------------------
public sealed record TutorGen6Group(string Name, int[] Moves);
public sealed record TutorGen6TableRequest(string WorkspacePath, int? Language = null);
public sealed record TutorGen6TableResponse(string GameVersion, TutorGen6Group[] Groups, NamedEntry[] Moves, string Warning);
public sealed record TutorGen6ExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, TutorGen6Group[] Groups, int? Language = null);

// Trainers --------------------------------------------------------------------
public sealed record TrainerCatalogRequest(string WorkspacePath, int? Language = null);
public sealed record TrainerSummary(int Id, string Name);
public sealed record TrainerCatalogResponse(TrainerSummary[] Trainers, NamedEntry[] Classes, NamedEntry[] Species, NamedEntry[] Items, NamedEntry[] Moves);
public sealed record TrainerEntryRequest(string WorkspacePath, int TrainerIndex, int? Language = null);
public sealed record TrainerPokemonEntry(int Species, int Form, int Level, int Item, int[] Moves, int Ability, int Gender, int Nature, bool Shiny, int[] IVs, int[] EVs);
public sealed record TrainerEntry(int TrainerClass, int Mode, int[] Items, int AI, bool Flag, int Money, TrainerPokemonEntry[] Team,
    bool? HasItems = null, bool? HasMoves = null, string? Name = null, string? ClassName = null);
public sealed record TrainerEntryResponse(int TrainerIndex, TrainerEntry Entry);
public sealed record TrainerExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int TrainerIndex, TrainerEntry? Entry, int? Language = null);

// TMs / HMs, ExeFS ------------------------------------------------------------
public sealed record TmHmTableRequest(string WorkspacePath, int? Language = null);
public sealed record TmHmTableResponse(string GameVersion, int[] TMs, int[] HMs, NamedEntry[] Moves, string Warning);
public sealed record TmHmExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int[] TMs, int[] HMs, int? Language = null);

// Pickup, Gen VI ExeFS --------------------------------------------------------
public sealed record PickupGen6TableRequest(string WorkspacePath, int? Language = null);
public sealed record PickupGen6TableResponse(string GameVersion, int[] Common, int[] Rare, NamedEntry[] Items, string Warning);
public sealed record PickupGen6ExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int[] Common, int[] Rare, int? Language = null);

// Shiny rate, ExeFS ----------------------------------------------------------
public sealed record ShinyRateTableRequest(string WorkspacePath, int? Language = null);
public sealed record ShinyRateTableResponse(string GameVersion, int Rerolls, bool EverythingShiny, int[] SupportedRerolls, string Warning);
public sealed record ShinyRateExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int Rerolls, bool EverythingShiny, int? Language = null);

// Poké Marts, Gen VII CRO -----------------------------------------------------
public sealed record MartEntry(int Item, int? Price = null);
public sealed record MartGroup(string Name, MartEntry[] Entries);
public sealed record MartTableRequest(string WorkspacePath, int? Language = null);
public sealed record MartTableResponse(string GameVersion, MartGroup[] Regular, MartGroup[] BattlePoints, NamedEntry[] Items, string Warning);
public sealed record MartExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, MartGroup[] Regular, MartGroup[] BattlePoints, int? Language = null);

// O-Powers, Gen VI ExeFS ------------------------------------------------------
public sealed record OPowerTableRequest(string WorkspacePath, int? Language = null);
public sealed record OPowerEntry(int PlayerCost, int OtherCost, int Stage, int LevelUp, int Efficacy, int Duration, int Usability);
public sealed record OPowerTableResponse(string GameVersion, OPowerEntry[] Entries, string Warning);
public sealed record OPowerExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, OPowerEntry[] Entries, int? Language = null);

// Type chart, Gen VI CRO / Gen VII ExeFS -------------------------------------
public sealed record TypeChartTableRequest(string WorkspacePath, int? Language = null);
public sealed record TypeChartTableResponse(string GameVersion, int TypeCount, int[] Chart, NamedEntry[] Types, string Warning);
public sealed record TypeChartExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int[] Chart, int? Language = null);

// Starters, Gen VI CRO -------------------------------------------------------
public sealed record StarterGroup(string Name, int[] Species);
public sealed record StarterTableRequest(string WorkspacePath, int? Language = null);
public sealed record StarterTableResponse(string GameVersion, StarterGroup[] Groups, NamedEntry[] Species, string Warning);
public sealed record StarterExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, StarterGroup[] Groups, int? Language = null);

// Move Tutors, Gen VII CRO ----------------------------------------------------
public sealed record TutorTableRequest(string WorkspacePath, int? Language = null);
public sealed record TutorEntry(int Move, int Price);
public sealed record TutorGroup(string Name, TutorEntry[] Entries);
public sealed record TutorTableResponse(string GameVersion, TutorGroup[] Groups, NamedEntry[] Moves, string Warning);
public sealed record TutorExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, TutorGroup[] Groups, int? Language = null);

// Pickup, Gen VII -------------------------------------------------------------
public sealed record PickupTableRequest(string WorkspacePath, int? Language = null);
public sealed record PickupEntry(int Item, int[] Rates);
public sealed record PickupTableResponse(PickupEntry[] Entries, NamedEntry[] Items);
public sealed record PickupExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, PickupEntry[] Entries, int? Language = null);

// Battle Maison / Battle Tree / Battle Royal ---------------------------------
public sealed record MaisonCatalogRequest(string WorkspacePath, string Variant = "normal", int? Language = null);
public sealed record MaisonCatalogResponse(string GameVersion, string Variant, NamedEntry[] Trainers, NamedEntry[] Pokemon, NamedEntry[] Classes, NamedEntry[] Species, NamedEntry[] Items, NamedEntry[] Moves, NamedEntry[] Natures);
public sealed record MaisonTrainerRequest(string WorkspacePath, string Variant, int TrainerIndex, int? Language = null);
public sealed record MaisonTrainerEntry(int TrainerClass, int[] Choices);
public sealed record MaisonTrainerResponse(string Variant, int TrainerIndex, MaisonTrainerEntry Entry);
public sealed record MaisonPokemonRequest(string WorkspacePath, string Variant, int PokemonIndex, int? Language = null);
public sealed record MaisonPokemonEntry(int Species, int Form, int Nature, int Item, int[] Moves, bool[] EVs);
public sealed record MaisonPokemonResponse(string Variant, int PokemonIndex, MaisonPokemonEntry Entry);
public sealed record MaisonTrainerExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, string Variant, int TrainerIndex, MaisonTrainerEntry? Entry, int? Language = null);
public sealed record MaisonPokemonExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, string Variant, int PokemonIndex, MaisonPokemonEntry? Entry, int? Language = null);
