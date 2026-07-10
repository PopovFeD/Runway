using System.Collections.ObjectModel;

namespace Runway.ViewModels;

// Одна плитка Дашборда: имя величины, последнее значение и время обновления
// (нижняя строка карточки, как в макете).
public class TileViewModel : ViewModelBase
{
    public TileViewModel(string title) => Title = title;

    public string Title { get; }

    private string _value = "—";
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    private string _updatedAtText = "";
    public string UpdatedAtText
    {
        get => _updatedAtText;
        set => SetProperty(ref _updatedAtText, value);
    }
}

// Раздел Дашборда — один протокол (тип сообщения) со своими плитками.
// Заголовок мелкий, чтобы не нарушать ощущение плиточности (пожелание из
// обсуждения макета). Видимость управляется галочкой во вкладке "Настройки"
// и сохраняется в settings.json (подписка — в App.axaml.cs).
public class ProtocolSectionViewModel : ViewModelBase
{
    public ProtocolSectionViewModel(string protocolKey, string header, params TileViewModel[] tiles)
    {
        ProtocolKey = protocolKey;
        Header = header;
        Tiles = new ObservableCollection<TileViewModel>(tiles);
    }

    // Стабильный ключ для settings.json (не зависит от текста заголовка)
    public string ProtocolKey { get; }

    public string Header { get; }

    public ObservableCollection<TileViewModel> Tiles { get; }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}
