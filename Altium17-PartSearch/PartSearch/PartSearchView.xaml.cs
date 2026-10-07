using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DXP;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Altium17PartSearch.PartSearch
{
    internal partial class PartSearchView : UserControl, IDisposable
    {
        private readonly AltiumSupplierSearch _search = new AltiumSupplierSearch();
        private readonly AltiumVaultModels _cloud = new AltiumVaultModels();
        private readonly ObservableCollection<SupplierPart> _parts = new ObservableCollection<SupplierPart>();
        private CancellationTokenSource _request, _previewDelay;
        private CadPreview _preview;
        private bool _closed, _busy, _ready, _loaded, _hasNext, _supportsPaging, _canPlace;
        private readonly DispatcherTimer _documentTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private SupplierLibraryImporter.ImportedComponent _imported;
        private LibraryModelChoice _importedModel;
        private SupplierOffer _importedOffer;
        private int _offset, _quantity = 1;
        private string _query;
        private bool _mpnOnly, _inStock;
        private AltiumSupplierSearch.Provider _provider;
        internal SupplierPart SelectedPart => resultsGrid.SelectedItem as SupplierPart;
        internal SupplierOffer SelectedOffer => (offerGrid.SelectedItem as SupplierOfferRow)?.Offer;
        internal LibraryModelChoice SelectedModel { get; private set; }
        private PartCategory Category => categoryCombo.SelectedItem as PartCategory ?? PartCategory.All[0];

        internal PartSearchView()
        {
            InitializeComponent();
            categoryCombo.ItemsSource = PartCategory.All;
            categoryCombo.SelectedIndex = 0;
            _documentTimer.Tick += (_, __) => { if (!_closed && !_busy) RefreshDocument(); };
            _documentTimer.Start();
            resultsGrid.ItemsSource = _parts;
            CollectionViewSource.GetDefaultView(_parts).Filter = FilterPart;
            _ready = true;
        }

        private void View_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loaded) return;
            _loaded = true;
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
            if (!Category.Matches(part.Category, part.Description, part.Parameters.Keys)) return false;
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
            if (_closed || _busy || providerCombo.SelectedItem == null) return;
            if (string.IsNullOrWhiteSpace(Category.SearchQuery(queryBox.Text))) { statusText.Text = "Enter an MPN or choose a category."; return; }
            _provider = (AltiumSupplierSearch.Provider)providerCombo.SelectedItem;
            _query = Category.SearchQuery(queryBox.Text); _mpnOnly = !string.IsNullOrWhiteSpace(queryBox.Text) && mpnOnlyBox.IsChecked == true; _inStock = inStockBox.IsChecked == true;
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
                statusText.Text = $"{CollectionViewSource.GetDefaultView(_parts).Cast<object>().Count()} matching parts on this page. Stock and prices are supplier snapshots.";
                if (resultsGrid.Items.Count > 0) resultsGrid.SelectedIndex = 0;
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

        private void Category_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || _busy) return;
            CollectionViewSource.GetDefaultView(_parts).Refresh();
            Search_Click(sender, e);
        }

        private void RefreshDocument()
        {
            bool canPlace = PartPlacement.CanPlace;
            if (canPlace == _canPlace) return;
            _canPlace = canPlace;
            UpdateControls();
        }

        private void Part_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            var part = SelectedPart;
            partTitle.Text = part == null ? "" : $"{part.Manufacturer} {part.Mpn}";
            descriptionText.Text = part?.Description ?? "";
            parameterGrid.ItemsSource = part?.GetImportParameters(null).OrderBy(p => p.Key).ToList();
            ShowOffers();
            SelectedModel = null; _imported = null; _importedModel = null; _importedOffer = null;
            modelGrid.ItemsSource = null;
            _preview = null; symbolImage.Source = footprintImage.Source = null;
            symbolPartCombo.ItemsSource = footprintCombo.ItemsSource = null;
            previewText.Text = part == null ? "Select a part to preview its CAD models." : "Loading CAD preview…";
            _previewDelay?.Cancel(); _previewDelay?.Dispose();
            _previewDelay = new CancellationTokenSource();
            if (part != null) QueuePreview(part, _previewDelay.Token);
            modelText.Text = "Place part downloads matching symbols and footprints automatically. If several revisions match, choose one below.";
            UpdateControls();
        }

        private void Offer_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            var offer = SelectedOffer;
            priceText.Text = offer == null ? "" : $"{offer.Supplier} · {offer.Sku}\n" +
                (offer.Prices.Count == 0 ? "No price data supplied." : offer.PriceSummary) +
                (string.IsNullOrWhiteSpace(offer.Updated) ? "" : "\n" + offer.Updated);
            UpdateControls();
        }

        private void ShowOffers()
        {
            var preferred = SelectedOffer;
            var rows = SelectedPart?.Offers.Select(o => new SupplierOfferRow(o, _quantity))
                .OrderByDescending(o => o.CanSupply).ThenByDescending(o => o.Stock)
                .ThenBy(o => o.Supplier, StringComparer.OrdinalIgnoreCase).ToList();
            offerGrid.ItemsSource = rows;
            offerGrid.SelectedItem = rows?.FirstOrDefault(r => r.Offer == preferred || preferred != null && SupplierAvailability.SameListing(r.Offer, preferred))
                ?? rows?.FirstOrDefault();
            stockSummary.Text = SelectedPart == null ? "" : SupplierAvailability.Summary(SelectedPart.Offers, _quantity);
        }

        private void Quantity_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            bool valid = int.TryParse(quantityBox.Text, out int quantity) && quantity > 0;
            quantityError.Text = valid ? "" : $"Enter a whole quantity of 1 or more. Showing quantity {_quantity:N0}.";
            if (!valid) return;
            _quantity = quantity;
            ShowOffers();
        }

        private async void RefreshStock_Click(object sender, RoutedEventArgs e)
        {
            if (_closed || _busy || SelectedPart == null || _provider == null) return;
            var part = SelectedPart;
            _previewDelay?.Cancel();
            _request?.Dispose(); _request = new CancellationTokenSource();
            var token = _request.Token;
            _busy = true; UpdateControls();
            try
            {
                var result = await _search.RefreshOffersAsync(_provider, part, token, message => { if (!_closed) statusText.Text = message; });
                if (_closed || token.IsCancellationRequested || SelectedPart != part) return;
                if (result.Offers.Count == 0)
                {
                    statusText.Text = "No matching distributor listings returned. Previous stock retained.";
                    return;
                }
                if (!result.Limited) part.Offers.Clear();
                SupplierAvailability.Merge(part.Offers, result.Offers);
                _imported = null; _importedOffer = null;
                part.StockChanged(); ShowOffers();
                statusText.Text = $"{result.Offers.Count} distributor listings refreshed at {DateTime.Now:t}. " +
                    (result.Limited ? "First 500 search results checked; other previous listings retained." : "Stock and prices are supplier snapshots.");
            }
            catch (OperationCanceledException) { if (!_closed) statusText.Text = "Stock refresh canceled. Previous stock retained."; }
            catch (Exception error)
            {
                RuntimeDiagnostics.Error("Stock refresh", error);
                if (!_closed) statusText.Text = "Stock refresh failed: " + error.GetBaseException().Message;
            }
            finally { _busy = false; if (!_closed) UpdateControls(); }
        }

        private void Model_Changed(object sender, SelectionChangedEventArgs e)
        {
            SelectedModel = null; _imported = null; _importedModel = null; _importedOffer = null;
            _preview = null; symbolImage.Source = footprintImage.Source = null;
            symbolPartCombo.ItemsSource = footprintCombo.ItemsSource = null;
            if (_ready && !_busy && SelectedPart != null && modelGrid.SelectedItem != null)
            {
                _previewDelay?.Cancel(); _previewDelay?.Dispose();
                _previewDelay = new CancellationTokenSource();
                QueuePreview(SelectedPart, _previewDelay.Token);
            }
        }

        private void UpdateControls()
        {
            if (!_ready || _closed) return;
            searchButton.IsEnabled = !_busy && providerCombo.SelectedItem != null;
            providerCombo.IsEnabled = queryBox.IsEnabled = mpnOnlyBox.IsEnabled = inStockBox.IsEnabled = categoryCombo.IsEnabled = !_busy;
            previousButton.IsEnabled = !_busy && _supportsPaging && _offset > 0;
            nextButton.IsEnabled = !_busy && _hasNext;
            cancelSearchButton.IsEnabled = _busy;
            importButton.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(SelectedPart?.Mpn);
            cloudButton.IsEnabled = downloadMenu.IsEnabled = previewButton.IsEnabled = importButton.IsEnabled;
            placeButton.IsEnabled = placeMenu.IsEnabled = importButton.IsEnabled && PartPlacement.CanPlace;
            placeButton.ToolTip = PartPlacement.CanPlace ? "Download matching CAD models and place on the active schematic" : "Open a schematic to place this part";
            resultsGrid.IsEnabled = offerGrid.IsEnabled = modelGrid.IsEnabled = !_busy;
            manufacturerFilter.IsEnabled = parameterFilter.IsEnabled = valueFilter.IsEnabled = !_busy;
            copyButton.IsEnabled = !_busy && SelectedPart != null;
            datasheetButton.IsEnabled = !_busy && WebLinks.IsHttp(SelectedPart?.Datasheet);
            supplierButton.IsEnabled = !_busy && WebLinks.IsHttp(SelectedOffer?.Url);
            refreshStockButton.IsEnabled = !_busy && _provider != null && !string.IsNullOrWhiteSpace(SelectedPart?.Mpn);
            quantityBox.IsEnabled = !_busy;
        }

        private void OpenLink(string url)
        {
            if (!WebLinks.IsHttp(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception error) { statusText.Text = "Could not open the link: " + error.Message; }
        }
        private void Datasheet_Click(object sender, RoutedEventArgs e) => OpenLink(SelectedPart?.Datasheet);
        private void Supplier_Click(object sender, RoutedEventArgs e)
        {
            if (e is MouseButtonEventArgs mouse && ItemsControl.ContainerFromElement(offerGrid, mouse.OriginalSource as DependencyObject) is not DataGridRow) return;
            OpenLink(SelectedOffer?.Url);
        }
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

        private async void Preview_Click(object sender, RoutedEventArgs e)
        {
            detailsTabs.SelectedItem = previewTab;
            await PreparePartAsync(false, true);
        }
        private async void QueuePreview(SupplierPart part, CancellationToken token)
        {
            try
            {
                await Task.Delay(400, token);
                if (!_closed && !_busy && SelectedPart == part) await PreparePartAsync(false, true);
            }
            catch (OperationCanceledException) { }
        }
        private void ShowPreview()
        {
            previewText.Text = "";
            _preview = new CadPreview(SelectedModel);
            previewText.Text = _preview.Warning ?? "";
            symbolPartCombo.ItemsSource = Enumerable.Range(1, _preview.PartCount).ToList();
            symbolPartCombo.SelectedItem = SelectedModel.PartId;
            footprintCombo.ItemsSource = _preview.Footprints;
            if (_preview.Footprints.Count > 0)
                footprintCombo.SelectedItem = _preview.Footprints.FirstOrDefault(f => f.IsCurrent) ?? _preview.Footprints[0];
            if (_preview.Footprints.Count == 0 && string.IsNullOrEmpty(previewText.Text))
                previewText.Text = "No footprint is supplied with this model.";
        }
        private void SymbolPart_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_preview == null || symbolPartCombo.SelectedItem is not int partId) return;
            try
            {
                symbolImage.Source = _preview.Symbol(partId);
                SelectedModel.PartId = partId;
                if (_imported != null) _imported.PartId = partId;
            }
            catch (Exception error) { previewText.Text = "Symbol preview: " + error.Message; }
        }
        private void Footprint_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_preview == null || footprintCombo.SelectedItem is not CadPreview.Footprint footprint) return;
            if (SelectedModel.FootprintName != footprint.Name)
            {
                SelectedModel.FootprintName = footprint.Name;
                _imported = null; _importedModel = null;
            }
            try { footprintImage.Source = _preview.Board(footprint); }
            catch (Exception error) { previewText.Text = "Footprint preview: " + error.Message; }
        }

        private async void Cloud_Click(object sender, RoutedEventArgs e) => await PreparePartAsync(false);
        private async void Place_Click(object sender, RoutedEventArgs e)
        {
            if (e is MouseButtonEventArgs mouse && ItemsControl.ContainerFromElement(resultsGrid, mouse.OriginalSource as DependencyObject) is not DataGridRow) return;
            await PreparePartAsync(true);
        }

        private async Task PreparePartAsync(bool place, bool preview = false)
        {
            if (_closed || _busy || SelectedPart == null) return;
            // A header double-click sorts the grid; only a result row places a part.
            var part = SelectedPart;
            var context = place ? PartPlacement.CurrentSchematic() : null;
            if (place && context == null) { statusText.Text = "Open a schematic to place this part."; return; }
            _request?.Dispose();
            _request = new CancellationTokenSource();
            var token = _request.Token;
            _busy = true; UpdateControls();
            void Progress(string message) { if (!_closed) statusText.Text = message; }
            try
            {
                if (SelectedModel == null)
                {
                    var model = modelGrid.SelectedItem as AltiumVaultModels.Model;
                    if (model == null)
                    {
                        var models = await _cloud.FindAsync(part, token, Progress);
                        if (_closed || token.IsCancellationRequested) return;
                        modelGrid.ItemsSource = models;
                        if (models.Count != 1)
                        {
                            detailsTabs.SelectedItem = modelsTab;
                            previewText.Text = models.Count == 0 ? "No CAD model available. Choose a local model to preview this part." : "Choose a model revision in Models.";
                            Progress(models.Count == 0 ? "No matching CAD model. Choose a local model to use this part."
                                : "Select a model revision, then click " + (place ? "Place part." : preview ? "Load CAD preview." : "Download to library."));
                            return;
                        }
                        modelGrid.SelectedIndex = 0;
                        model = models[0];
                    }
                    SelectedModel = await _cloud.DownloadAsync(model, token, Progress);
                }
                if (_closed || token.IsCancellationRequested || SelectedPart != part) return;
                if (_preview == null)
                {
                    try { ShowPreview(); }
                    catch (Exception error)
                    {
                        RuntimeDiagnostics.Error("CAD preview", error);
                        previewText.Text = "CAD preview: " + error.GetBaseException().Message;
                    }
                }
                if (preview)
                {
                    Progress(string.IsNullOrEmpty(previewText.Text) ? "CAD preview ready." : previewText.Text);
                    return;
                }
                if (_imported == null || _importedModel != SelectedModel || _importedOffer != SelectedOffer)
                {
                    Progress("Importing symbol and model links…");
                    _imported = SupplierLibraryImporter.Import(part, SelectedOffer, SelectedModel);
                    _importedModel = SelectedModel; _importedOffer = SelectedOffer;
                }
                modelText.Text = "Ready: " + _imported.Reference + "\n" + _imported.LibraryPath;
                if (!place) { Progress("Downloaded to library. Choose Place part to use it on a schematic."); return; }
                Progress("Click on the schematic to place the part. Press Esc to finish.");
                // Leave the WPF event before entering Altium's interactive command loop.
                await Dispatcher.InvokeAsync(() => PartPlacement.Place(context, _imported), DispatcherPriority.Background);
            }
            catch (OperationCanceledException) { Progress("CAD download canceled."); }
            catch (Exception error)
            {
                RuntimeDiagnostics.Error(place ? "Place part" : "CAD download", error);
                Progress(error.GetBaseException().Message);
            }
            finally { _busy = false; if (!_closed) UpdateControls(); }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || SelectedPart == null) return;
            _busy = true; UpdateControls();
            try
            {
                var model = LibraryModelChoice.Browse(SelectedPart.Mpn);
                if (model == null) return;
                SelectedModel = model;
                ShowPreview();
                _imported = SupplierLibraryImporter.Import(SelectedPart, SelectedOffer, SelectedModel);
                _importedModel = SelectedModel; _importedOffer = SelectedOffer;
                modelText.Text = "Ready: " + _imported.Reference;
                statusText.Text = "Imported to library. Choose Place part to use it on a schematic.";
            }
            catch (Exception error) { statusText.Text = "Could not choose a library component: " + error.Message; }
            finally { _busy = false; UpdateControls(); }
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true; _documentTimer.Stop(); _previewDelay?.Cancel(); _previewDelay?.Dispose(); _request?.Cancel(); _request?.Dispose();
        }
    }
}
