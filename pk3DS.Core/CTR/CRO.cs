using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace pk3DS.Core.CTR;

public sealed record CrrRebuildResult(byte[] Crr, byte[][] Cros, bool Changed);

public class CRO(byte[] data)
{
    // Utility
    internal static void UpdateTB(RichTextBox RTB, string progress)
    {
        try
        {
            if (RTB.InvokeRequired)
            {
                RTB.Invoke((MethodInvoker)delegate
                {
                    RTB.AppendText(Environment.NewLine + progress);
                    RTB.SelectionStart = RTB.Text.Length;
                    RTB.ScrollToCaret();
                });
            }
            else
            {
                RTB.SelectionStart = RTB.Text.Length;
                RTB.ScrollToCaret();
                RTB.AppendText(progress + Environment.NewLine);
            }
        }
        catch { }
    }

    internal static int IndexOfBytes(byte[] array, byte[] pattern, int startIndex, int count)
    {
        int i = startIndex;
        int endIndex = count > 0 ? startIndex + count : array.Length;
        int fidx = 0;

        while (i++ != endIndex - 1)
        {
            if (array[i] != pattern[fidx]) i -= fidx;
            fidx = array[i] == pattern[fidx] ? ++fidx : 0;
            if (fidx == pattern.Length)
                return i - fidx + 1;
        }
        return -1;
    }

    internal static string GetHexString(byte[] data)
    {
        return BitConverter.ToString(data).Replace("-", "");
    }

    internal static byte[] StringToByteArray(string hex)
    {
        return Enumerable.Range(0, hex.Length)
            .Where(x => x % 2 == 0)
            .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
            .ToArray();
    }

    // Checking
    internal static string[] VerifyCRR(string PATH_CRR, string PATH_CRO)
    {
        // Get CRO files
        string[] CROFiles = Directory.GetFiles(PATH_CRO);

        // Weed out anything that isn't a .cro
        var cros = new List<string>();
        for (int i = 0; i < CROFiles.Length; i++)
        {
            if (Path.GetExtension(CROFiles[i]) == ".cro")
                cros.Add(CROFiles[i]);
        }

        CROFiles = [.. cros];

        // Store Hashes as Strings (hacky way to sort byte[]'s against eachother
        string[] hashes = new string[CROFiles.Length];
        for (int i = 0; i < hashes.Length; i++)
        {
            byte[] data = File.ReadAllBytes(CROFiles[i]);
            byte[] hash = HashCRO(ref data);
            hashes[i] = GetHexString(hash).ToUpper();
        }
        Array.Sort(hashes, string.Compare);
        // Convert Hash Strings to Bytes
        byte[][] hashData = new byte[hashes.Length][];
        for (int i = 0; i < hashes.Length; i++)
            hashData[i] = StringToByteArray(hashes[i]);

        // Open the CRR
        byte[] CRR = File.ReadAllBytes(PATH_CRR);
        int hashTableOffset = BitConverter.ToInt32(CRR, 0x350);
        int hashCount = BitConverter.ToInt32(CRR, 0x354);

        // A little validation...
        if (hashCount != hashData.Length)
            throw new Exception($"Amount of input file-hashes does not equal the hash count in CRR. Expected {hashCount}, got {hashData.Length}.");

        string[] results = new string[hashData.Length];
        // Store Hashes in CRR
        for (int i = 0; i < hashData.Length; i++)
        {
            byte[] crrEntryHash = new byte[0x20];
            Array.Copy(CRR, (i * 0x20) + hashTableOffset, crrEntryHash, 0, 0x20);
            results[i] = "Hash @ {0} is " + (crrEntryHash.SequenceEqual(hashData[i]) ? "valid." : "invalid.");
            Array.Copy(hashData[i], 0, CRR, hashTableOffset + (0x20 * i), 0x20);
        }
        return results;
    }

