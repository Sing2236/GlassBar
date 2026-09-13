using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GlassBar.Controls;

public sealed class GifSticker : Image
{
    private readonly List<BitmapFrame> _frames = [];
    private readonly List<TimeSpan> _delays = [];
    private readonly DispatcherTimer _timer = new();
    private int _frameIndex;

    public GifSticker(string path)
    {
        Stretch = System.Windows.Media.Stretch.Uniform;
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => Advance();
        Unloaded += (_, _) => _timer.Stop();
        Load(path);
    }

    private void Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            foreach (var frame in decoder.Frames)
            {
                frame.Freeze();
                _frames.Add(frame);
                _delays.Add(ReadDelay(frame));
            }

            if (_frames.Count == 0) return;
            Source = _frames[0];
            if (_frames.Count > 1)
            {
                _timer.Interval = _delays[0];
                _timer.Start();
            }
        }
        catch { }
    }

    private void Advance()
    {
        if (_frames.Count < 2) return;
        _frameIndex = (_frameIndex + 1) % _frames.Count;
        Source = _frames[_frameIndex];
        _timer.Interval = _delays[_frameIndex];
    }

    private static TimeSpan ReadDelay(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/grctlext/Delay") is ushort delay)
                return TimeSpan.FromMilliseconds(Math.Max(20, delay * 10));
        }
        catch { }
        return TimeSpan.FromMilliseconds(100);
    }
}
