namespace pk3DS.Editors;

// Text ------------------------------------------------------------------------
public enum TextArchiveKind { Game, Story }
public sealed record TextCatalogRequest(string WorkspacePath, TextArchiveKind Kind = TextArchiveKind.Game, int? Language = null);
public sealed record TextTableRequest(string WorkspacePath, TextArchiveKind Kind, int TableIndex, int? Language = null);
public sealed record TextExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, TextArchiveKind Kind, int TableIndex, string[] Lines, int? Language = null);
public sealed record TextAllTablesRequest(string WorkspacePath, TextArchiveKind Kind = TextArchiveKind.Game, int? Language = null);
public sealed record TextAllTablesResponse(TextArchiveKind Kind, string[][] Tables);
public sealed record TextArchiveExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, TextArchiveKind Kind, string[][] Tables, int? Language = null);
public sealed record TextArchiveExportResponse(
    string OutputFolder,
    string ZipPath,
    string[] ChangedFiles,
    int[] FailedTableIndices,
    string[] Errata);
public sealed record TextTableSummary(int Index, string Name, int LineCount);
public sealed record TextCatalogResponse(string GameVersion, TextArchiveKind Kind, TextTableSummary[] Tables);
public sealed record TextTableResponse(TextArchiveKind Kind, int TableIndex, string[] Lines);

// Level up moves --------------------------------------------------------------
public sealed record LearnsetCatalogRequest(string WorkspacePath, int? Language = null);
public sealed record LearnsetTableRequest(string WorkspacePath, int SpeciesIndex, int? Language = null);
public sealed record LearnsetExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int SpeciesIndex, LearnsetEntry[] Entries, int? Language = null);
public sealed record LearnsetMetronomeExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int? Language = null);
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
/// <summary>
/// The original required fields stay first for source and JSON compatibility. Every additional
/// field is nullable so a caller that only knows the initial Mac contract can still export an
/// entry without manufacturing values for fields it did not read. Compatibility arrays contain
/// the complete packed bitfields in responses; the editor only applies the bits Windows exposes.
/// </summary>
public sealed record PersonalExportRequest(
    string WorkspacePath, string? OutputDirectory, string? TitleId, int SpeciesIndex,
    int[] Stats, int[] Types, int CatchRate, int[] Abilities, int[] Items, int[] EggGroups,
    int? Language = null, int[]? EVYields = null, int? Gender = null,
    int? HatchCycles = null, int? BaseFriendship = null, int? EXPGrowth = null,
    int? Color = null, int? BaseEXP = null,
    int? Height = null, int? Weight = null, bool[]? TMHM = null, bool[]? TypeTutors = null,
    bool[][]? SpecialTutors = null, int? EscapeRate = null, int? SpecialZItem = null,
    int? SpecialZBaseMove = null, int? SpecialZMove = null, bool? LocalVariant = null);

/// <summary>Complete per-entry fields represented by the Gen. VI/VII personal records.</summary>
public sealed record PersonalEntryResponse(
    int SpeciesIndex, int[] Stats, int[] Types, int CatchRate, int[] Abilities, int[] Items,
    int[] EggGroups, int[] EVYields = null!, int EvoStage = 0, int Gender = 0,
    int HatchCycles = 0, int BaseFriendship = 0, int EXPGrowth = 0, int FormeCount = 0,
    int FormeSprite = 0, int Color = 0, int BaseEXP = 0, int Height = 0, int Weight = 0,
    bool[] TMHM = null!, bool[] TypeTutors = null!, bool[][] SpecialTutors = null!,
    int? EscapeRate = null, int? SpecialZItem = null, int? SpecialZBaseMove = null,
    int? SpecialZMove = null, bool? LocalVariant = null, int BST = 0, int ColorRaw = 0);

