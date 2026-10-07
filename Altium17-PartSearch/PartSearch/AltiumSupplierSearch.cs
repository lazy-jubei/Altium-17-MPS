using SCH;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;

namespace Altium17PartSearch.PartSearch
{
    internal sealed class AltiumSupplierSearch
    {
        internal sealed class Provider
        {
            internal ISupplier Native { get; set; }
            internal ISupplierManager Manager { get; set; }
            public string Name { get; set; }
            public override string ToString() => Name;
        }

        internal sealed class Page
        {
            internal List<SupplierPart> Parts { get; } = new List<SupplierPart>();
            internal int RawCount { get; set; }
            internal int? Total { get; set; }
            internal bool SupportsPaging { get; set; }
        }

        internal const int PageSize = 50;

        internal async Task<(List<SupplierOffer> Offers, bool Limited)> RefreshOffersAsync(Provider provider, SupplierPart part,
            CancellationToken token, Action<string> progress)
        {
            var offers = new List<SupplierOffer>();
            // Keyword providers can return other MPNs as well. Scan their pages,
            // but bound broad searches so a short MPN cannot tie up the panel.
            const int maxPages = 10;
            for (int pageIndex = 0; pageIndex < maxPages; pageIndex++)
            {
                int offset = pageIndex * PageSize;
                progress($"Refreshing distributor stock… page {pageIndex + 1}");
                var page = await SearchAsync(provider, part.Mpn, offset, true, false, token);
                foreach (var match in page.Parts.Where(p => string.Equals(p.Manufacturer, part.Manufacturer, StringComparison.OrdinalIgnoreCase)))
                    SupplierAvailability.Merge(offers, match.Offers);
                bool more = page.SupportsPaging && (page.Total.HasValue ? offset + PageSize < page.Total.Value : page.RawCount >= PageSize);
                if (!more) return (offers, false);
            }
            return (offers, true);
        }

