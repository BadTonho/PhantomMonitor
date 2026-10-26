using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PhantomMonitor.App.Services.Overlay;

/// <summary>Read-side checks shared by diagnostics/tests and mirrored by native renderers.</summary>
internal static class InGameOverlayFrameProtocol
{
    internal const int Capacity = 1024 * 1024;
    internal const int HeaderSize = 32;
    internal const uint Magic = 0x4F534850; // "PHSO" in little endian.
    internal const uint Version = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static bool TryCopyStableFrame(ReadOnlySpan<byte> mapping, long nowUnixMilliseconds,
        TimeSpan maximumAge, out byte[] payload)
    {
        payload = [];
        if (mapping.Length < HeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(mapping) != Magic
            || BinaryPrimitives.ReadUInt32LittleEndian(mapping[4..]) != Version)
            return false;

        var sequenceBefore = BinaryPrimitives.ReadInt64LittleEndian(mapping[8..]);
        if (sequenceBefore <= 0 || (sequenceBefore & 1) != 0) return false;

        var timestamp = BinaryPrimitives.ReadInt64LittleEndian(mapping[16..]);
        var length = BinaryPrimitives.ReadInt32LittleEndian(mapping[24..]);
        if (length < 0 || length > Capacity - HeaderSize || mapping.Length - HeaderSize < length)
            return false;

        var frame = mapping.Slice(HeaderSize, length).ToArray();
        Thread.MemoryBarrier();
        var sequenceAfter = BinaryPrimitives.ReadInt64LittleEndian(mapping[8..]);
        if (sequenceBefore != sequenceAfter || (sequenceAfter & 1) != 0) return false;

        var age = nowUnixMilliseconds - timestamp;
        if (timestamp <= 0 || age < -1000 || age > maximumAge.TotalMilliseconds)
            return false;
        if (!IsValidPayload(frame)) return false;

        payload = frame;
        return true;
    }

    private static bool IsValidPayload(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var text = StrictUtf8.GetString(bytes);
            if (text.Length == 0 || text[^1] != '\n') return false;
            var lines = text.Split('\n');
            if (lines.Length < 2 || lines[^1].Length != 0) return false;

            var frame = lines[0].Split('\t');
            if (frame.Length != 31 || frame[0] != "F" || frame[1] is not ("1" or "2")
                || !Flag(frame[2]) || !Flag(frame[3])
                || !Positive(frame[4]) || !Positive(frame[5]) || !Positive(frame[6])
                || !Positive(frame[7]) || !Range(frame[8], 0, 1)
                || !Color(frame[9]) || !Flag(frame[10]) || !Color(frame[11]) || !Color(frame[12])
                || !Flag(frame[13]) || !Base64(frame[14]) || !Base64(frame[15]) || !Positive(frame[16])
                || !Numbers(frame, 17, 4) || !Numbers(frame, 21, 4)
                || !NonNegative(frame[25]) || !Numbers(frame, 26, 4) || !Positive(frame[30]))
                return false;

            var insideBlock = false;
            for (var index = 1; index < lines.Length - 1; index++)
            {
                var fields = lines[index].Split('\t');
                if (fields[0] == "B")
                {
                    if (insideBlock || fields.Length != 5 || !Base64(fields[1], allowEmpty: false)
                        || !Range(fields[2], -5000, 5000) || !Range(fields[3], -5000, 5000)
                        || !Base64(fields[4])) return false;
                    insideBlock = true;
                    continue;
                }

                if (fields[0] == "E")
                {
                    if (!insideBlock || fields.Length != 1) return false;
                    insideBlock = false;
                    continue;
                }

                if (!insideBlock) return false;
                if (fields[0] is "N" or "T")
                {
                    if (fields.Length != 4 || !Positive(fields[1]) || !Color(fields[2]) || !Base64(fields[3]))
                        return false;
                }
                else if (frame[1] == "2" && fields[0] == "W")
                {
                    if (fields.Length != 5 || !Range(fields[1], 1, 256) || !Color(fields[2])
                        || !Base64(fields[3]) || !Range(fields[4], 1, 10000)) return false;
                }
                else if (fields[0] == "G")
                {
                    if (fields.Length != 6 || !Positive(fields[1]) || !Color(fields[2])
                        || fields[3].Length > 0 && !Number(fields[3])
                        || fields[4].Length > 0 && !Number(fields[4])) return false;
                    if (fields[5].Length > 0 && !fields[5].Split(',').All(Number)) return false;
                }
                else return false;
            }

            return !insideBlock;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool Flag(string value) => value is "0" or "1";
    private static bool Color(string value) => value.Length == 8 && value.All(Uri.IsHexDigit);
    private static bool Base64(string value, bool allowEmpty = true)
    {
        if (!allowEmpty && value.Length == 0) return false;
        try { Convert.FromBase64String(value); return true; }
        catch (FormatException) { return false; }
    }
    private static bool Numbers(string[] fields, int offset, int count)
        => Enumerable.Range(offset, count).All(index => NonNegative(fields[index]));
    private static bool Positive(string value) => Number(value) && double.Parse(value, CultureInfo.InvariantCulture) > 0;
    private static bool NonNegative(string value) => Number(value) && double.Parse(value, CultureInfo.InvariantCulture) >= 0;
    private static bool Range(string value, double minimum, double maximum)
        => Number(value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= minimum && parsed <= maximum;
    private static bool Number(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed);
}
