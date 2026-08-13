using System.Buffers.Binary;
using pk3DS.Core;

namespace pk3DS.Editors;

/// <summary>
/// Pickup item tables for Gen VII. The game stores one row per item and ten percentage columns
/// covering levels 1-10, 11-20, ..., 91-100. The GARC entries may be LZSS-compressed, so this
/// editor intentionally uses <see cref="GameConfig.GetlzGARCData"/> rather than a plain GARC.
/// </summary>
public static class PickupEditor
{
    private const int FileIndex = 0;
    private const int ColumnCount = 10;
    private const int RowSize = sizeof(ushort) + ColumnCount;
    private const int HeaderSize = sizeof(ushort) + sizeof(ushort);

    public static PickupTableResponse GetTable(PickupTableRequest request)
    {
        var (_, config) = EditorSession.OpenReadOnly(request.WorkspacePath, request.Language);
        EnsureSupportedGame(config);
        var pickup = config.GetlzGARCData("pickup");
        return new PickupTableResponse(Read(pickup), Catalogs.Items(config));
    }

    public static ExportResult Export(PickupExportRequest request) =>
        EditorSession.Export(request.WorkspacePath, request.OutputDirectory, request.TitleId, request.Language,
            "pickup", ["pickup"], config =>
            {
                EnsureSupportedGame(config);
                var pickup = config.GetlzGARCData("pickup");
                var entries = Validate(request.Entries, Catalogs.ItemCount(config));
                pickup[FileIndex] = Write(entries);
                pickup.Save();
                return [config.GetGARCFileName("pickup")];
            });

    private static PickupEntry[] Read(LazyGARCFile pickup)
    {
        if (pickup.FileCount <= FileIndex)
            throw new WorkspaceException("El archivo de objetos de Recogida no tiene el formato esperado.");

        var data = pickup[FileIndex];
        if (data.Length < HeaderSize)
            throw new WorkspaceException("El archivo de objetos de Recogida está incompleto.");

        var encodedCount = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (encodedCount == 0)
            throw new WorkspaceException("El archivo de objetos de Recogida tiene una cabecera inválida.");
        var rowCount = encodedCount - 1;
        var requiredLength = HeaderSize + (rowCount * RowSize);
        if (rowCount > 0 && requiredLength > data.Length)
            throw new WorkspaceException("El archivo de objetos de Recogida está truncado.");

        var entries = new PickupEntry[rowCount];
        for (var row = 0; row < rowCount; row++)
        {
            var offset = HeaderSize + (row * RowSize);
            var item = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)));
            var rates = data.AsSpan(offset + sizeof(ushort), ColumnCount).ToArray().Select(value => (int)value).ToArray();
            entries[row] = new PickupEntry(item, rates);
        }
        return entries;
    }

    private static byte[] Write(PickupEntry[] entries)
    {
        var data = new byte[HeaderSize + (entries.Length * RowSize)];
        BinaryPrimitives.WriteUInt16LittleEndian(data, checked((ushort)(entries.Length + 1)));
        for (var row = 0; row < entries.Length; row++)
        {
            var offset = HeaderSize + (row * RowSize);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)), checked((ushort)entries[row].Item));
            entries[row].Rates.Select(rate => (byte)rate).ToArray().CopyTo(data, offset + sizeof(ushort));
        }
        return data;
    }

    /// <summary>Returns a non-null, range-checked payload ready to write to the compressed GARC.</summary>
    internal static PickupEntry[] Validate(PickupEntry[]? entries, int itemCount)
    {
        if (entries is null || entries.Length > ushort.MaxValue - 1 || entries.Any(entry =>
            entry is null || entry.Item < 0 || entry.Item >= itemCount || entry.Rates is not { Length: ColumnCount }
            || entry.Rates.Any(rate => rate is < 0 or > 100)))
            throw new WorkspaceException("La tabla de objetos de Recogida no es válida.");

        for (var column = 0; column < ColumnCount; column++)
        {
            var total = entries.Sum(entry => entry.Rates[column]);
            if (total != 100)
                throw new WorkspaceException($"La columna de niveles {column + 1} debe sumar 100 (suma actual: {total}).");
        }
        return entries;
    }

    private static void EnsureSupportedGame(GameConfig config)
    {
        if (config.Version is not (GameVersion.SM or GameVersion.USUM))
            throw new WorkspaceException("El editor de objetos de Recogida está disponible para Sol/Luna y Ultrasol/Ultraluna.");
    }
}
