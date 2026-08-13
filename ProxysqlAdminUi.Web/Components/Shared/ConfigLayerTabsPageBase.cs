using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using ProxysqlAdminUi.Web.Models;

namespace ProxysqlAdminUi.Web.Components.Shared;

public abstract class ConfigLayerTabsPageBase : ComponentBase, IDisposable
{
    protected static readonly ProxySqlConfigLayer[] ConfigLayers = Enum.GetValues<ProxySqlConfigLayer>();

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    protected int _selectedLayerIndex;

    protected ProxySqlConfigLayer CurrentLayer => ConfigLayers[_selectedLayerIndex];

    protected override async Task OnInitializedAsync()
    {
        NavigationManager.LocationChanged += OnLocationChanged;

        var layer = GetLayerFromUri(NavigationManager.Uri, out var hasValidFragment);
        _selectedLayerIndex = Array.IndexOf(ConfigLayers, layer);

        if (!hasValidFragment)
        {
            NavigateToLayer(layer, replace: true);
        }

        await LoadLayerAsync(layer);
    }

    protected async Task OnLayerChangedAsync(int index)
    {
        if (index < 0 || index >= ConfigLayers.Length)
        {
            return;
        }

        _selectedLayerIndex = index;
        NavigateToLayer(CurrentLayer, replace: false);
        await LoadLayerAsync(CurrentLayer);
    }

    protected abstract Task LoadLayerAsync(ProxySqlConfigLayer layer, bool force = false);

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        _ = InvokeAsync(async () =>
        {
            var layer = GetLayerFromUri(args.Location, out var hasValidFragment);
            var index = Array.IndexOf(ConfigLayers, layer);

            if (!hasValidFragment)
            {
                NavigateToLayer(layer, replace: true);
            }

            if (index == _selectedLayerIndex)
            {
                return;
            }

            _selectedLayerIndex = index;
            await LoadLayerAsync(layer);
            StateHasChanged();
        });
    }

    private void NavigateToLayer(ProxySqlConfigLayer layer, bool replace)
    {
        var uri = new Uri(NavigationManager.Uri);
        var builder = new UriBuilder(uri)
        {
            Fragment = layer.ToString().ToLowerInvariant()
        };

        if (!string.Equals(uri.AbsoluteUri, builder.Uri.AbsoluteUri, StringComparison.Ordinal))
        {
            NavigationManager.NavigateTo(builder.Uri.AbsoluteUri, replace: replace);
        }
    }

    private static ProxySqlConfigLayer GetLayerFromUri(string location, out bool hasValidFragment)
    {
        var fragment = new Uri(location).Fragment.TrimStart('#');
        hasValidFragment = Enum.TryParse(fragment, ignoreCase: true, out ProxySqlConfigLayer layer)
            && Enum.IsDefined(layer);

        return hasValidFragment ? layer : ProxySqlConfigLayer.Main;
    }

    public void Dispose()
    {
        NavigationManager.LocationChanged -= OnLocationChanged;
        GC.SuppressFinalize(this);
    }
}
