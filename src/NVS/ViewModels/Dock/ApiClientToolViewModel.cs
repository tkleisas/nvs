using ApiClient.Core.Storage;
using ApiClient.UI.ViewModels;
using Dock.Model.Mvvm.Controls;
using NVS.Helpers;

namespace NVS.ViewModels.Dock;

public class ApiClientToolViewModel : Document
{
    public MainViewModel Main { get; }
    public WorkspaceViewModel ApiClientViewModel { get; }

    public ApiClientToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "ApiClient";
        Title = "🌐 API Client";
        CanClose = true;
        CanPin = false;

        ApiClientViewModel = new WorkspaceViewModel(new CollectionStore(), new SettingsStore(), ResolveLlm())
        {
            IsMenuVisible = false, // NVS surfaces the workspace commands in its own menus
        };
    }

    /// <summary>
    /// Routes the workspace's AI features through the NVS LLM service so they use the
    /// endpoint/model configured in NVS Settings. Returns null when unavailable, in which
    /// case the workspace falls back to its built-in (standalone) LLM configuration.
    /// </summary>
    private static ApiClient.Core.Llm.ILlmService? ResolveLlm()
    {
        try
        {
            var llm = App.Current?.Services?.GetService(typeof(NVS.Core.Interfaces.ILlmService)) as NVS.Core.Interfaces.ILlmService;
            return llm is null ? null : new ApiClientLlmAdapter(llm);
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "LLM integration for API Client unavailable");
            return null;
        }
    }
}
