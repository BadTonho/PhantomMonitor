using System.Globalization;
using System.Text;

namespace PhantomMonitor.Services;

internal sealed record PresentMonFrame(
    int ProcessId,
    string Application,
    double FrametimeMs,
    double GpuBusy,
    double CpuBusy,
    double CpuWait,
    double DisplayLatencyMs);

/// <summary>Parses the changing CSV schema emitted by PresentMon.</summary>
internal sealed class PresentMonCsvParser
{
    private int _colPid = -1;
    private int _colApp = -1;
    private int _colFrametime = -1;
    private int _colGpuBusy = -1;
    private int _colCpuBusy = -1;
    private int _colCpuWait = -1;
    private int _colDisplayLatency = -1;
    private bool _headerParsed;

    internal void Reset()
    {
        _colPid = _colApp = _colFrametime = -1;
        _colGpuBusy = _colCpuBusy = _colCpuWait = _colDisplayLatency = -1;
        _headerParsed = false;
    }

    /// <returns>True when the line is a header or a valid frame row.</returns>
    internal bool TryParseLine(string line, out PresentMonFrame? frame)
    {
        frame = null;
        if (!_headerParsed || LooksLikeHeader(line))
        {
            ParseHeader(SplitCsv(line));
            return true;
        }

        if (_colPid < 0 || _colFrametime < 0) return false;
        var parts = SplitCsv(line);
        if (parts.Count <= Math.Max(_colPid, _colFrametime)
            || !int.TryParse(parts[_colPid].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
            || pid <= 0
            || !double.TryParse(parts[_colFrametime].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var frametime)
            || !double.IsFinite(frametime) || frametime <= 0 || frametime > 2000)
            return false;

        var application = _colApp >= 0 && parts.Count > _colApp ? parts[_colApp].Trim() : "";
        frame = new PresentMonFrame(pid, application, frametime,
            ParseOptional(parts, _colGpuBusy, 0, 100),
            ParseOptional(parts, _colCpuBusy, 0, 100),
            ParseOptional(parts, _colCpuWait, 0, 100),
            ParseOptional(parts, _colDisplayLatency, 0, 2000));
        return true;
    }

    private void ParseHeader(IReadOnlyList<string> headers)
    {
        _colPid = _colApp = _colFrametime = -1;
        _colGpuBusy = _colCpuBusy = _colCpuWait = _colDisplayLatency = -1;
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i].Trim();
            if (header.Equals("ProcessID", StringComparison.OrdinalIgnoreCase)) _colPid = i;
            else if (header.Equals("Application", StringComparison.OrdinalIgnoreCase)) _colApp = i;
            else if (header.Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase)
                     || header.Equals("FrameTime", StringComparison.OrdinalIgnoreCase)
                     || header.Equals("msBetweenDisplayChange", StringComparison.OrdinalIgnoreCase))
            {
                if (_colFrametime < 0 || header.Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase))
                    _colFrametime = i;
            }
            else if (_colGpuBusy < 0 && header.Contains("gpubusy", StringComparison.OrdinalIgnoreCase)) _colGpuBusy = i;
            else if (_colCpuBusy < 0 && header.Contains("cpubusy", StringComparison.OrdinalIgnoreCase)) _colCpuBusy = i;
            else if (_colCpuWait < 0 && header.Contains("cpuwait", StringComparison.OrdinalIgnoreCase)) _colCpuWait = i;
            else if (_colDisplayLatency < 0 &&
                     (header.Contains("msuntildisplayed", StringComparison.OrdinalIgnoreCase)
                      || header.Contains("presentlatency", StringComparison.OrdinalIgnoreCase)))
                _colDisplayLatency = i;
        }
        _headerParsed = _colPid >= 0 && _colFrametime >= 0;
    }

    private static bool LooksLikeHeader(string line) =>
        line.Contains("ProcessID", StringComparison.OrdinalIgnoreCase)
        && (line.Contains("msBetweenPresents", StringComparison.OrdinalIgnoreCase)
            || line.Contains("FrameTime", StringComparison.OrdinalIgnoreCase)
            || line.Contains("msBetweenDisplayChange", StringComparison.OrdinalIgnoreCase));

    private static double ParseOptional(IReadOnlyList<string> parts, int column, double min, double max)
    {
        if (column < 0 || column >= parts.Count) return 0;
        return double.TryParse(parts[column].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
               && double.IsFinite(value) && value >= min && value <= max ? value : 0;
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else inQuotes = !inQuotes;
            }
            else if (character == ',' && !inQuotes)
            {
                result.Add(field.ToString());
                field.Clear();
            }
            else field.Append(character);
        }
        result.Add(field.ToString());
        return result;
    }
}