// Move stats ------------------------------------------------------------------
public sealed record MoveEntryRequest(string WorkspacePath, int MoveIndex, int? Language = null);
/// <summary>
/// The move editor keeps the original six arguments first for source compatibility with the
/// initial Mac port. The remaining values are optional for older callers, but the web editor
/// sends every field Windows exposes. Gen. VII-only values stay nullable so a Gen. VI request
/// cannot accidentally manufacture bytes that do not exist in its record layout.
/// </summary>
public sealed record MoveExportRequest(
    string WorkspacePath, string? OutputDirectory, string? TitleId, int MoveIndex,
    int Type, int Category, int Power, int Accuracy, int PP, int Priority, int? Language = null,
    int? Quality = null, int? HitMin = null, int? HitMax = null, int? Inflict = null, int? InflictPercent = null,
    int? InflictCount = null, int? TurnMin = null, int? TurnMax = null, int? CritStage = null, int? Flinch = null,
    int? EffectSequence = null, int? Recoil = null, int? Healing = null, int? Target = null,
    int? Stat1 = null, int? Stat2 = null, int? Stat3 = null, int? Stat1Stage = null, int? Stat2Stage = null,
    int? Stat3Stage = null, int? Stat1Percent = null, int? Stat2Percent = null, int? Stat3Percent = null,
    uint? Flags = null, int? ZMove = null, int? ZPower = null, int? ZEffect = null,
    int? RefreshAfflictType = null, int? RefreshAfflictPercent = null);

/// <summary>All fields from the Gen. VI/VII move records that the Windows editor exposes.</summary>
public sealed record MoveEntryResponse(
    int MoveIndex, int Type, int Category, int Power, int Accuracy, int PP, int Priority,
    int Quality = 0, int HitMin = 0, int HitMax = 0, int Inflict = 0, int InflictPercent = 0,
    int InflictCount = 0, int TurnMin = 0, int TurnMax = 0, int CritStage = 0, int Flinch = 0,
    int EffectSequence = 0, int Recoil = 0, int Healing = 0, int Target = 0,
    int Stat1 = 0, int Stat2 = 0, int Stat3 = 0, int Stat1Stage = 0, int Stat2Stage = 0,
    int Stat3Stage = 0, int Stat1Percent = 0, int Stat2Percent = 0, int Stat3Percent = 0,
    uint Flags = 0, int Generation = 6, int? ZMove = null, int? ZPower = null, int? ZEffect = null,
    int? RefreshAfflictType = null, int? RefreshAfflictPercent = null);

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
public sealed record WildGen7BatchReportRequest(string WorkspacePath, string? OutputDirectory, int? Language = null);
public sealed record WildGen7TablePairReport(
    int TableIndex, string HeaderHex, WildEncounterTable Day, WildEncounterTable Night,
    string DayRawHex, string NightRawHex);
public sealed record WildGen7AreaReportDocument(
    string GameVersion, int AreaIndex, int FileNumber, string AreaName, string[] ZoneNames,
    bool? HasTables, WildGen7TablePairReport[] Tables, string? Diagnostics = null);
public sealed record WildGen7BatchReportFile(
    int AreaIndex, int FileNumber, string AreaName, string FileName,
    int Bytes, int TableCount, bool? HasTables);
public sealed record WildGen7AreaWithoutTables(
    int AreaIndex, int FileNumber, string AreaName, string Reason, string? ReportFile = null);
public sealed record WildGen7SkippedArea(
    int AreaIndex, int FileNumber, string AreaName, string Diagnostics, string? ReportFile = null);
public sealed record WildGen7BatchReportResponse(
    string GameVersion, string OutputDirectory, string ManifestFile, int TotalAreas,
    WildGen7BatchReportFile[] Reports, WildGen7AreaWithoutTables[] AreasWithoutTables,
    WildGen7SkippedArea[] Skipped, int UnassignedFileCount, int[] UnassignedFileIndices,
    long TotalBytes, string? Diagnostics = null);

// OWSE / scripts del mundo, Gen VI/VII ---------------------------------------
public sealed record OverworldCatalogRequest(string WorkspacePath, int? Language = null);
public sealed record OverworldScriptGroupSummary(string Id, string Name, int WorldIndex, string LocationName, int ScriptCount, long RawBytes);
public sealed record OverworldCatalogResponse(string GameVersion, OverworldScriptGroupSummary[] Groups);
public sealed record OverworldScriptEntryRequest(string WorkspacePath, string Group, int WorldIndex, int ScriptIndex, int? Language = null);
public sealed record OverworldGen7EntityEntrySummary(
    int EntryIndex, int Bytes, int? RecordCount = null, int? RecordKind = null,
    string? PreviewHex = null, string? Schema = null, int? RecordStride = null,
    int? PositionOffset = null, OverworldGen7EsKind0DescriptorSummary[]? EsKind0Descriptors = null);
