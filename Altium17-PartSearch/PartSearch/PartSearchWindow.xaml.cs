using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Controls;

namespace Altium17PartSearch.PartSearch
{
    internal partial class PartSearchWindow : Window
    {
        private readonly AltiumSupplierSearch _search = new AltiumSupplierSearch();
        private readonly AltiumVaultModels _cloud = new AltiumVaultModels();
        private readonly ObservableCollection<SupplierPart> _parts = new ObservableCollection<SupplierPart>();
        private CancellationTokenSource _request;
        private bool _closed, _busy, _ready, _hasNext, _supportsPaging;
        private int _offset;
        private string _query;
        private bool _mpnOnly, _inStock;
        private AltiumSupplierSearch.Provider _provider;
        internal SupplierPart SelectedPart => resultsGrid.SelectedItem as SupplierPart;
        internal SupplierOffer SelectedOffer => offerGrid.SelectedItem as SupplierOffer;
        internal LibraryModelChoice SelectedModel { get; private set; }
        internal bool PlaceInSchematic => placeBox.IsChecked == true;

        internal PartSearchWindow(bool canPlace)
        {
            InitializeComponent();
            placeBox.IsEnabled = canPlace;
            placeBox.IsChecked = canPlace;
            resultsGrid.ItemsSource = _parts;
            CollectionViewSource.GetDefaultView(_parts).Filter = FilterPart;
            _ready = true;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var providers = _search.GetProviders();
                providerCombo.ItemsSource = providers;
                providerCombo.SelectedIndex = providers.Count > 0 ? 0 : -1;
                if (providers.Count == 0) statusText.Text = "No enabled suppliers were found. Configure suppliers in Altium Preferences, then reopen this window.";
            }
            catch (Exception error) { statusText.Text = error.Message; }
            UpdateControls();
            queryBox.Focus();
        }

        private bool FilterPart(object item)
        {
            var part = item as SupplierPart;
            if (part == null) return false;
            bool Contains(string text, string filter) => string.IsNullOrWhiteSpace(filter) ||
                (text ?? "").IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
            if (!Contains(part.Manufacturer, manufacturerFilter.Text)) return false;
            if (string.IsNullOrWhiteSpace(parameterFilter.Text) && string.IsNullOrWhiteSpace(valueFilter.Text)) return true;
            return part.GetImportParameters(null).Any(p => Contains(p.Key, parameterFilter.Text) && Contains(p.Value, valueFilter.Text));
        }

