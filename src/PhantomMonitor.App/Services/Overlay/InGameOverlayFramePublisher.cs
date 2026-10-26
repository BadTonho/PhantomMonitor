using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
using Avalonia.Media;
using PhantomMonitor.App.ViewModels;

namespace PhantomMonitor.App.Services.Overlay;

/// <summary>Publishes the latest applied OSD as a versioned read-only native frame.</summary>
internal sealed class InGameOverlayFramePublisher : IDisposable
{
    internal const int Capacity = InGameOverlayFrameProtocol.Capacity;
    internal const int HeaderSize = InGameOverlayFrameProtocol.HeaderSize;
    internal const uint Magic = InGameOverlayFrameProtocol.Magic;
    internal const uint Version = InGameOverlayFrameProtocol.Version;
    internal const string WindowsMappingName = "Local\\PhantomMonitor.InGameOverlay.v1";
    internal const string LinuxMappingNamePrefix = "/phantommonitor_overlay_v1_";

    private readonly object _gate = new();
    private IntPtr _mapping = IntPtr.Zero;
    private IntPtr _view = IntPtr.Zero;
    private int _linuxFd = -1;
    private long _sequence;
    private bool _disposed;

    public string Status { get; private set; } = "Canal nativo aguardando o modo dentro do jogo.";

    public bool Publish(byte[] payload)
    {
        if (payload.Length > Capacity - HeaderSize)
        {
            Status = "Quadro do overlay excedeu o limite do canal nativo.";
            return false;
        }

        lock (_gate)
        {
            if (_disposed) return false;
            if (_view == IntPtr.Zero && !Open()) return false;

            var odd = _sequence + 1;
            if ((odd & 1) == 0) odd++;
            Marshal.WriteInt32(_view, 0, unchecked((int)Magic));
            Marshal.WriteInt32(_view, 4, unchecked((int)Version));
            Marshal.WriteInt64(_view, 8, odd);
            Thread.MemoryBarrier();
            Marshal.Copy(payload, 0, IntPtr.Add(_view, HeaderSize), payload.Length);
            Marshal.WriteInt64(_view, 16, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Marshal.WriteInt32(_view, 24, payload.Length);
            Marshal.WriteInt32(_view, 28, 0);
            Thread.MemoryBarrier();
            Marshal.WriteInt64(_view, 8, odd + 1);
            _sequence = odd + 1;
            Status = $"Canal v1 atualizado · {payload.Length} bytes; o renderer nativo ainda não confirmou desenho.";
            return true;
        }
    }

    private bool Open()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _mapping = CreateFileMapping(new IntPtr(-1), IntPtr.Zero, PageReadWrite,
                    0, Capacity, WindowsMappingName);
                if (_mapping == IntPtr.Zero) throw new InvalidOperationException($"CreateFileMapping falhou ({Marshal.GetLastWin32Error()}).");
                _view = MapViewOfFile(_mapping, FileMapRead | FileMapWrite, 0, 0, (UIntPtr)Capacity);
                if (_view == IntPtr.Zero) throw new InvalidOperationException($"MapViewOfFile falhou ({Marshal.GetLastWin32Error()}).");
                var oldMagic = unchecked((uint)Marshal.ReadInt32(_view, 0));
                var oldVersion = unchecked((uint)Marshal.ReadInt32(_view, 4));
                var oldSequence = Marshal.ReadInt64(_view, 8);
                if (oldMagic == Magic && oldVersion == Version && oldSequence > 0)
                    _sequence = oldSequence;
            }
            else if (OperatingSystem.IsLinux())
            {
                var name = LinuxMappingNamePrefix + GetUid().ToString(CultureInfo.InvariantCulture);
                // Reaproveita a mesma região para que um renderer que já a mapeou
                // continue recebendo quadros se o app for reiniciado com o jogo aberto.
                _linuxFd = ShmOpen(name, O_RDWR, 0x180);
                if (_linuxFd < 0)
                {
                    var existingReadHandle = ShmOpen(name, O_RDONLY, 0);
                    if (existingReadHandle >= 0)
                    {
                        if (FChmod(existingReadHandle, 0x180) != 0)
                        {
                            CloseFd(existingReadHandle);
                            throw new InvalidOperationException($"fchmod para reabrir o canal falhou ({Marshal.GetLastWin32Error()}).");
                        }
                        CloseFd(existingReadHandle);
                        _linuxFd = ShmOpen(name, O_RDWR, 0x180);
                    }
                }
                if (_linuxFd < 0)
                    _linuxFd = ShmOpen(name, O_CREAT | O_RDWR, 0x180); // 0600
                if (_linuxFd < 0) throw new InvalidOperationException($"shm_open falhou ({Marshal.GetLastWin32Error()}).");
                if (FTruncate(_linuxFd, Capacity) != 0) throw new InvalidOperationException($"ftruncate falhou ({Marshal.GetLastWin32Error()}).");
                _view = MMap(IntPtr.Zero, (UIntPtr)Capacity, ProtRead | ProtWrite, MapShared, _linuxFd, 0);
                if (_view == new IntPtr(-1)) _view = IntPtr.Zero;
                if (_view == IntPtr.Zero) throw new InvalidOperationException($"mmap falhou ({Marshal.GetLastWin32Error()}).");
                var oldMagic = unchecked((uint)Marshal.ReadInt32(_view, 0));
                var oldVersion = unchecked((uint)Marshal.ReadInt32(_view, 4));
                var oldSequence = Marshal.ReadInt64(_view, 8);
                if (oldMagic == Magic && oldVersion == Version && oldSequence > 0)
                    _sequence = oldSequence;
                else
                {
                    Marshal.WriteInt32(_view, 0, unchecked((int)Magic));
                    Marshal.WriteInt32(_view, 4, unchecked((int)Version));
                    Marshal.WriteInt64(_view, 8, 0);
                    Marshal.WriteInt64(_view, 16, 0);
                    Marshal.WriteInt32(_view, 24, 0);
                    Marshal.WriteInt32(_view, 28, 0);
                }
                // O app conserva o descritor e o mapeamento de escrita já abertos;
                // novos processos só podem abrir o objeto para leitura.
                if (FChmod(_linuxFd, 0x100) != 0) throw new InvalidOperationException($"fchmod (somente leitura) falhou ({Marshal.GetLastWin32Error()})."); // 0400
            }
            else
            {
                Status = "O canal nativo do overlay não oferece suporte a este sistema.";
                return false;
            }

