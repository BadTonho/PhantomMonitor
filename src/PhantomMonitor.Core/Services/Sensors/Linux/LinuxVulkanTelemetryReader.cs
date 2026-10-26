using System.Buffers.Binary;
using System.Text;

namespace PhantomMonitor.Services.Linux;

internal readonly record struct VulkanFrame(ulong Number, ulong TimestampNs, float FrametimeMs, float Fps);
internal sealed record VulkanTelemetrySnapshot(uint Pid, string Name, ulong Session, ulong FrameCount,
    ulong TimestampNs, IReadOnlyList<VulkanFrame> Frames, bool Legacy);

/// <summary>Reads v1 legacy samples and stable v2 per-frame rings without losing intervening presents.</summary>
internal static class LinuxVulkanTelemetryReader
{
    internal const int Capacity = 4096;
    internal const int Size = 120 + Capacity * 24;
    private const uint Magic = PhantomVulkanTelemetryHeader.ExpectedMagic;

    internal static VulkanTelemetrySnapshot? Read(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length is < 100 or > Size) return null;
            var bytes = new byte[(int)file.Length];
            file.ReadExactly(bytes);
            ulong after = 0;
            if (bytes.Length >= 120 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) == 2)
            {
                Span<byte> sequence = stackalloc byte[8];
                file.Position = 104;
                file.ReadExactly(sequence);
                after = BinaryPrimitives.ReadUInt64LittleEndian(sequence);
            }
            return Parse(bytes, after);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static VulkanTelemetrySnapshot? Parse(ReadOnlySpan<byte> bytes, ulong sequenceAfter)
    {
        if (bytes.Length < 100 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic) return null;
        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        if (version is not (1 or 2)) return null;
        var pid = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        var count = BinaryPrimitives.ReadUInt64LittleEndian(bytes[12..]);
        var timestamp = BinaryPrimitives.ReadUInt64LittleEndian(bytes[20..]);
        var nameBytes = bytes.Slice(36, 64);
        var terminator = nameBytes.IndexOf((byte)0);
        var name = Encoding.UTF8.GetString(terminator >= 0 ? nameBytes[..terminator] : nameBytes);
        if (pid == 0 || count == 0 || timestamp == 0) return null;
        if (version == 1)
        {
            var ft = BinaryPrimitives.ReadSingleLittleEndian(bytes[28..]);
            var fps = BinaryPrimitives.ReadSingleLittleEndian(bytes[32..]);
            if (!Valid(ft, fps)) return null;
            return new(pid, name, 0, count, timestamp, [new(count, timestamp, ft, fps)], true);
        }
        if (count > ulong.MaxValue / 2 || bytes.Length != Size || BinaryPrimitives.ReadUInt32LittleEndian(bytes[100..]) != Capacity) return null;
        var sequence = BinaryPrimitives.ReadUInt64LittleEndian(bytes[104..]);
        if (sequence == 0 || (sequence & 1) != 0 || sequence != sequenceAfter || sequence != count * 2) return null;
        var session = BinaryPrimitives.ReadUInt64LittleEndian(bytes[112..]);
        if (session == 0) return null;
        var frames = new List<VulkanFrame>((int)Math.Min(count, Capacity));
        var first = count >= Capacity ? count - Capacity + 1 : 1;
        for (var number = first; number <= count; number++)
        {
            var slot = bytes.Slice(120 + (int)((number - 1) % Capacity) * 24, 24);
            var actual = BinaryPrimitives.ReadUInt64LittleEndian(slot);
            var time = BinaryPrimitives.ReadUInt64LittleEndian(slot[8..]);
            var ft = BinaryPrimitives.ReadSingleLittleEndian(slot[16..]);
            var fps = BinaryPrimitives.ReadSingleLittleEndian(slot[20..]);
            if (actual != number || time == 0 || time > timestamp || !Valid(ft, fps)) return null;
            frames.Add(new(number, time, ft, fps));
        }
        return new(pid, name, session, count, timestamp, frames, false);
    }

    private static bool Valid(float ft, float fps) => float.IsFinite(ft) && float.IsFinite(fps)
        && ft > 0 && fps >= 0 && (ft <= 0.001f ? fps == 0 : MathF.Abs(ft * fps - 1000f) <= 5f);
}