    public static bool E_HashCRR(string PATH_CRR, string PATH_CRO, bool saveCRO = true, bool saveCRR = true, RichTextBox TB_Progress = null, ProgressBar PB_Show = null)
    {
        // Get CRO files
        string[] CROFiles = Directory.GetFiles(PATH_CRO);

        // Weed out anything that isn't a .cro
        CROFiles = CROFiles.Where(t => Path.GetExtension(t) == ".cro").ToArray();
        // Open the CRR
        byte[] CRR = File.ReadAllBytes(PATH_CRR);
        int hashTableOffset = BitConverter.ToInt32(CRR, 0x350);
        int hashCount = BitConverter.ToInt32(CRR, 0x354);

        // A little validation...
        if (hashCount != CROFiles.Length)
        {
            UpdateTB(TB_Progress,
                $"Amount of input file-hashes does not equal the hash count in CRR. Expected {hashCount}, got {CROFiles.Length}.");
            UpdateTB(TB_Progress, "Did not modify files. Aborting.");
            return false;
        }

        // Initialize Update Display
        TB_Progress ??= new RichTextBox();
        PB_Show ??= new ProgressBar();
        if (PB_Show.InvokeRequired)
        {
            PB_Show.Invoke((MethodInvoker)delegate { PB_Show.Minimum = 0; PB_Show.Step = 1; PB_Show.Value = 0; PB_Show.Maximum = CROFiles.Length; });
        }
        else { PB_Show.Minimum = 0; PB_Show.Step = 1; PB_Show.Value = 0; PB_Show.Maximum = CROFiles.Length; }
        UpdateTB(TB_Progress, "");
        UpdateTB(TB_Progress, "Computing hashes for " + CROFiles.Length + " CRO files.");

        // Store Hashes as Strings (hacky way to sort byte[]'s against eachother
        string[] hashes = new string[CROFiles.Length];
        for (int i = 0; i < hashes.Length; i++)
        {
            byte[] data = File.ReadAllBytes(CROFiles[i]);
            byte[] hash = HashCRO(ref data);
            hashes[i] = GetHexString(hash).ToUpper();
            if (saveCRO)
                File.WriteAllBytes(CROFiles[i], data);

            if (PB_Show.InvokeRequired)
            {
                PB_Show.Invoke((MethodInvoker)(() => PB_Show.PerformStep()));
            }
            else { PB_Show.PerformStep(); }
        }
        UpdateTB(TB_Progress, "Hashes computed, now sorting."); // Don't need to fiddle the ProgressBar because this should be quite quick.
        string[] hashCopy = (string[])hashes.Clone(); // Store an unsorted list for later.
        Array.Sort(hashes, string.Compare);
        // Convert Hash Strings to Bytes
        byte[][] hashData = new byte[hashes.Length][];
        for (int i = 0; i < hashes.Length; i++)
            hashData[i] = StringToByteArray(hashes[i]);

        UpdateTB(TB_Progress, "Hashes sorted, writing hashes to CRR.");

        // Loop to check which CROs have to be updated. Do this separate from overwriting so we don't overwrite hashes for other CROs (yet).
        int updatedCTR = 0;
        for (int i = 0; i < hashData.Length; i++)
        {
            // Check to see if the hash is currently in the table already.
            int index = IndexOfBytes(CRR, hashData[i], 0, CRR.Length);
            if (index < 0)
            {
                // CRO was updated.
                string file = CROFiles[Array.IndexOf(hashCopy, hashes[i])];
                UpdateTB(TB_Progress, $"{Path.GetFileName(file)} hash has been updated.");
                updatedCTR++;
            }
        }
        // Store Hashes in CRR
        for (int i = 0; i < hashData.Length; i++)
            Array.Copy(hashData[i], 0, CRR, hashTableOffset + (0x20 * i), 0x20);

        UpdateTB(TB_Progress,
            updatedCTR > 0
                ? $"{updatedCTR} hashes have been updated."
                : "CRR is fine. No modifications are necessary.");

        // Save File
        if (saveCRR && updatedCTR > 0)
        {
            File.WriteAllBytes(PATH_CRR, CRR);
            UpdateTB(TB_Progress, "Wrote CRR.");
        }
        else
        {
            UpdateTB(TB_Progress, "CRR has not been updated.");
        }
        return true;
    }

    internal static byte[] HashCRO(ref byte[] CRO)
    {
        // Allocate new byte array to store modified CRO

        // Compute the hashes
        byte[] hashH = SHA256.HashData(CRO.AsSpan(0x80, 0x100));
        byte[] hash0 = SHA256.HashData(CRO.AsSpan(BitConverter.ToInt32(CRO, 0xB0), BitConverter.ToInt32(CRO, 0xB4)));
        byte[] hash1 = SHA256.HashData(CRO.AsSpan(BitConverter.ToInt32(CRO, 0xC0), BitConverter.ToInt32(CRO, 0xB8) - BitConverter.ToInt32(CRO, 0xC0)));
        byte[] hash2 = SHA256.HashData(CRO.AsSpan(BitConverter.ToInt32(CRO, 0xB8), BitConverter.ToInt32(CRO, 0xBC)));

        // Set the hashes
        Array.Copy(hashH, 0, CRO, 0x00, 0x20);
        Array.Copy(hash0, 0, CRO, 0x20, 0x20);
        Array.Copy(hash1, 0, CRO, 0x40, 0x20);
        Array.Copy(hash2, 0, CRO, 0x60, 0x20);

        // Return the fixed overall hash
        return SHA256.HashData(CRO.AsSpan(0, 0x80));
    }