public sealed record OverworldGen7EsKind0DescriptorSummary(
    int DescriptorIndex, int PayloadOffset, int RawWord1, int RawWord2, int RawWord3,
    int PayloadBytes, int? PayloadType, int? PointCount, string SegmentLayout, string? PreviewHex);
public sealed record OverworldGen7EntityBlockSummary(
    string Identifier, int Bytes, int EntryCount, bool IsMiniArchive,
    OverworldGen7EntityEntrySummary[]? Entries = null, int? BlockIndex = null);
public sealed record OverworldZoneSummary(
    int ZoneIndex, int ZoneDataBytes, int ZoneFileCount, int? ParentMap = null,
    int? MapArea = null, int? MapMatrix = null, int? TextFile = null, int? ScriptFile = null,
    int? Weather = null, int? AreaIndex = null, int? FurnitureCount = null,
    int? NpcCount = null, int? WarpCount = null, int? TriggerCount = null,
    int? UnknownEntityCount = null, string? Diagnostics = null,
    OverworldGen7EntityBlockSummary[]? EntityBlocks = null);
public sealed record OverworldScriptEntryResponse(
    string Group, int WorldIndex, int ScriptIndex, string LocationName, int RawBytes,
    uint Magic, bool Debug, int ScriptInstructionStart, int ScriptMovementStart,
    int FinalOffset, int AllocatedMemory, int CompressedBytes, int DecompressedBytes,
    uint[] Instructions, string[] ParsedLines, string? ParseError, string[] RawHex,
    OverworldZoneSummary? Zone = null);
public sealed record OverworldGen6ZoneRequest(string WorkspacePath, int ZoneIndex, int? Language = null);
public sealed record OverworldFurnitureEntry(int Script, int X, int Y, int Width, int Height);
public sealed record OverworldNpcEntry(
    int Id, int Model, int SpawnFlag, int Script, int FaceDirection, int SightRange,
    int X, int Y, int MovePermissions, int MovePermissions2);
public sealed record OverworldWarpEntry(
    int DestinationMap, int DestinationTileIndex, int X, int Y, int Width = 0, int Height = 0);
public sealed record OverworldTriggerEntry(int Script, int Constant, int Type, int Flags, int X, int Y, int Width, int Height);
public sealed record OverworldGen6ZoneMetadata(
    int MapArea, int MapMatrix, int TextFile, int ScriptFile, int ParentMap, int Weather,
    int? MapType = null, int? MapMove = null,
    uint? BgmSpring = null, uint? BgmSummer = null, uint? BgmAutumn = null, uint? BgmWinter = null,
    int? TownMapGroup = null, int? OlValue = null,
    bool? SkyBoxEnabled = null, bool? RollerSkateEnabled = null,
    int? BattleBackground = null, int? MapChange = null,
    bool? BicycleEnabled = null, bool? RunEnabled = null, bool? EscapeRopeEnabled = null,
    bool? FlyEnabled = null, bool? BgmEnabled = null, bool? UnknownFlag = null,
    int? Camera1 = null, int? Camera2 = null, uint? CameraFlags = null,
    float? StartX = null, float? StartY = null, int? StartZ = null,
    float? EndX = null, float? EndY = null, int? EndZ = null);
public sealed record OverworldGen6ZoneResponse(
    string GameVersion, int ZoneIndex, string LocationName, OverworldZoneSummary Summary,
    OverworldFurnitureEntry[] Furniture, OverworldNpcEntry[] Npcs, OverworldWarpEntry[] Warps,
    OverworldTriggerEntry[] Triggers, OverworldTriggerEntry[] UnknownTriggers,
    OverworldGen6ZoneMetadata? Metadata = null);
public sealed record OverworldGen6ZoneBatchExportRequest(
    string WorkspacePath, string? OutputDirectory, int? Language = null);
