using CommunityToolkit.Mvvm.ComponentModel;

namespace CyberDownloadManager.Presentation.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Cyber Download Manager";

    [ObservableProperty]
    private string _activeTransferCountText = "0 tasks running • 0 B/s";
}