    /// <summary>Returns a copy of a CRO with its embedded SHA-256 fields recalculated.</summary>
    public static byte[] Rehash(byte[] cro)
    {
        var copy = (byte[])(cro ?? throw new ArgumentNullException(nameof(cro))).Clone();
        ValidateLayout(copy);
        HashCRO(ref copy);
        return copy;
    }

    /// <summary>Computes the CRR entry hash without modifying the caller's CRO bytes.</summary>
    public static byte[] ComputeHash(byte[] cro)
    {
        var copy = (byte[])(cro ?? throw new ArgumentNullException(nameof(cro))).Clone();
        ValidateLayout(copy);
        return HashCRO(ref copy);
    }

    /// <summary>
    /// Rebuilds a copy of a static CRR from the supplied CROs. CROs are returned with their own
    /// embedded hashes fixed, while neither input array is changed.
    /// </summary>
    public static CrrRebuildResult RebuildCRR(byte[] crr, IReadOnlyList<byte[]> cros)
    {
        if (crr is null)
            throw new ArgumentNullException(nameof(crr));
        if (cros is null)
            throw new ArgumentNullException(nameof(cros));
        if (crr.Length < 0x358)
            throw new ArgumentException("El CRR no contiene su tabla de hashes.", nameof(crr));

        var tableOffset = BitConverter.ToInt32(crr, 0x350);
        var count = BitConverter.ToInt32(crr, 0x354);
        if (tableOffset < 0 || count < 0 || tableOffset > crr.Length || count > (crr.Length - tableOffset) / 0x20)
            throw new ArgumentException("La tabla de hashes del CRR está fuera de rango.", nameof(crr));
        if (count != cros.Count)
            throw new ArgumentException($"El CRR espera {count} CROs, pero se recibieron {cros.Count}.", nameof(cros));

        var prepared = new byte[cros.Count][];
        var hashes = new byte[cros.Count][];
        for (var index = 0; index < cros.Count; index++)
        {
            prepared[index] = Rehash(cros[index]);
            hashes[index] = SHA256.HashData(prepared[index].AsSpan(0, 0x80));
        }
        var sorted = hashes.OrderBy(hash => Convert.ToHexString(hash), StringComparer.Ordinal).ToArray();
        var output = (byte[])crr.Clone();
        var changed = false;
        for (var index = 0; index < sorted.Length; index++)
        {
            var at = tableOffset + (index * 0x20);
            if (!output.AsSpan(at, 0x20).SequenceEqual(sorted[index]))
                changed = true;
            sorted[index].CopyTo(output, at);
        }
        return new CrrRebuildResult(output, prepared, changed);
    }

    private static void ValidateLayout(byte[] cro)
    {
        if (cro.Length < 0x180)
            throw new ArgumentException("El CRO es demasiado pequeño para contener sus hashes.", nameof(cro));
        var section0 = BitConverter.ToInt32(cro, 0xB0);
        var section0Length = BitConverter.ToInt32(cro, 0xB4);
        var section1 = BitConverter.ToInt32(cro, 0xC0);
        var section2 = BitConverter.ToInt32(cro, 0xB8);
        var section2Length = BitConverter.ToInt32(cro, 0xBC);
        if (section0 < 0 || section0Length < 0 || section1 < 0 || section2 < section1 || section2Length < 0
            || section0 > cro.Length - section0Length || section2 > cro.Length - section2Length || section2 < section1)
            throw new ArgumentException("Las secciones declaradas del CRO están fuera de rango.", nameof(cro));
    }

    private readonly byte[] Data = (byte[])data.Clone();

    public byte[] HashSHA2
    {
        get
        {
            byte[] hashData = new byte[0x80];
            Array.Copy(Data, hashData, 0x80);
            return hashData;
        }
        set
        {
            if (value.Length != 0x80)
                throw new ArgumentOutOfRangeException(value.Length.ToString("X5"));
            Array.Copy(value, Data, value.Length);
        }
    }

    public string Magic => new(Data.Skip(0x80).Take(4).Select(c => (char)c).ToArray());
}