public sealed record OverworldGen6ZoneExportDocument(
    int ZoneIndex, int SourceFileIndex, string LocationName, OverworldZoneSummary Summary,
    string ZoneDataHex, OverworldGen6ZoneMetadata? Metadata,
    OverworldFurnitureEntry[] Furniture, string[] FurnitureRawHex,
    OverworldNpcEntry[] Npcs, string[] NpcRawHex,
    OverworldWarpEntry[] Warps, string[] WarpRawHex,
    OverworldTriggerEntry[] Triggers, string[] TriggerRawHex,
    string[] UnknownEntityRawHex, string? Diagnostics = null);
public sealed record OverworldGen6ZoneExportedFile(
    int ZoneIndex, int SourceFileIndex, string LocationName, string FileName, int Bytes,
    int FurnitureCount, int NpcCount, int WarpCount, int TriggerCount,
    int UnknownEntityCount, string? Diagnostics = null);
public sealed record OverworldGen6ZoneSkippedFile(int ZoneIndex, string LocationName, string Diagnostics);
public sealed record OverworldGen6ZoneBatchExportResponse(
    string GameVersion, string OutputDirectory, string ManifestFile, int TotalZones,
    OverworldGen6ZoneExportedFile[] Zones, OverworldGen6ZoneSkippedFile[] Skipped,
    long TotalBytes, string? Diagnostics = null);
public sealed record OverworldGen6ExportRequest(
    string WorkspacePath, string? OutputDirectory, string? TitleId, int ZoneIndex,
    OverworldFurnitureEntry[]? Furniture, OverworldNpcEntry[]? Npcs, OverworldWarpEntry[]? Warps,
    OverworldTriggerEntry[]? Triggers, OverworldTriggerEntry[]? UnknownTriggers,
    OverworldGen6ZoneMetadata? Metadata = null, int? Language = null);
public sealed record OverworldGen6ScriptExportRequest(
    string WorkspacePath, string? OutputDirectory, string? TitleId, string Group, int ZoneIndex,
    uint[]? Instructions, int? Language = null);
public sealed record OverworldScriptExportRequest(
    string WorkspacePath, string? OutputDirectory, string? TitleId, string Group, int WorldIndex,
    int ScriptIndex, uint[]? Instructions, int? Language = null);
public sealed record OverworldGen7ZoneRequest(string WorkspacePath, int ZoneIndex, int? Language = null);
public sealed record OverworldGen7ZoneResponse(
    string GameVersion, int ZoneIndex, string LocationName, int ParentMap, int ZoneDataBytes,
    int WorldIndex = -1, int AreaIndex = -1);
public sealed record OverworldGen7ZoneExportRequest(
    string WorkspacePath, string? OutputDirectory, string? TitleId, int ZoneIndex, int ParentMap,
    int? AreaIndex = null, int? Language = null);
public sealed record OverworldGen7PositionEntry(int ContainerEntry, int RecordIndex, float X, float Y, float Z);
public sealed record OverworldGen7EmPositionEntry(int ContainerEntry, int RecordIndex, float X, float Y, float Z);
public sealed record OverworldGen7EiPositionEntry(int BlockEntry, int ContainerEntry, int RecordIndex, float X, float Y, float Z);
public sealed record OverworldGen7EbPositionEntry(
    int BlockEntry, int ContainerEntry, int RecordIndex, float X, float Y, float Z, int RecordKind = 2);