            Status = "Canal nativo do overlay conectado.";
            return true;
        }
        catch (Exception ex)
        {
            CloseHandles();
            Status = $"Falha ao abrir canal nativo: {ex.Message}";
            return false;
        }
    }

    private void CloseHandles()
    {
        if (_view != IntPtr.Zero)
        {
            if (OperatingSystem.IsWindows()) UnmapViewOfFile(_view);
            else Munmap(_view, (UIntPtr)Capacity);
            _view = IntPtr.Zero;
        }
        if (_mapping != IntPtr.Zero)
        {
            CloseHandle(_mapping);
            _mapping = IntPtr.Zero;
        }
        if (_linuxFd >= 0)
        {
            CloseFd(_linuxFd);
            _linuxFd = -1;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CloseHandles();
        }
    }

    private const uint PageReadWrite = 0x04;
    private const uint FileMapRead = 0x0004;
    private const uint FileMapWrite = 0x0002;
    private const int O_CREAT = 0x40;
    private const int O_RDWR = 0x2;
    private const int O_RDONLY = 0x0;
    private const int ProtRead = 0x1;
    private const int ProtWrite = 0x2;
    private const int MapShared = 0x01;

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileMapping(IntPtr file, IntPtr attributes, uint protect,
        uint maxSizeHigh, uint maxSizeLow, string name);
    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh,
        uint offsetLow, UIntPtr bytesToMap);
    [DllImport("kernel32", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr address);
    [DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("libc", SetLastError = true)]
    private static extern int shm_open(string name, int flags, uint mode);
    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);
    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr mmap(IntPtr address, UIntPtr length, int protection, int flags, int fd, long offset);
    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(IntPtr address, UIntPtr length);
    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
    [DllImport("libc", SetLastError = true)]
    private static extern int fchmod(int fd, uint mode);
    [DllImport("libc")]
    private static extern uint getuid();

    private static int ShmOpen(string name, int flags, uint mode) => shm_open(name, flags, mode);
    private static int FTruncate(int fd, long length) => ftruncate(fd, length);
    private static IntPtr MMap(IntPtr address, UIntPtr length, int protection, int flags, int fd, long offset)
        => mmap(address, length, protection, flags, fd, offset);
    private static int Munmap(IntPtr address, UIntPtr length) => munmap(address, length);
    private static int CloseFd(int fd) => close(fd);
    private static int FChmod(int fd, uint mode) => fchmod(fd, mode);
    private static uint GetUid() => getuid();
}

