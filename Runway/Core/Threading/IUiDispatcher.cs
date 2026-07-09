namespace Runway.Threading;

// Абстракция вокруг Dispatcher.UIThread.Post. Нужна, чтобы MainWindowViewModel
// можно было юнит-тестировать без запущенного Avalonia-приложения — "голый"
// Dispatcher.UIThread без инициализированного App либо не работает предсказуемо,
// либо тянет за собой пакет Avalonia.Headless. В тестах используется реализация,
// которая просто выполняет действие синхронно в вызывающем потоке.
public interface IUiDispatcher
{
    void Post(Action action);
}
