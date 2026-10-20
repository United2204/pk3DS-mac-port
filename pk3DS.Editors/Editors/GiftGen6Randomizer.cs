using pk3DS.Core;
using pk3DS.Core.Randomizers;
using pk3DS.Core.Structures;

namespace pk3DS.Editors;

/// <summary>
/// Ports GiftEditor6's <c>Randomize All</c> action. Gift records live in the loose
/// <c>DllField.cro</c> file, so the result is one CRO LayeredFS patch.
/// </summary>
public static class GiftGen6Randomizer
{
    private const string CroFile = "DllField.cro";

    private static readonly (int Species, int[] Items)[] MegaOptionsXY =
    [
        (003, [659]), (006, [660, 678]), (009, [661]), (065, [679]), (094, [656]),
        (115, [675]), (127, [671]), (130, [676]), (142, [672]), (150, [662, 663]),
        (181, [658]), (212, [670]), (214, [680]), (229, [666]), (248, [669]),
        (257, [664]), (282, [657]), (303, [681]), (306, [667]), (308, [665]),
        (310, [682]), (354, [668]), (359, [677]), (380, [684]), (381, [685]),
        (445, [683]), (448, [673]), (460, [674]),
    ];

    private static readonly (int Species, int[] Items)[] MegaOptionsORAS =
    [
        (015, [770]), (018, [762]), (080, [760]), (208, [761]), (254, [753]),
        (260, [752]), (302, [754]), (319, [759]), (323, [767]), (334, [755]),
        (362, [763]), (373, [769]), (376, [758]), (428, [768]), (475, [756]),
        (531, [757]), (719, [764]),
    ];

    // These are the same held-item and Poké Ball pools used by the Windows randomizer.
    // Item 0 is intentionally removed when the final pool is built.
    private static readonly int[] ItemsHeldXY =
    [
        001, 002, 003, 004, 005, 006, 007, 008, 009, 010, 011, 012, 013, 014, 015, 017, 018, 019, 020, 021, 022,
        023, 024, 025, 026, 027, 028, 029, 030, 031, 032, 033, 034, 035,
        036, 037, 038, 039, 040, 041, 042, 043, 044, 045, 046, 047, 048, 049, 050, 051, 052, 053, 054, 055, 056, 057,
        058, 059, 060, 061, 062, 063, 064, 065, 066, 067, 068, 069, 070,
        071, 072, 073, 074, 075, 076, 077, 078, 079, 080, 081, 082, 083, 084, 085, 086, 087, 088, 089, 090, 091, 092,
        093, 094, 099, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109,
        110, 112, 116, 117, 118, 119, 134, 135, 136, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161,
        162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174,
        175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196,
        197, 198, 199, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209,
        210, 211, 212, 213, 214, 215, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231, 232,
        233, 234, 235, 236, 237, 238, 239, 240, 241, 242, 243, 244,
        245, 246, 247, 248, 249, 250, 251, 252, 253, 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266,
        267, 268, 269, 270, 271, 272, 273, 274, 275, 276, 277, 278, 279,
        280, 281, 282, 283, 284, 285, 286, 287, 288, 289, 290, 291, 292, 293, 294, 295, 296, 297, 298, 299, 300, 301,
        302, 303, 304, 305, 306, 307, 308, 309, 310, 311, 312, 313, 314,
        315, 316, 317, 318, 319, 320, 321, 322, 323, 324, 325, 326, 327, 504, 537, 538, 539, 540, 541, 542, 543, 544,
        545, 546, 547, 548, 549, 550, 551, 552, 553, 554, 555, 556, 557,
        558, 559, 560, 561, 562, 563, 564, 565, 566, 567, 568, 569, 570, 571, 572, 573, 577, 580, 581, 582, 583, 584,
        585, 586, 587, 588, 589, 590, 591, 639, 640, 644, 645, 646, 647,
        648, 649, 650, 652, 653, 654, 655, 656, 657, 658, 659, 660, 661, 662, 663, 664, 665, 666, 667, 668, 669, 670,
        671, 672, 673, 674, 675, 676, 677, 678, 679, 680, 681, 682, 683,
        684, 685, 686, 687, 688, 699, 704, 708, 709, 710, 711, 715,
    ];

    private static readonly int[] ItemsHeldORAS =
    [
        .. ItemsHeldXY,
        534, 535,
        752, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763, 764, 767, 768, 769, 770,
    ];

    private static readonly int[] ItemsBall =
    [
        000, 001, 002, 003, 004, 005, 006, 007, 008, 009, 010, 011, 012,
        013, 014, 015, 016, 492, 493, 494, 495, 496, 497, 498, 499, 576,
    ];

    public static ExportResult Randomize(GiftGen6RandomizeRequest request)
    {
        ValidateRequest(request);
        var workspace = GameWorkspace.Open(request.WorkspacePath);
        RequireCro(workspace);

        var sourceConfig = EditorSession.OpenReadOnly(workspace, request.Language);
        Guard.Gen6(sourceConfig);
        RequireTable(workspace, sourceConfig.ORAS);

        return EditorSession.ExportLooseFiles(request.WorkspacePath, request.OutputDirectory, request.TitleId,
            request.Language, "gift6-randomize", [CroFile], (_, config, scratchRomFs) =>
            {
                Guard.Gen6(config);
                var path = Path.Combine(scratchRomFs, CroFile);
                var data = File.ReadAllBytes(path);
                RequireTable(data, config.ORAS);

                RandomizerService.WithRandomizerSeed(request.Seed, () =>
                {
                    MutateTable(config, data, request);
                    return 0;
                });
                File.WriteAllBytes(path, data);
            });
    }

