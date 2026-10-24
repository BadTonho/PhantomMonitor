using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace PhantomMonitor.App.Services.Overlay;

internal sealed record InGameOverlayNativeStatus(
    int ProcessId,
    uint Api,
    uint State,
    long UpdatedUnixMilliseconds,
    string Message)
{
    internal string ApiName => Api switch
    {
        1 => "Vulkan",
        2 => "Direct3D 11",
        3 => "Direct3D 12",
        4 => "OpenGL",
        5 => "Direct3D 9",
        6 => "Direct3D 10",
        _ => "API desconhecida"
    };

    internal string StateName => State switch
    {
        0 => "aguardando",
        1 => "desenhando",
        2 => "oculto",
        3 => "quadro expirado",
        4 => "não suportado",
        5 => "falha",
        _ => "estado desconhecido"
    };
}

/// <summary>Reads the per-process native renderer status channel.</summary>
internal static class InGameOverlayNativeStatusReader
{
    internal const int Capacity = 292;
    internal const uint Magic = 0x53504850;
    internal const uint Version = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<InGameOverlayNativeStatus> ReadLinux()
    {
        if (!OperatingSystem.IsLinux()) return [];
        var uid = GetUid();
        var prefix = $"phantommonitor_overlay_status_v1_{uid}_";
        if (!Directory.Exists("/dev/shm")) return [];
        var statuses = new List<InGameOverlayNativeStatus>();
        var sequenceBytes = new byte[8];
        foreach (var path in Directory.EnumerateFiles("/dev/shm", $"{prefix}*"))
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var suffix = name[prefix.Length..];
            var separator = suffix.IndexOf('_');
            if (separator < 0 || !int.TryParse(suffix[..separator], out var pid)
                || !Directory.Exists($"/proc/{pid}")) continue;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var bytes = new byte[Capacity];
                stream.Position = 8;
                stream.ReadExactly(sequenceBytes);
                var sequenceBefore = BinaryPrimitives.ReadInt64LittleEndian(sequenceBytes);
                if (sequenceBefore <= 0 || (sequenceBefore & 1) != 0) continue;
                stream.Position = 0;
                stream.ReadExactly(bytes);
                Thread.MemoryBarrier();
                stream.Position = 8;
                stream.ReadExactly(sequenceBytes);
                var sequenceAfter = BinaryPrimitives.ReadInt64LittleEndian(sequenceBytes);
                if (TryParseSnapshot(bytes, sequenceBefore, sequenceAfter, out var status)
                    && status.ProcessId == pid)
                    statuses.Add(status);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return statuses.OrderBy(status => status.ProcessId).ToArray();
    }

    internal static IReadOnlyList<InGameOverlayNativeStatus> ReadWindows(IEnumerable<int> processIds)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var statuses = new List<InGameOverlayNativeStatus>();
        foreach (var pid in processIds.Distinct())
        {
            foreach (var producer in new[] { "vulkan", "opengl", "windows" })
                if (TryReadWindows(pid, producer, out var status)) statuses.Add(status);
        }
        return statuses.OrderBy(status => status.ProcessId).ToArray();
    }

    internal static bool TryParseSnapshot(ReadOnlySpan<byte> bytes, out InGameOverlayNativeStatus status)
    {
        if (bytes.Length < Capacity)
        {
            status = default!;
            return false;
        }
        var sequence = BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]);
        return TryParseSnapshot(bytes, sequence, sequence, out status);
    }

    private static bool TryParseSnapshot(ReadOnlySpan<byte> bytes, long sequenceBefore,
        long sequenceAfter, out InGameOverlayNativeStatus status)
    {
        status = default!;
        if (bytes.Length < Capacity
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != Version)
            return false;
        var snapshotSequence = BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]);
        if (sequenceBefore <= 0 || (sequenceBefore & 1) != 0) return false;
        var pid = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);
        var api = BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]);
        var state = BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]);
        var updated = BinaryPrimitives.ReadInt64LittleEndian(bytes[28..]);
        var messageBytes = bytes.Slice(36, 256);
        var nullIndex = messageBytes.IndexOf((byte)0);
        if (nullIndex >= 0) messageBytes = messageBytes[..nullIndex];
        string message;
        try { message = StrictUtf8.GetString(messageBytes); }
        catch (DecoderFallbackException) { return false; }
        Thread.MemoryBarrier();
        var statusAge = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - updated;
        if (sequenceBefore != sequenceAfter || sequenceAfter != snapshotSequence || (sequenceAfter & 1) != 0
            || pid <= 0 || api > 6 || state > 5 || updated <= 0 || statusAge is < -1000 or > 30_000
            || message.Length == 0)
            return false;
        status = new InGameOverlayNativeStatus(pid, api, state, updated, message);
        return true;
    }

    private static bool TryReadWindows(int pid, string producer, out InGameOverlayNativeStatus status)
    {
        status = default!;
        var mapping = OpenFileMapping(FileMapRead, false,
            $"Local\\PhantomMonitor.InGameOverlay.Status.v1.{pid}.{producer}");
        if (mapping == IntPtr.Zero) return false;
        try
        {
            var view = MapViewOfFile(mapping, FileMapRead, 0, 0, (UIntPtr)Capacity);
            if (view == IntPtr.Zero) return false;
            try
            {
                var sequenceBefore = Marshal.ReadInt64(view, 8);
                var bytes = new byte[Capacity];
                Marshal.Copy(view, bytes, 0, bytes.Length);
                Thread.MemoryBarrier();
                var sequenceAfter = Marshal.ReadInt64(view, 8);
                return TryParseSnapshot(bytes, sequenceBefore, sequenceAfter, out status)
                    && status.ProcessId == pid;
            }
            finally { UnmapViewOfFile(view); }
        }
        finally { CloseHandle(mapping); }
    }

    private const uint FileMapRead = 0x0004;

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenFileMapping(uint access, bool inheritHandle, string name);
    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access,
        uint offsetHigh, uint offsetLow, UIntPtr bytesToMap);
    [DllImport("kernel32", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr address);
    [DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
