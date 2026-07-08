using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Runway.ViewModels;

namespace Runway;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        var viewTypeName = data.GetType().FullName!.Replace("ViewModel", "View");
        var viewType = Type.GetType(viewTypeName);

        if (viewType is not null)
        {
            return (Control)Activator.CreateInstance(viewType)!;
        }

        return new TextBlock { Text = $"Unable to create view for {data.GetType().Name}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
