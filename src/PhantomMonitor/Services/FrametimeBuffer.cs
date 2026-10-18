namespace RivaMonitor.Services;

public sealed class FrametimeBuffer
{
    private readonly float[] _buffer;
    private int _index;
    private int _count;

    public int Capacity => _buffer.Length;
    public int Count => _count;
    public ReadOnlySpan<float> Data => new(_buffer, 0, _count);

    public FrametimeBuffer(int capacity = 300)
    {
        _buffer = new float[capacity];
    }

    public void Add(float value)
    {
        _buffer[_index] = value;
        _index = (_index + 1) % _buffer.Length;
        if (_count < _buffer.Length) _count++;
    }

    public void Clear()
    {
        _index = 0;
        _count = 0;
        Array.Clear(_buffer);
    }

    public float[] ToArray()
    {
        var result = new float[_count];
        if (_count < _buffer.Length)
        {
            Array.Copy(_buffer, result, _count);
        }
        else
        {
            Array.Copy(_buffer, _index, result, 0, _buffer.Length - _index);
            Array.Copy(_buffer, 0, result, _buffer.Length - _index, _index);
        }
        return result;
    }

    public (float min, float max, float avg) GetStats()
    {
        if (_count == 0) return (0, 0, 0);

        float sum = 0, min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < _count; i++)
        {
            var v = _buffer[i];
            sum += v;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        return (min, max, sum / _count);
    }
}
