using Avalonia.Controls;
using Avalonia.Input;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class CodeMetricsView : UserControl
{
    public CodeMetricsView()
    {
        InitializeComponent();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.ClickCount == 2
            && DataContext is CodeMetricsToolViewModel vm)
        {
            // Find the clicked MetricTreeNode
            if (e.Source is Control { DataContext: MetricTreeNode node } && node.FilePath is not null)
            {
                _ = vm.NavigateToFile(node.FilePath, node.Line);
                e.Handled = true;
            }
        }
    }
}
