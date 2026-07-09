using Runway.Threading;

namespace Runway.Tests.Support;

// Выполняет действие сразу же, синхронно, в вызывающем потоке — так тестам не
// нужен настоящий Avalonia Dispatcher.UIThread (который без инициализированного
// Application либо не работает предсказуемо, либо требует пакет Avalonia.Headless).
public class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
