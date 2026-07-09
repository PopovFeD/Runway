using Avalonia.Controls;
using Runway.ViewModels;

namespace Runway.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Автопрокрутка живого вывода вниз при каждой новой записи — как в
        // терминале VS Code. Единственный кусочек логики в code-behind: скролл —
        // поведение самого контрола (View), ViewModel про него знать не должна.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.LogEntries.CollectionChanged += (_, _) =>
                {
                    if (vm.LogEntries.Count > 0)
                    {
                        LiveOutput.ScrollIntoView(vm.LogEntries.Count - 1);
                    }
                };
            }
        };
    }
}
