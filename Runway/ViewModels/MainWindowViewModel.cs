using System.Collections.ObjectModel;
using Avalonia.Threading;
using Runway.Framing;
using Runway.Transport;

namespace Runway.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly FrameReader _frameReader;
    private readonly ISerialTransport _transport;
    private readonly IPortLister _portLister;

    public MainWindowViewModel(
        FrameReader frameReader,
        ISerialTransport transport,
        IPortLister portLister
    )
    {
        _frameReader = frameReader;
        _transport = transport;
        _portLister = portLister;

        _transport.DataReceived += OnDataReceived;
    }

    public string Greeting => "Welcome to Avalonia!";
    public IReadOnlyList<string> AvailablePorts => _portLister.GetAvailablePorts();

    // Сюда GUI будет смотреть, чтобы показать лог принятых кадров
    public ObservableCollection<string> LogEntries { get; } = new();

    private void OnDataReceived(byte[] bytes)
    {
        var frames = _frameReader.Append(bytes);

        foreach (var frame in frames)
        {
            string line =
                $"Seq={frame.Sequence}  Type={frame.MessageType}  Payload={BitConverter.ToString(frame.Payload)}";

            // DataReceived приходит из фонового потока —
            // а трогать LogEntries (она привязана к GUI) можно только из UI-потока.
            // Dispatcher.UIThread.Post перекладывает это действие туда.
            Dispatcher.UIThread.Post(() => LogEntries.Add(line));
        }
    }
}
