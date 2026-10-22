namespace PhantomMonitor.Services;

/// <summary>
/// Buffer circular de frametimes (ms). Escrito pela thread de saída do PresentMon e
/// lido pelo serviço de sensores/UI em outra thread — todas as operações são protegidas
/// por lock interno para que um snapshot nunca saia inconsistente (index/count mortos
/// ou buffer alterado durante os dois Array.Copy do snapshot cheio).
/// </summary>
public sealed class FrametimeBuffer
{
    private readonly float[] _buffer;
    private readonly object _lock = new();
    private int _index;
    private int _count;

    public int Capacity
    {
        get { lock (_lock) return _buffer.Length; }
    }

    public int Count
    {
        get { lock (_lock) return _count; }
    }

    public FrametimeBuffer(int capacity = 600)
    {
        _buffer = new float[capacity];
    }

    public void Add(float value)
    {
        lock (_lock)
        {
            _buffer[_index] = value;
            _index = (_index + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _index = 0;
            _count = 0;
            Array.Clear(_buffer);
        }
    }

    public float[] ToArray()
    {
        lock (_lock)
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
    }

    /// <summary>
    /// Média da janela sem materializar cópia: o <see cref="SensorTuningEngine"/>
    /// chama por amostra fresca de toda série suavizada e o valor anterior gastava
    /// um float[] + LINQ por chamada.
    /// </summary>
    public double GetAverage()
    {
        lock (_lock)
        {
            if (_count == 0) return 0;
            double sum = 0;
            for (var i = 0; i < _count; i++) sum += _buffer[i];
            return sum / _count;
        }
    }

    public (float min, float max, float avg) GetStats()
    {
        lock (_lock)
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
}