public sealed record OverworldGen7EgPositionEntry(int BlockEntry, int ContainerEntry, int RecordIndex, float X, float Y, float Z);
public sealed record OverworldGen7EsPositionEntry(int BlockEntry, int ContainerEntry, int RecordIndex, float X, float Y, float Z);
public sealed record OverworldGen7EaPositionEntry(int BlockEntry, int ContainerEntry, int RecordIndex, float X, float Y, float Z);
public sealed record OverworldGen7EtPositionEntry(int BlockEntry, int ContainerEntry, int RecordIndex, float X, float Y, float Z);
public sealed record OverworldGen7PrPositionEntry(int BlockEntry, int ContainerEntry, int RecordIndex, float X, float Y, float Z);
public sealed record OverworldGen7EntityRequest(string WorkspacePath, int WorldIndex, int? Language = null);
public sealed record OverworldGen7EntityResponse(
    string GameVersion, int WorldIndex, OverworldGen7PositionEntry[] Positions,
    OverworldGen7EmPositionEntry[] EmPositions, OverworldGen7EbPositionEntry[] EbPositions,
    OverworldGen7EsPositionEntry[] EsPositions, OverworldGen7EaPositionEntry[] EaPositions,
    OverworldGen7EtPositionEntry[] EtPositions, OverworldGen7EntityBlockSummary[] Blocks,
    string? Diagnostics = null, OverworldGen7EiPositionEntry[]? EiPositions = null,
    OverworldGen7PrPositionEntry[]? PrPositions = null,
    OverworldGen7EgPositionEntry[]? EgPositions = null);
public sealed record OverworldGen7EntityExportRequest(
    string WorkspacePath, string? OutputDirectory, string? TitleId, int WorldIndex,
    OverworldGen7PositionEntry[]? Positions, OverworldGen7EmPositionEntry[]? EmPositions = null,
    OverworldGen7EbPositionEntry[]? EbPositions = null, OverworldGen7EsPositionEntry[]? EsPositions = null,
    OverworldGen7EaPositionEntry[]? EaPositions = null, OverworldGen7EtPositionEntry[]? EtPositions = null,
    int? Language = null, OverworldGen7EiPositionEntry[]? EiPositions = null,
    OverworldGen7PrPositionEntry[]? PrPositions = null,
    OverworldGen7EgPositionEntry[]? EgPositions = null);
public sealed record OverworldGen7EntityRawExportRequest(
    string WorkspacePath, string? OutputDirectory, int WorldIndex, int? Language = null);
public sealed record OverworldGen7EntityRawFile(
    string RelativePath, string Identifier, int? BlockIndex, int? EntryIndex, int Bytes);
public sealed record OverworldGen7EntityRawExportResponse(
    string GameVersion, int WorldIndex, int SourceFileIndex, string SourceGarc,
    string OutputDirectory, string ManifestFile, OverworldGen7EntityRawFile[] Files,
    long TotalBytes, string? Diagnostics = null);
public sealed record OverworldGen7MapResourceRequest(string WorkspacePath, int WorldIndex, int? Language = null);
public sealed record OverworldGen7MapResourceEntry(
    int FileIndex, int SlotOffset, string RelativePath, int Depth, string Signature, int Bytes,
    bool IsMiniArchive, int? MiniEntryCount, string HeaderHex, string[] PrintableStrings);
public sealed record OverworldGen7MapResourceResponse(
    string GameVersion, int WorldIndex, string SourceGarc, OverworldGen7MapResourceEntry[] Resources,
    string Note, string? Diagnostics = null);
public sealed record OverworldGen7MapResourceExportRequest(
    string WorkspacePath, string? OutputDirectory, int WorldIndex, int? Language = null);
public sealed record OverworldGen7MapResourceExportedFile(
    int FileIndex, int SlotOffset, string RelativePath, string FileName, int Depth, string Signature, int Bytes);
public sealed record OverworldGen7MapResourceExportResponse(
    string GameVersion, int WorldIndex, string SourceGarc, string OutputDirectory, string ManifestFile,
    OverworldGen7MapResourceExportedFile[] Files, long TotalBytes, string Note, string? Diagnostics = null);
public sealed record OverworldGen6MapRequest(
    string WorkspacePath, int ZoneIndex, int? Language = null, bool IncludeEntityOverlay = true);
public sealed record OverworldGen6MapPreview(
    string? PngBase64, int Width, int Height, string? Diagnostics = null);
public sealed record OverworldGen6MapResponse(
    string GameVersion, int ZoneIndex, int MapArea, int MapMatrix, int Width, int Height,
    uint[] Properties, int MatrixWidth, int MatrixHeight, ushort[] MatrixValues,
    string? Diagnostics = null, OverworldGen6MapPreview? Preview = null);