        private void Filter_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_ready || _closed) return;
            CollectionViewSource.GetDefaultView(_parts).Refresh();
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            if (_closed || _busy || providerCombo.SelectedItem == null || string.IsNullOrWhiteSpace(queryBox.Text)) return;
            _provider = (AltiumSupplierSearch.Provider)providerCombo.SelectedItem;
            _query = queryBox.Text.Trim(); _mpnOnly = mpnOnlyBox.IsChecked == true; _inStock = inStockBox.IsChecked == true;
            _parts.Clear(); _offset = 0; _hasNext = false; pageText.Text = "";
            await LoadPageAsync(0);
        }

        private async Task LoadPageAsync(int offset)
        {
            _request?.Dispose();
            _request = new CancellationTokenSource();
            var token = _request.Token;
            _busy = true; UpdateControls();
            statusText.Text = $"Searching {_provider.Name}...";
            try
            {
                var page = await _search.SearchAsync(_provider, _query, offset, _mpnOnly, _inStock, token);
                if (_closed || token.IsCancellationRequested) return;
                _offset = offset;
                _parts.Clear();
                foreach (var part in page.Parts) _parts.Add(part);
                _supportsPaging = page.SupportsPaging;
                _hasNext = _supportsPaging && (page.Total.HasValue ? offset + AltiumSupplierSearch.PageSize < page.Total.Value : page.RawCount >= AltiumSupplierSearch.PageSize);
                pageText.Text = _supportsPaging ? $"Page {offset / AltiumSupplierSearch.PageSize + 1}" : "MPN results";
                statusText.Text = $"{page.Parts.Count} manufacturer parts from {_provider.Name}. Filters apply to this page. Stock and prices are supplier snapshots.";
                if (_parts.Count > 0) resultsGrid.SelectedIndex = 0;
            }
            catch (OperationCanceledException) { if (!_closed) statusText.Text = "Search canceled."; }
            catch (Exception error)
            {
                RuntimeDiagnostics.Error("Supplier search", error);
                if (!_closed) statusText.Text = $"Search failed: {error.Message}";
            }
            finally { _busy = false; if (!_closed) UpdateControls(); }
        }

        private async void Previous_Click(object sender, RoutedEventArgs e) { if (!_busy && _offset > 0) await LoadPageAsync(_offset - AltiumSupplierSearch.PageSize); }
        private async void Next_Click(object sender, RoutedEventArgs e) { if (!_busy && _hasNext) await LoadPageAsync(_offset + AltiumSupplierSearch.PageSize); }
        private void CancelSearch_Click(object sender, RoutedEventArgs e) => _request?.Cancel();
        private void Query_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; Search_Click(sender, e); } }

        private void Provider_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            _parts.Clear(); _offset = 0; _hasNext = false; pageText.Text = "";
            UpdateControls();
        }

        private void Part_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            var part = SelectedPart;
            partTitle.Text = part == null ? "" : $"{part.Manufacturer} {part.Mpn}";
            descriptionText.Text = part?.Description ?? "";
            parameterGrid.ItemsSource = part?.GetImportParameters(null).OrderBy(p => p.Key).ToList();
            offerGrid.ItemsSource = part?.Offers.OrderByDescending(o => o.Stock.GetValueOrDefault() > 0).ToList();
            if (part?.Offers.Count > 0) offerGrid.SelectedIndex = 0;
            SelectedModel = null;
            modelGrid.ItemsSource = null;
            UpdateControls();
        }

        private void Offer_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            var offer = SelectedOffer;
            priceText.Text = offer == null ? "" : (offer.Prices.Count == 0 ? "No price data supplied." : offer.PriceSummary) +
                (string.IsNullOrWhiteSpace(offer.Updated) ? "" : "\nSupplier updated: " + offer.Updated);
            UpdateControls();
        }

        private void UpdateControls()
        {
            if (!_ready || _closed) return;
            searchButton.IsEnabled = !_busy && providerCombo.SelectedItem != null;
            providerCombo.IsEnabled = queryBox.IsEnabled = mpnOnlyBox.IsEnabled = inStockBox.IsEnabled = !_busy;
            previousButton.IsEnabled = !_busy && _supportsPaging && _offset > 0;
            nextButton.IsEnabled = !_busy && _hasNext;
            cancelSearchButton.IsEnabled = _busy;
            importButton.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(SelectedPart?.Mpn);
            cloudButton.IsEnabled = importButton.IsEnabled;
            resultsGrid.IsEnabled = offerGrid.IsEnabled = modelGrid.IsEnabled = !_busy;
            manufacturerFilter.IsEnabled = parameterFilter.IsEnabled = valueFilter.IsEnabled = !_busy;
            copyButton.IsEnabled = !_busy && SelectedPart != null;
            datasheetButton.IsEnabled = !_busy && WebLinks.IsHttp(SelectedPart?.Datasheet);
            supplierButton.IsEnabled = !_busy && WebLinks.IsHttp(SelectedOffer?.Url);
        }

        private void OpenLink(string url)
        {
            if (!WebLinks.IsHttp(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception error) { statusText.Text = "Could not open the link: " + error.Message; }
        }
        private void Datasheet_Click(object sender, RoutedEventArgs e) => OpenLink(SelectedPart?.Datasheet);
        private void Supplier_Click(object sender, RoutedEventArgs e) => OpenLink(SelectedOffer?.Url);
        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPart == null) return;
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, SelectedPart.GetImportParameters(SelectedOffer)
                    .Select(p => p.Key.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ') + "\t" +
                        p.Value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '))));
                statusText.Text = "Parameters copied.";
            }
            catch (Exception error) { statusText.Text = "Could not copy parameters: " + error.Message; }
        }

        private async void Cloud_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || SelectedPart == null) return;
            var part = SelectedPart;
            _request?.Dispose();
            _request = new CancellationTokenSource();
            var token = _request.Token;
            _busy = true;
            UpdateControls();
            void Progress(string message) { if (!_closed) statusText.Text = message; }
            try
            {
                var model = modelGrid.SelectedItem as AltiumVaultModels.Model;
                if (model == null)
                {
                    var models = await _cloud.FindAsync(part, token, Progress);
                    if (_closed || token.IsCancellationRequested) return;
                    modelGrid.ItemsSource = models;
                    detailsTabs.SelectedItem = modelsTab;
                    if (models.Count != 1)
                    {
                        statusText.Text = models.Count == 0
                            ? "No exact CAD match was found in your Vaults. You can choose an installed library component instead."
                            : "Select a model revision, then click Download CAD models.";
                        return;
                    }
                    modelGrid.SelectedIndex = 0;
                    model = models[0];
                }
                SelectedModel = await _cloud.DownloadAsync(model, token, Progress);
                if (_closed || token.IsCancellationRequested || SelectedPart != part) { SelectedModel = null; return; }
                DialogResult = true;
            }
            catch (OperationCanceledException) { Progress("CAD download canceled."); }
            catch (Exception error)
            {
                SelectedModel = null;
                RuntimeDiagnostics.Error("CAD download", error);
                Progress(error.Message);
            }
            finally { _busy = false; if (!_closed) UpdateControls(); }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || SelectedPart == null) return;
            try
            {
                SelectedModel = LibraryModelChoice.Browse(SelectedPart.Mpn);
                if (SelectedModel == null) return;
                DialogResult = true;
            }
            catch (Exception error) { statusText.Text = "Could not choose a library component: " + error.Message; }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel) return;
            _closed = true; _request?.Cancel();
        }
        protected override void OnClosed(EventArgs e) { _request?.Dispose(); base.OnClosed(e); }
    }
}