internal sealed record InGameOverlayPresentation(
    bool Visible,
    bool Paused,
    double Width,
    double ReferenceWidth,
    double ReferenceHeight,
    double FontSize,
    double Opacity,
    string BackgroundColor,
    bool BorderEnabled,
    string BorderColor,
    string AccentColor,
    bool Compact,
    string NameFontFamily,
    string ValueFontFamily,
    double NameFontSize,
    Avalonia.Thickness OuterMargin,
    Avalonia.Thickness Padding,
    double ContentSpacing,
    Avalonia.Thickness RowMargin,
    double FrametimeGraphHeight,
    IReadOnlyList<InGameOverlayBlockPresentation> Blocks);

internal sealed record InGameOverlayBlockPresentation(
    string Id,
    double X,
    double Y,
    string Title,
    IReadOnlyList<InGameOverlayElement> Elements);

internal abstract record InGameOverlayElement;
internal sealed record InGameOverlayText(char Kind, double FontSize, string Color, string Text, double? Width = null) : InGameOverlayElement;
internal sealed record InGameOverlayGraph(double Height, string Color, double? Minimum, double? Maximum,
    IReadOnlyList<double> Samples) : InGameOverlayElement;

/// <summary>Captures the applied blocks once, then encodes that immutable presentation for native consumers.</summary>
internal static class InGameOverlayFrameSerializer
{
    public static byte[] Build(MainWindowViewModel viewModel, bool forceHidden = false)
        => Serialize(Capture(viewModel, forceHidden));

    internal static InGameOverlayPresentation Capture(MainWindowViewModel viewModel, bool forceHidden = false)
    {
        var visible = !forceHidden && viewModel.OverlayRenderMode == "inGame" && viewModel.OverlayVisible;
        InGameOverlayBlockPresentation[] blocks = visible
            ? viewModel.OverlayAppliedBlocks
                .Where(block => block.IsRenderable)
                .Select(block => new InGameOverlayBlockPresentation(
                    block.Key,
                    block.X,
                    block.Y,
                    block.Title,
                    CaptureElements(block, viewModel)))
                .ToArray()
            : [];

        return new InGameOverlayPresentation(
            visible,
            viewModel.IsPaused,
            viewModel.OverlayWidth,
            viewModel.OverlayCanvasWidth,
            viewModel.OverlayCanvasHeight,
            viewModel.OverlayFontSize,
            viewModel.OverlayOpacity,
            ColorHex(viewModel.OverlayBackgroundBrush, "FF141A24"),
            viewModel.OverlayBorderThickness.Left > 0,
            ColorHex(viewModel.OverlayPanelBorderBrush, "FF2C527A"),
            ColorHex(viewModel.OverlayAccentBrush, "FF1E90FF"),
            viewModel.OverlayCompactMode,
            viewModel.OverlayMetricNameFontFamily.Name,
            viewModel.OverlayMetricValueFontFamily.Name,
            viewModel.OverlayMetricNameFontSize,
            viewModel.OverlayOuterMargin,
            viewModel.OverlayPadding,
            viewModel.OverlayContentSpacing,
            viewModel.OverlayRowMargin,
            viewModel.OverlayFrametimeGraphHeight,
            blocks);
    }

    private static IReadOnlyList<InGameOverlayElement> CaptureElements(
        OverlayCanvasBlockViewModel block, MainWindowViewModel viewModel)
    {
        var elements = new List<InGameOverlayElement>(block.Content.Count * 2);
        foreach (var item in block.Content)
        {
            switch (item)
            {
                case OverlayBlockMetricViewModel metric:
                    if (metric.ShowName)
                        elements.Add(new InGameOverlayText('N', viewModel.OverlayMetricNameFontSize,
                            ColorHex(metric.NameForeground, "FF8E9AA8"), metric.Name));
                    elements.Add(new InGameOverlayText('T', viewModel.OverlayMetricValueFontSize,
                        ColorHex(metric.ValueForeground, "FFF2F5FA"), metric.Value));
                    if (metric.ShowFrametimeGraph)
                        elements.Add(new InGameOverlayGraph(viewModel.OverlayFrametimeGraphHeight,
                            ColorHex(viewModel.OverlayAccentBrush, "FF1E90FF"), null, null,
                            viewModel.FrametimeSamples.TakeLast(1200).ToArray()));
                    break;
                case OverlayGraphRowViewModel graph when graph.IsVisible:
                    elements.Add(new InGameOverlayGraph(graph.Height,
                        ColorHex(graph.Stroke, ColorHex(viewModel.OverlayAccentBrush, "FF1E90FF")), graph.MinY, graph.MaxY,
                        graph.Samples.TakeLast(1200).ToArray()));
                    break;
                case OverlayFrametimeGraphViewModel:
                    elements.Add(new InGameOverlayGraph(viewModel.OverlayFrametimeGraphHeight,
                        ColorHex(viewModel.OverlayAccentBrush, "FF1E90FF"), null, null,
                        viewModel.FrametimeSamples.TakeLast(1200).ToArray()));
                    break;
                case OverlayTemplateRowViewModel template when template.IsVisible:
                    elements.Add(new InGameOverlayText('T', viewModel.OverlayMetricValueFontSize,
                        ColorHex(template.TextBrush, "FFF2F5FA"), template.Text));
                    break;
                case OverlayCustomLineViewModel line when line.IsVisible:
                    elements.Add(new InGameOverlayText('W', line.FontSize,
                        ColorHex(MainWindowViewModel.ResolveBrush("TextPrimaryBrush"), "FFF2F5FA"), line.Text, line.Width));
                    break;
            }
        }

        return elements;
    }