public sealed record OverworldGen6MapExportRequest(
    string WorkspacePath, string? OutputDirectory, string? TitleId, int ZoneIndex,
    uint[]? Properties, ushort[]? MatrixValues = null, int? Language = null);
public sealed record OverworldGen6MapBatchExportRequest(
    string WorkspacePath, string? OutputDirectory, bool IncludeEntityOverlay = true, int? Language = null);
public sealed record OverworldGen6MapExportedFile(
    int ZoneIndex, string LocationName, int MapArea, int MapMatrix, string FileName,
    int Width, int Height, int Bytes, ushort[] MapEntries, string? Diagnostics = null);
public sealed record OverworldGen6MapSkippedFile(int ZoneIndex, string LocationName, string Diagnostics);
public sealed record OverworldGen6MapBatchExportResponse(
    string GameVersion, string OutputDirectory, string ManifestFile, int TotalZones,
    OverworldGen6MapExportedFile[] Maps, OverworldGen6MapSkippedFile[] Skipped,
    long TotalBytes, string? Diagnostics = null);
public sealed record OverworldGen6ScriptBatchExportRequest(
    string WorkspacePath, string? OutputDirectory, int? Language = null);
public sealed record OverworldGen6ScriptExportedFile(
    int ZoneIndex, string LocationName, string Group, string FileName,
    int RawBytes, int InstructionCount, int Bytes, string? Diagnostics = null);
public sealed record OverworldGen6ScriptSkippedFile(
    int ZoneIndex, string LocationName, string Group, string Diagnostics);
public sealed record OverworldGen6ScriptExportDocument(
    int ZoneIndex, string LocationName, string Group, int RawBytes,
    uint Magic, bool Debug, int ScriptInstructionStart, int ScriptMovementStart,
    int FinalOffset, int AllocatedMemory, int CompressedBytes, int DecompressedBytes,
    uint[] ScriptInstructions, uint[] MovementInstructions,
    string[] ScriptInstructionHex, string[] MovementInstructionHex,
    string[] ParsedScriptLines, string[] ParsedMovementLines,
    string[] RawHex, string? Diagnostics);
public sealed record OverworldGen6ScriptBatchExportResponse(
    string GameVersion, string OutputDirectory, string ManifestFile, int TotalZones,
    OverworldGen6ScriptExportedFile[] Scripts, OverworldGen6ScriptSkippedFile[] Skipped,
    long TotalBytes, string? Diagnostics = null);
public sealed record OverworldGen7ScriptBatchExportRequest(
    string WorkspacePath, string? OutputDirectory, int? Language = null);
public sealed record OverworldGen7ScriptExportedFile(
    int WorldIndex, int ScriptIndex, string LocationName, string Group, string FileName,
    int RawBytes, int InstructionCount, int Bytes, string? Diagnostics = null);
public sealed record OverworldGen7ScriptSkippedFile(
    int WorldIndex, string LocationName, string Group, string Diagnostics);
public sealed record OverworldGen7ScriptExportDocument(
    int WorldIndex, int ScriptIndex, string LocationName, string Group, int RawBytes,
    uint Magic, bool Debug, int ScriptInstructionStart, int ScriptMovementStart,
    int FinalOffset, int AllocatedMemory, int CompressedBytes, int DecompressedBytes,
    uint[] ScriptInstructions, uint[] MovementInstructions,
    string[] ScriptInstructionHex, string[] MovementInstructionHex,
    string[] ParsedScriptLines, string[] ParsedMovementLines,
    string[] RawHex, string? Diagnostics);
public sealed record OverworldGen7ScriptBatchExportResponse(
    string GameVersion, string OutputDirectory, string ManifestFile, int TotalWorlds,
    int TotalScripts, int UnassignedFileCount, OverworldGen7ScriptExportedFile[] Scripts,
    OverworldGen7ScriptSkippedFile[] Skipped, long TotalBytes, string? Diagnostics = null);