    private static void MutateTable(GameConfig config, byte[] data, GiftGen6RandomizeRequest options)
    {
        var speciesRandomizer = new SpeciesRandomizer(config)
        {
            G1 = options.AllowGen1,
            G2 = options.AllowGen2,
            G3 = options.AllowGen3,
            G4 = options.AllowGen4,
            G5 = options.AllowGen5,
            G6 = options.AllowGen6,
            G7 = false,
            E = options.IncludeMythical,
            L = options.IncludeLegendary,
            rBST = options.MatchBst,
        };
        speciesRandomizer.Initialize();

        var finalEvolutions = Legal.FinalEvolutions_6
            .Concat(options.IncludeLegendary ? Legal.Legendary_6 : Array.Empty<int>())
            .Concat(options.IncludeMythical ? Legal.Mythical_6 : Array.Empty<int>())
            .Where(species => species > 0 && species < config.Personal.Table.Length)
            .Distinct()
            .ToArray();
        var megaOptions = GetMegaOptions(config.ORAS);
        var megaItems = megaOptions.SelectMany(option => option.Items).ToHashSet();
        var itemPool = options.RandomizeHeldItems
            ? GetRandomItemPool(config.ORAS)
            : Array.Empty<int>();
        var formRandomizer = new FormRandomizer(config)
        {
            AllowMega = options.AllowMegaForms,
            AllowAlolanForm = false,
        };

        for (var index = 0; index < GiftGen6Editor.GetCount(config.ORAS); index++)
        {
            var offset = GiftGen6Editor.GetOffset(config.ORAS, index);
            var gift = new EncounterGift6(data.Skip(offset).Take(GiftGen6Editor.GetSize(config.ORAS)).ToArray(), config.ORAS);
            var species = (int)gift.Species;

            if (megaItems.Contains(gift.HeldItem))
            {
                // Some story gifts rely on their original species and stone pairing. Windows
                // leaves the whole record untouched unless this option is enabled.
                if (!options.ReplaceMegaGifts)
                    continue;

                // Keep the Windows RNG call shape, but use the complete range so the last Mega
                // entry is reachable (the original used Count - 1 as the exclusive upper bound).
                var mega = megaOptions[Util.Rand.Next(megaOptions.Length)];
                species = mega.Species;
                gift.HeldItem = mega.Items[Util.Rand.Next(mega.Items.Length)];
            }
            else
            {
                species = speciesRandomizer.GetRandomSpecies(species);
                if (options.RandomizeHeldItems)
                    gift.HeldItem = itemPool[RandomIndex(itemPool.Length)];
            }

            if (options.RemoveShinyLocks)
                gift.ShinyLock = false;

            if (options.RandomizeLevels)
                gift.Level = (byte)GetModifiedLevel(gift.Level, options.LevelMultiplier);

            if (options.RandomizeAbilities)
                // The UI stores Ability 1/2/Hidden as 0/1/2; SelectedIndex is one higher.
                gift.Ability = (sbyte)(Util.Rand.Next(1, 4) - 1);

            if (options.ForceFullyEvolved && gift.Level >= options.FullyEvolvedLevel
                && finalEvolutions.Length > 0 && !finalEvolutions.Contains(species))
            {
                species = finalEvolutions[RandomIndex(finalEvolutions.Length)];
            }

            gift.Species = (ushort)species;
            gift.Form = (byte)formRandomizer.GetRandomForme(species);
            gift.Gender = 0;
            gift.Nature = -1;
            Array.Fill(gift.IVs, (sbyte)-1);

            // A gift carrying a Megastone must stay in its base form; the stone is used by the
            // game's battle logic to trigger the Mega Evolution.
            if (megaItems.Contains(gift.HeldItem) && gift.Form != 0)
                gift.Form = 0;

            Array.Copy(gift.Write(), 0, data, offset, GiftGen6Editor.GetSize(config.ORAS));
        }
    }

    private static (int Species, int[] Items)[] GetMegaOptions(bool oras) => oras
        ? [.. MegaOptionsXY, .. MegaOptionsORAS]
        : MegaOptionsXY;

    private static int[] GetRandomItemPool(bool oras)
    {
        var held = oras ? ItemsHeldORAS : ItemsHeldXY;
        return held.Concat(ItemsBall).Where(item => item != 0).ToArray();
    }

    private static int GetModifiedLevel(int level, decimal factor)
    {
        var modified = (int)(level * factor);
        return Math.Clamp(modified, 1, 100);
    }

    private static int RandomIndex(int length) => (int)(Util.Random32() % (uint)length);

    private static void ValidateRequest(GiftGen6RandomizeRequest request)
    {
        if (request.LevelMultiplier < 0m || request.LevelMultiplier > 3m)
            throw new WorkspaceException("El multiplicador de nivel debe estar entre 0.00 y 3.00.");
        if (request.FullyEvolvedLevel is < 1 or > 100)
            throw new WorkspaceException("El nivel para forzar evoluciones debe estar entre 1 y 100.");
    }

    private static void RequireCro(GameWorkspace workspace)
    {
        if (!File.Exists(Path.Combine(workspace.RomFsPath, CroFile)))
            throw new WorkspaceException($"Falta {CroFile} en el RomFS. Es necesario para randomizar regalos de Gen. VI.");
    }

    private static void RequireTable(GameWorkspace workspace, bool oras)
    {
        RequireTable(File.ReadAllBytes(Path.Combine(workspace.RomFsPath, CroFile)), oras);
    }

    private static void RequireTable(byte[] data, bool oras)
    {
        var minimumLength = (long)GiftGen6Editor.GetOffset(oras, GiftGen6Editor.GetCount(oras) - 1)
            + GiftGen6Editor.GetSize(oras);
        if (data.Length < minimumLength)
            throw new WorkspaceException($"{CroFile} no contiene la tabla de regalos completa.");
    }
}