        internal List<Provider> GetProviders()
        {
            var manager = EDP.Utils.GetSupplierManager()
                ?? throw new InvalidOperationException("Altium's supplier manager is unavailable. Enable the Supplier Search extension in Altium.");
            var result = new List<Provider>();
            for (int i = 0; i < manager.GetSupplierSourceCount(); i++)
            {
                var source = manager.GetSupplierSource(i);
                if (source != null && source.GetState_Enabled() && !source.GetState_Hidden())
                    result.Add(new Provider { Native = source, Manager = manager, Name = source.SourceName() });
            }
            return result.OrderByDescending(p => p.Name.IndexOf("Altium", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal async Task<Page> SearchAsync(Provider provider, string query, int offset, bool mpnOnly, bool inStock, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // The SDK's asynchronous provider API reports results to a window on
            // Altium's UI thread. Do not move its COM objects to Task.Run workers.
            using var sink = new SearchSink(provider, token);
            uint handle = unchecked((uint)sink.Handle.ToInt32());
            // AD17's Ciiva MPN endpoint fails even for parts its keyword search
            // returns. Keep the working native search and filter exact MPNs.
            bool keyword = !mpnOnly || string.Equals(provider.Name, "Ciiva", StringComparison.OrdinalIgnoreCase);
            if (!keyword) provider.Native.BackgroundSearchByManufacturerPartNumber(query, handle);
            else
            {
                var filters = new TSupplierSearchFilterSet();
                if (inStock && provider.Native.SupportedSearchFilters().Contains(TSupplierSearchFilter.eFilter_InStock))
                    filters.Add(TSupplierSearchFilter.eFilter_InStock);
                provider.Native.BackgroundSearchByKeyword(query, PageSize, offset, filters, null, null, handle);
            }
            var page = await sink.Completion;
            page.SupportsPaging = keyword;
            if (keyword && mpnOnly)
                page.Parts.RemoveAll(p => !string.Equals(p.Mpn?.Trim(), query.Trim(), StringComparison.OrdinalIgnoreCase));
            if (inStock) page.Parts.RemoveAll(p => !p.Offers.Any(o => o.Stock.GetValueOrDefault() > 0));
            return page;
        }

        private sealed class SearchSink : NativeWindow, IDisposable
        {
            // AD17 SupplierSearchHandler's window-message protocol.
            private const int ResultMessage = 33168, StatisticsMessage = 33169, ErrorMessage = 33170;
            private const int EndKeywordMessage = 33179, CompletedMessage = 33181, ContinueMessage = 33183;
            private readonly Provider _provider;
            private readonly Page _page = new Page();
            private readonly TaskCompletionSource<Page> _completion = new TaskCompletionSource<Page>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly DispatcherTimer _timeout;
            private readonly CancellationTokenRegistration _registration;
            private int _canceled;
            private bool _disposed;
            internal Task<Page> Completion => _completion.Task;

            internal SearchSink(Provider provider, CancellationToken token)
            {
                _provider = provider;
                var dispatcher = Dispatcher.CurrentDispatcher;
                CreateHandle(new CreateParams { Caption = "AD17 Part Search Callback", Parent = new IntPtr(-3) });
                // AD17 providers validate GWL_ID against the callback window handle.
                SetWindowLong(Handle, -12, Handle.ToInt32());
                if (GetWindowLong(Handle, -12) != Handle.ToInt32())
                {
                    DestroyHandle();
                    throw new InvalidOperationException("Could not register Altium's supplier-search callback window.");
                }
                _timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
                _timeout.Tick += (s, e) =>
                {
                    Interlocked.Exchange(ref _canceled, 1);
                    _completion.TrySetException(new TimeoutException("Altium's supplier search timed out. Check its sign-in, internet access, and supplier configuration."));
                    _timeout.Stop();
                };
                _timeout.Start();
                _registration = token.Register(() =>
                {
                    Interlocked.Exchange(ref _canceled, 1);
                    dispatcher.BeginInvoke(new Action(() => _completion.TrySetCanceled()));
                });
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == ContinueMessage)
                {
                    message.Result = Volatile.Read(ref _canceled) == 0 ? new IntPtr(1) : IntPtr.Zero;
                    return;
                }
                if (message.Msg == EndKeywordMessage || message.Msg == CompletedMessage)
                {
                    _timeout.Stop();
                    _completion.TrySetResult(_page);
                    return;
                }
                if (message.Msg != ResultMessage && message.Msg != StatisticsMessage && message.Msg != ErrorMessage)
                {
                    base.WndProc(ref message);
                    return;
                }
                if (message.WParam == IntPtr.Zero) return;
                try
                {
                    if (Volatile.Read(ref _canceled) != 0 || _completion.Task.IsCompleted) return;
                    var value = Marshal.GetObjectForIUnknown(message.WParam);
                    if (message.Msg == ResultMessage)
                    {
                        var part = SupplierPart.Read(value as ISupplierSourceRelationship
                            ?? throw new InvalidOperationException("Altium returned an unsupported supplier result."), _provider.Native, _provider.Manager);
                        _page.RawCount++;
                        // Group offers only when both manufacturer and MPN are known.
                        var existing = string.IsNullOrWhiteSpace(part.Manufacturer) || string.IsNullOrWhiteSpace(part.Mpn) ? null :
                            _page.Parts.FirstOrDefault(p => string.Equals(p.Manufacturer, part.Manufacturer, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(p.Mpn, part.Mpn, StringComparison.OrdinalIgnoreCase));
                        if (existing == null) _page.Parts.Add(part);
                        else
                        {
                            SupplierAvailability.Merge(existing.Offers, part.Offers);
                            if (existing.Datasheet == null) existing.Datasheet = part.Datasheet;
                            foreach (var p in part.Parameters) if (!existing.Parameters.ContainsKey(p.Key)) existing.Parameters[p.Key] = p.Value;
                        }
                    }
                    else if (message.Msg == StatisticsMessage)
                    {
                        var statistics = value as ISupplierSearchStatistics;
                        if (statistics != null) _page.Total = statistics.GetState_RecordCount();
                    }
                    else
                    {
                        string error = (value as ISupplierSearchError)?.ErrorMessage();
                        _completion.TrySetException(new InvalidOperationException($"{_provider.Name} provider reported: {error ?? "Search failed."}"));
                    }
                }
                catch (Exception error) { _completion.TrySetException(error); }
                finally
                {
                    // Release the reference transferred in the message; retain no
                    // Altium result COM objects in our view models.
                    Marshal.Release(message.WParam);
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Interlocked.Exchange(ref _canceled, 1);
                _registration.Dispose();
                _timeout.Stop();
                // Invalidate the SDK's handle check before dropping the window.
                // Release result pointers already queued when a search is canceled.
                SetWindowLong(Handle, -12, 0);
                while (PeekMessage(out var queued, Handle, ResultMessage, ErrorMessage, 1))
                    if (queued.WParam != IntPtr.Zero) Marshal.Release(queued.WParam);
                DestroyHandle();
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct NativeMessage
            {
                internal IntPtr Window;
                internal uint Id;
                internal IntPtr WParam, LParam;
                internal uint Time;
                internal int X, Y;
                internal uint Private;
            }

            [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);

            [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
            private static extern int SetWindowLong(IntPtr window, int index, int value);
            [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
            private static extern int GetWindowLong(IntPtr window, int index);
        }
    }
}
