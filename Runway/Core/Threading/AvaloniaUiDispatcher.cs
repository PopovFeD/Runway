using Avalonia.Threading;

namespace Runway.Threading;

public class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