// Wild encounters, Gen VI -----------------------------------------------------
public sealed record WildGen6CatalogRequest(string WorkspacePath, int? Language = null);
public sealed record WildGen6AreaSummary(int FileIndex, int LocationIndex, string Name, bool HasEncounters);
public sealed record WildGen6CatalogResponse(string Game, WildGen6AreaSummary[] Areas);
public sealed record WildGen6TableRequest(string WorkspacePath, int FileIndex, int? Language = null);
public sealed record WildGen6Slot(int Species, int Form, int MinLevel, int MaxLevel);
public sealed record WildGen6Group(string Name, WildGen6Slot[] Slots);
public sealed record WildGen6TableResponse(int FileIndex, string AreaName, WildGen6Group[] Groups, NamedEntry[] Species);
public sealed record WildGen6ExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int FileIndex, WildGen6Group[] Groups, int? Language = null);
public sealed record WildGen6BatchReportRequest(string WorkspacePath, string? OutputDirectory, int? Language = null);
public sealed record WildGen6AreaReportDocument(
    string GameVersion, int FileIndex, int LocationIndex, string AreaName, bool? HasEncounters,
    WildGen6Group[] Groups, string? Diagnostics = null);
public sealed record WildGen6BatchReportFile(
    int FileIndex, int LocationIndex, string AreaName, string FileName,
    int Bytes, int SlotCount, int GroupCount, bool? HasEncounters);
public sealed record WildGen6NoEncounterArea(
    int FileIndex, int LocationIndex, string AreaName, string Reason, string? ReportFile = null);
public sealed record WildGen6SkippedArea(
    int FileIndex, int LocationIndex, string AreaName, string Diagnostics, string? ReportFile = null);
public sealed record WildGen6BatchReportResponse(
    string GameVersion, string OutputDirectory, string ManifestFile, int TotalAreas,
    WildGen6BatchReportFile[] Reports, WildGen6NoEncounterArea[] AreasWithoutEncounters,
    WildGen6SkippedArea[] Skipped, long TotalBytes, string? Diagnostics = null);

// Static encounters, Gen VII --------------------------------------------------
public sealed record StaticCatalogRequest(string WorkspacePath, int? Language = null);
public sealed record StaticGroupSummary(string Id, string Name, int Count);
public sealed record StaticCatalogResponse(StaticGroupSummary[] Groups, NamedEntry[] Species, NamedEntry[] Items, NamedEntry[] Moves);
public sealed record StaticEntryRequest(string WorkspacePath, string Group, int EntryIndex, int? Language = null);
public sealed record StaticEntry(int Species, int Form, int Level, int HeldItem, int? Gender = null, int? Ability = null, int? Nature = null, bool? ShinyLock = null, bool? IsEgg = null, int? SpecialMove = null, int[]? RelearnMoves = null, int[]? IVs = null, int[]? EVs = null, int? Aura = null, int? Ally1 = null, int? Ally2 = null, int? TradeRequestSpecies = null, int? TID = null, bool? Shiny = null, int? Map = null, int? Allies = null, int? SID = null, int? OTGender = null, int? OTIntensity = null, int? OTMemory = null, int? OTTextVar = null, int? OTFeeling = null);
public sealed record StaticEntryResponse(string Group, int EntryIndex, StaticEntry Entry);
public sealed record StaticExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, string Group, int EntryIndex, StaticEntry? Entry, int? Language = null);

// Static encounters, Gen VI ---------------------------------------------------
public sealed record StaticGen6CatalogRequest(string WorkspacePath, int? Language = null);
public sealed record StaticGen6CatalogResponse(string Game, int Count, NamedEntry[] Species, NamedEntry[] Items, string Warning);
public sealed record StaticGen6EntryRequest(string WorkspacePath, int EntryIndex, int? Language = null);
public sealed record StaticGen6Entry(int Species, int Form, int Level, int HeldItem, int Gender, int Ability, bool ShinyLock, bool IV3);
public sealed record StaticGen6EntryResponse(int EntryIndex, StaticGen6Entry Entry);
public sealed record StaticGen6ExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int EntryIndex, StaticGen6Entry? Entry, int? Language = null);
public sealed record StaticGen6RandomizeRequest(
    string WorkspacePath,
    string? OutputDirectory,
    string? TitleId,
    int? Language = null,
    bool AllowGen1 = true,
    bool AllowGen2 = true,
    bool AllowGen3 = true,
    bool AllowGen4 = true,
    bool AllowGen5 = true,
    bool AllowGen6 = true,
    bool IncludeMythical = true,
    bool IncludeLegendary = true,
    bool MatchBst = false,
    bool ReplaceLegendary = true,
    bool AllowMegaForms = false,
    bool RandomizeHeldItems = true,
    bool RandomizeLevels = true,
    decimal LevelMultiplier = 1.0m,
    bool RemoveShinyLocks = false,
    bool RandomizeAbilities = false,
    bool ForceFullyEvolved = false,
    int FullyEvolvedLevel = 50,
    int? Seed = null);

