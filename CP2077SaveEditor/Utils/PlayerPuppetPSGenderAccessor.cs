using WolvenKit.Common.FNV1A;
using WolvenKit.RED4.Save;
using WolvenKit.RED4.Types;

namespace CP2077SaveEditor.Utils;

public static class PlayerPuppetPSGenderAccessor
{
    private const ulong PlayerPuppetPSClassHash = 0x5665839E94D6FD83;
    private const ulong PlayerPuppetEntryId = 0x0000000000000001;

    private static readonly byte[] GenderPropNameHash;
    private static readonly byte[] CNameTypeHash;
    private static readonly byte[] MaleCName;
    private static readonly byte[] FemaleCName;

    static PlayerPuppetPSGenderAccessor()
    {
        GenderPropNameHash = BitConverter.GetBytes(FNV1A64HashAlgorithm.HashString("gender"));
        CNameTypeHash = BitConverter.GetBytes(FNV1A64HashAlgorithm.HashString("CName"));
        MaleCName = BitConverter.GetBytes(FNV1A64HashAlgorithm.HashString("Male"));
        FemaleCName = BitConverter.GetBytes(FNV1A64HashAlgorithm.HashString("Female"));
    }

    public static string? GetBodyGender(CyberpunkSaveFile saveFile)
    {
        var entry = FindPlayerPuppetPSEntry(saveFile);
        if (entry == null)
            return null;

        if (entry.Data is PlayerPuppetPS typed)
            return ResolveGenderFromCName(typed.Gender);

        if (entry.Data is not UnknownRedClass urc || urc.Hash != PlayerPuppetPSClassHash)
            return null;

        var buf = urc.Buffer;
        if (buf.Length < 24)
            return "";

        if (!BufMatch(buf, 0, GenderPropNameHash))
            return "";

        if (!BufMatch(buf, 8, CNameTypeHash))
            return null;

        if (BufMatch(buf, 16, MaleCName))
            return "Male";

        if (BufMatch(buf, 16, FemaleCName))
            return "Female";

        return null;
    }

    public static SetBodyGenderResult SetBodyGender(CyberpunkSaveFile saveFile, string targetGender)
    {
        if (targetGender != "Male" && targetGender != "Female")
            throw new ArgumentException("targetGender must be 'Male' or 'Female'", nameof(targetGender));

        var entry = FindPlayerPuppetPSEntry(saveFile);
        if (entry == null)
            return SetBodyGenderResult.NotFound;

        if (entry.Data is PlayerPuppetPS typed)
        {
            var current = ResolveGenderFromCName(typed.Gender);
            if (current == targetGender)
                return SetBodyGenderResult.AlreadyCorrect;
            typed.Gender = (CName)targetGender;
            return SetBodyGenderResult.Updated;
        }

        if (entry.Data is not UnknownRedClass urc || urc.Hash != PlayerPuppetPSClassHash)
            return SetBodyGenderResult.WrongClassHash;

        var buf = urc.Buffer;
        if (buf.Length == 0)
            return SetBodyGenderResult.EmptyBuffer;

        if (buf.Length >= 24 && BufMatch(buf, 0, GenderPropNameHash))
        {
            if (!BufMatch(buf, 8, CNameTypeHash))
                throw new InvalidOperationException(
                    "PlayerPuppetPS buffer gender property has unexpected type hash. " +
                    "Expected CName (0xA5E23DE2A2657AF9). Buffer may be from an unsupported game version.");

            var isMale = BufMatch(buf, 16, MaleCName);
            var isFemale = BufMatch(buf, 16, FemaleCName);

            if (!isMale && !isFemale)
                throw new InvalidOperationException(
                    "PlayerPuppetPS buffer gender CName is neither Male nor Female. " +
                    "Buffer may be corrupted or from an unsupported game version.");

            var currentGender = isMale ? "Male" : "Female";
            if (currentGender == targetGender)
                return SetBodyGenderResult.AlreadyCorrect;

            var targetBytes = targetGender == "Male" ? MaleCName : FemaleCName;
            Buffer.BlockCopy(targetBytes, 0, buf, 16, 8);
            return SetBodyGenderResult.Updated;
        }

        if (targetGender == "Female")
            return SetBodyGenderResult.AlreadyCorrect;

        var newBuf = new byte[24 + buf.Length];
        Buffer.BlockCopy(GenderPropNameHash, 0, newBuf, 0, 8);
        Buffer.BlockCopy(CNameTypeHash, 0, newBuf, 8, 8);
        Buffer.BlockCopy(MaleCName, 0, newBuf, 16, 8);
        Buffer.BlockCopy(buf, 0, newBuf, 24, buf.Length);
        urc.Buffer = newBuf;
        return SetBodyGenderResult.Updated;
    }

    private static PS2Entry? FindPlayerPuppetPSEntry(CyberpunkSaveFile saveFile)
    {
        PersistencySystem2? ps = null;
        for (var i = 0; i < saveFile.Nodes.Count; i++)
        {
            if (saveFile.Nodes[i].Value is PersistencySystem2 p)
            {
                ps = p;
                break;
            }
        }

        if (ps == null)
            return null;

        PS2Entry? fallbackEntry = null;

        for (var i = 0; i < ps.Entries.Count; i++)
        {
            var entry = ps.Entries[i];
            if (entry == null || entry.Data == null)
                continue;

            if (entry.Id == PlayerPuppetEntryId)
            {
                if (entry.Data is UnknownRedClass urc && urc.Hash == PlayerPuppetPSClassHash)
                    return entry;

                if (entry.Data is PlayerPuppetPS)
                    return entry;

                fallbackEntry = entry;
            }
        }

        for (var i = 0; i < ps.Entries.Count; i++)
        {
            var entry = ps.Entries[i];
            if (entry?.Data is UnknownRedClass urc && urc.Hash == PlayerPuppetPSClassHash)
                return entry;
        }

        return fallbackEntry;
    }

    private static string? ResolveGenderFromCName(CName gender)
    {
        var value = (ulong)gender;
        var maleHash = FNV1A64HashAlgorithm.HashString("Male");
        var femaleHash = FNV1A64HashAlgorithm.HashString("Female");

        if (value == maleHash)
            return "Male";
        if (value == femaleHash)
            return "Female";
        if (value == 0)
            return "";

        return null;
    }

    private static bool BufMatch(byte[] buf, int offset, byte[] expected)
    {
        for (var i = 0; i < expected.Length; i++)
        {
            if (buf[offset + i] != expected[i])
                return false;
        }
        return true;
    }
}

public enum SetBodyGenderResult
{
    Updated,
    AlreadyCorrect,
    NotFound,
    WrongClassHash,
    EmptyBuffer
}
