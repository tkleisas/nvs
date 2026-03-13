using Avalonia.Controls;

namespace NVS.Views.Dock;

public partial class NuGetView : UserControl
{
    public NuGetView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is NVS.ViewModels.Dock.NuGetToolViewModel vm)
        {
            vm.RefreshProjects();
        }
    }
}