public sealed record StaticGen7RandomizeRequest(
    string WorkspacePath,
    string? OutputDirectory,
    string? TitleId,
    int? Language = null,
    bool RandomizeStartersOnly = false,
    bool AllowGen1 = true,
    bool AllowGen2 = true,
    bool AllowGen3 = true,
    bool AllowGen4 = true,
    bool AllowGen5 = true,
    bool AllowGen6 = true,
    bool AllowGen7 = true,
    bool IncludeMythical = true,
    bool IncludeLegendary = true,
    bool MatchBst = false,
    bool ReplaceLegendary = true,
    bool AllowMegaForms = false,
    bool RandomizeHeldItems = true,
    bool RandomizeLevels = true,
    decimal LevelMultiplier = 1.0m,
    bool RemoveShinyLocks = true,
    bool RandomizeAbilities = false,
    bool RandomizeAura = false,
    bool RandomizeSpecialMoves = false,
    bool MetronomeMode = false,
    bool ForceFullyEvolved = false,
    int FullyEvolvedLevel = 50,
    bool ForceFullyEvolvedTotems = false,
    bool BasicStarter = true,
    int? Seed = null,
    bool UpdateStarterText = false);
public sealed record StaticGen7RandomizeResponse(
    string OutputFolder,
    string ZipPath,
    string[] ChangedFiles,
    bool StarterTextUpdated);

// Gift Pokémon, Gen VI CRO ----------------------------------------------------
public sealed record GiftGen6CatalogRequest(string WorkspacePath, int? Language = null);
public sealed record GiftGen6CatalogResponse(string Game, int Count, NamedEntry[] Species, NamedEntry[] Items, NamedEntry[] Natures, string Warning);
public sealed record GiftGen6EntryRequest(string WorkspacePath, int EntryIndex, int? Language = null);
public sealed record GiftGen6Entry(int Species, int Form, int Level, int HeldItem, int Gender, int Ability, int Nature, bool ShinyLock, int[] IVs);
public sealed record GiftGen6EntryResponse(int EntryIndex, GiftGen6Entry Entry);
public sealed record GiftGen6ExportRequest(string WorkspacePath, string? OutputDirectory, string? TitleId, int EntryIndex, GiftGen6Entry? Entry, int? Language = null);
public sealed record GiftGen6RandomizeRequest(
    string WorkspacePath,
    string? OutputDirectory,
    string? TitleId,
    int? Language = null,
    bool AllowGen1 = true,
    bool AllowGen2 = true,
    bool AllowGen3 = true,
    bool AllowGen4 = true,
    bool AllowGen5 = true,
    bool AllowGen6 = true,
    bool IncludeMythical = true,
    bool IncludeLegendary = true,
    bool MatchBst = false,
    bool ReplaceMegaGifts = false,
    bool AllowMegaForms = false,
    bool RandomizeHeldItems = true,
    bool RandomizeLevels = true,
    decimal LevelMultiplier = 1.0m,
    bool RemoveShinyLocks = false,
    bool RandomizeAbilities = false,
    bool ForceFullyEvolved = false,
    int FullyEvolvedLevel = 50,
    int? Seed = null);

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
    bool? HasItems = null, bool? HasMoves = null, string? Name = null, string? ClassName = null, int? Prize = null);
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
public sealed record StarterRandomizeRequest(
    string WorkspacePath,
    string? OutputDirectory,
    string? TitleId,
    int? Language = null,
    bool BasicStarter = true,
    bool KeepSameGen = false,
    bool IncludeLegendary = false,
    bool IncludeMythical = false,
    int? Seed = null);

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