    internal static byte[] Serialize(InGameOverlayPresentation presentation)
    {
        var builder = new StringBuilder(4096);
        var version = presentation.Blocks.Any(block => block.Elements.OfType<InGameOverlayText>().Any(element => element.Kind == 'W')) ? 2 : 1;
        builder.Append("F\t").Append(version).Append('\t')
            .Append(presentation.Visible ? '1' : '0').Append('\t')
            .Append(presentation.Paused ? '1' : '0').Append('\t')
            .Append(Number(presentation.Width)).Append('\t')
            .Append(Number(presentation.ReferenceWidth)).Append('\t')
            .Append(Number(presentation.ReferenceHeight)).Append('\t')
            .Append(Number(presentation.FontSize)).Append('\t')
            .Append(Number(presentation.Opacity, "0.####")).Append('\t')
            .Append(presentation.BackgroundColor).Append('\t')
            .Append(presentation.BorderEnabled ? "1" : "0").Append('\t')
            .Append(presentation.BorderColor).Append('\t')
            .Append(presentation.AccentColor).Append('\t')
            .Append(presentation.Compact ? "1" : "0").Append('\t')
            .Append(Text64(presentation.NameFontFamily)).Append('\t')
            .Append(Text64(presentation.ValueFontFamily)).Append('\t')
            .Append(Number(presentation.NameFontSize)).Append('\t')
            .Append(Thickness(presentation.OuterMargin)).Append('\t')
            .Append(Thickness(presentation.Padding)).Append('\t')
            .Append(Number(presentation.ContentSpacing)).Append('\t')
            .Append(Thickness(presentation.RowMargin)).Append('\t')
            .Append(Number(presentation.FrametimeGraphHeight)).Append('\n');

        foreach (var block in presentation.Blocks)
        {
            builder.Append("B\t").Append(Text64(block.Id)).Append('\t')
                .Append(Number(block.X, "0.######")).Append('\t')
                .Append(Number(block.Y, "0.######")).Append('\t')
                .Append(Text64(block.Title)).Append('\n');
            foreach (var element in block.Elements)
            {
                switch (element)
                {
                    case InGameOverlayText text:
                        builder.Append(text.Kind).Append('\t').Append(Number(text.FontSize)).Append('\t')
                            .Append(text.Color).Append('\t').Append(Text64(text.Text));
                        if (text.Kind == 'W') builder.Append('\t').Append(Number(text.Width ?? 1));
                        builder.Append('\n');
                        break;
                    case InGameOverlayGraph graph:
                        builder.Append("G\t").Append(Number(graph.Height)).Append('\t').Append(graph.Color).Append('\t')
                            .Append(graph.Minimum is { } minimum ? Number(minimum, "0.###") : "").Append('\t')
                            .Append(graph.Maximum is { } maximum ? Number(maximum, "0.###") : "").Append('\t')
                            .Append(string.Join(',', graph.Samples.Where(double.IsFinite).Select(value => Number(value, "0.###"))))
                            .Append('\n');
                        break;
                }
            }
            builder.Append("E\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string Number(double value, string format = "0.##")
        => double.IsFinite(value) ? value.ToString(format, CultureInfo.InvariantCulture) : "0";

    private static string Text64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string Thickness(Avalonia.Thickness value)
        => string.Join("\t", new[] { value.Left, value.Top, value.Right, value.Bottom }.Select(item => Number(item)));

    private static string ColorHex(IBrush? brush, string fallback)
    {
        if (brush is SolidColorBrush solid)
        {
            var color = solid.Color;
            return $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        return fallback;
    }
}
