﻿global using Alpaca.Markets;
global using Alpaca.Markets.Extensions;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using AlpacaEnvironment = Alpaca.Markets.Environments;

namespace AlpacaDashboard.Brokers;

/// <summary>
/// This class handles all request related Alpaca Market and Data api
/// </summary>
public class Broker : IDisposable
{
    #region public and private properties
    private string key;
    private string secret;
    public bool subscribed;

    public IAlpacaTradingClient alpacaTradingClient { get; set; } = default!;

    public IAlpacaDataClient alpacaDataClient { get; set; } = default!;
    public IAlpacaDataStreamingClient alpacaDataStreamingClient { get; set; } = default!;
    public IAlpacaStreamingClient alpacaStreamingClient { get; set; } = default!;

    private SecretKey secretKey;

    private readonly ILogger _logger;
    private readonly IOptions<MySettings> _mySetting;

    private TimeZoneInfo easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private IReadOnlyList<ICalendar> MarketCalendar { get; set; } = default!;

    private CancellationToken token;
    public string Environment { get; set; }

    public CryptoExchange SelectedCryptoExchange { get; set; }

    static public IAlpacaCryptoDataClient alpacaCryptoDataClient { get; set; } = default!;
    static public IAlpacaCryptoStreamingClient alpacaCryptoStreamingClient { get; set; } = default!;
    static bool CryptoConnected = false;
    static string CryptoConnectedEnvironment = "";

    #endregion

    #region constructor
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="token"></param>
    /// <param name="key"></param>
    /// <param name="secret"></param>
    /// <param name="live"></param>
    /// <param name="mySetting"></param>
    /// <param name="logger"></param>
    public Broker(CancellationToken token, string key, string secret, string environment, IOptions<MySettings> mySetting, ILogger logger)
    {
        this.token = token;
        _logger = logger;
        _mySetting = mySetting;

        //alpaca client
        this.key = key;
        this.secret = secret;
        Environment = environment;

        subscribed = _mySetting.Value.Subscribed;

        SelectedCryptoExchange = (CryptoExchange)Enum.Parse(typeof(CryptoExchange), mySetting.Value.CryptoExchange);

        secretKey = new(key, secret);

        if (Environment == "Live")
        {
            alpacaTradingClient = AlpacaEnvironment.Live.GetAlpacaTradingClient(secretKey);
            alpacaDataClient = AlpacaEnvironment.Live.GetAlpacaDataClient(secretKey);

            //connect only in one environment
            if (!CryptoConnected)
            {
                alpacaCryptoDataClient = AlpacaEnvironment.Live.GetAlpacaCryptoDataClient(secretKey);
                CryptoConnectedEnvironment = Environment;
                CryptoConnected = true;
            }
        }
        if (Environment == "Paper")
        {
            alpacaTradingClient = AlpacaEnvironment.Paper.GetAlpacaTradingClient(secretKey);
            alpacaDataClient = AlpacaEnvironment.Paper.GetAlpacaDataClient(secretKey);

            //connect only in one environment
            if (!CryptoConnected)
            {
                alpacaCryptoDataClient = AlpacaEnvironment.Paper.GetAlpacaCryptoDataClient(secretKey);
                CryptoConnectedEnvironment = Environment;
                CryptoConnected = true;
            }
        }

        //streaming client
        if (subscribed)
        {
            if (Environment == "Live")
            {
                // Connect to Alpaca's websocket and listen for updates on our orders.
                alpacaStreamingClient = AlpacaEnvironment.Live.GetAlpacaStreamingClient(secretKey).WithReconnect();

                // Connect to Alpaca's websocket and listen for price updates.
                alpacaDataStreamingClient = AlpacaEnvironment.Live.GetAlpacaDataStreamingClient(secretKey).WithReconnect();

                //connect only in one environment
                if (CryptoConnectedEnvironment == Environment)
                    alpacaCryptoStreamingClient = AlpacaEnvironment.Live.GetAlpacaCryptoStreamingClient(secretKey).WithReconnect();
            }
            if (Environment == "Paper")
            {
                // Connect to Alpaca's websocket and listen for updates on our orders.
                alpacaStreamingClient = AlpacaEnvironment.Paper.GetAlpacaStreamingClient(secretKey).WithReconnect();

                // Connect to Alpaca's websocket and listen for price updates.
                alpacaDataStreamingClient = AlpacaEnvironment.Paper.GetAlpacaDataStreamingClient(secretKey).WithReconnect();

                //connect only in one environment
                if (CryptoConnectedEnvironment == Environment)
                    alpacaCryptoStreamingClient = AlpacaEnvironment.Paper.GetAlpacaCryptoStreamingClient(secretKey).WithReconnect();
            }

            //Streaming client event
            alpacaStreamingClient.OnTradeUpdate += AlpacaStreamingClient_OnTradeUpdate;
            alpacaStreamingClient.OnError += AlpacaStreamingClient_OnError;
            alpacaStreamingClient.OnWarning += AlpacaStreamingClient_OnWarning;

            //Data Streaming client event
            alpacaDataStreamingClient.OnError += AlpacaDataStreamingClient_OnError;
            alpacaDataStreamingClient.OnWarning += AlpacaDataStreamingClient_OnWarning;
            alpacaDataStreamingClient.Connected += AlpacaDataStreamingClient_Connected;
            alpacaDataStreamingClient.SocketOpened += AlpacaDataStreamingClient_SocketOpened;
            alpacaDataStreamingClient.SocketClosed += AlpacaDataStreamingClient_SocketClosed;

            alpacaCryptoStreamingClient.OnError += AlpacaCryptoStreamingClient_OnError;
            alpacaCryptoStreamingClient.OnWarning += AlpacaCryptoStreamingClient_OnWarning;
            alpacaCryptoStreamingClient.Connected += AlpacaCryptoStreamingClient_Connected;
            alpacaCryptoStreamingClient.SocketOpened += AlpacaCryptoStreamingClient_SocketOpened;
            alpacaCryptoStreamingClient.SocketClosed += AlpacaCryptoStreamingClient_SocketClosed;
        }

        //GetMarketOpenClose().GetAwaiter().GetResult();
    }

    #endregion

    #region connect method
    /// <summary>
    /// Connects to the streaming API if subscribed, else runs a loop to get the data in a periodic interval
    /// </summary>
    /// <returns></returns>
    public async Task Connect()
    {
        if (subscribed)
        {
            //connect
            await alpacaStreamingClient.ConnectAndAuthenticateAsync().ConfigureAwait(false); 
            await alpacaDataStreamingClient.ConnectAndAuthenticateAsync().ConfigureAwait(false); 

            //connect only in one environment
            if (CryptoConnectedEnvironment == Environment)
            {
                await alpacaCryptoStreamingClient.ConnectAndAuthenticateAsync().ConfigureAwait(false); 
            }
        }
    }
    #endregion

    #region warning and error events
    private void AlpacaStreamingClient_OnWarning(string obj)
    {
        _logger.LogWarning($"{Environment} StreamingClient Warning");
    }
    private void AlpacaStreamingClient_OnError(Exception obj)
    {
        _logger.LogError($"{Environment} StreamingClient Exception {obj.Message}");
    }

    private void AlpacaDataStreamingClient_OnWarning(string obj)
    {
        _logger.LogWarning($"{Environment} DataStreamingClient socket warning");
    }
    private void AlpacaDataStreamingClient_OnError(Exception obj)
    {
        _logger.LogError($"{Environment} DataStreamingClient socket error {obj.Message}");
    }
    private void AlpacaDataStreamingClient_SocketOpened()
    {
        _logger.LogInformation($"{Environment} DataStreamingClient socket opened");
    }
    private async void AlpacaDataStreamingClient_Connected(AuthStatus obj)
    {
        _logger.LogInformation($"{Environment} DataStreamingClient Auth status {obj.ToString()}");

        if (obj.ToString() == "Authorized")
        {
            //update for the first time after authorized
            await UpdateEnviromentData().ConfigureAwait(false); 
        }
    }
    private void AlpacaDataStreamingClient_SocketClosed()
    {
        _logger.LogInformation($"{Environment} DataStreamingClient socket closed ");
    }


    private void AlpacaCryptoStreamingClient_OnWarning(string obj)
    {
        _logger.LogWarning($"{Environment} CryptoStreamingClient Warning");
    }
    private void AlpacaCryptoStreamingClient_OnError(Exception obj)
    {
        _logger.LogError($"{Environment} CryptoStreamingClient Exception {obj.Message}");
    }
    private void AlpacaCryptoStreamingClient_SocketOpened()
    {
        _logger.LogInformation($"{Environment} CryptoStreamingClient socket opened");
    }
    private void AlpacaCryptoStreamingClient_Connected(AuthStatus obj)
    {
        _logger.LogInformation($"{Environment} CryptoStreamingClient Auth status {obj.ToString()}");

        if (obj.ToString() == "Authorized")
        {
            //update for the first time after authorized
            //await UpdateEnviromentData();
        }
    }
    private void AlpacaCryptoStreamingClient_SocketClosed()
    {
        _logger.LogInformation($"{Environment} CryptoStreamingClient socket closed ");
    }
    #endregion

    #region Market Methods
    /// <summary>
    /// Get the market open close time
    /// </summary>
    /// <returns></returns>
    public async Task<IReadOnlyList<ICalendar>> GetMarketOpenClose()
    {
        var today = DateTime.Today;
        var interval = today.AddDays(-2).GetInclusiveIntervalFromThat().WithInto(today);
        var calendar = await alpacaTradingClient.ListCalendarAsync(new CalendarRequest().SetTimeInterval(interval), token).ConfigureAwait(false); 
        var calendarDate = calendar.Last().TradingOpenTimeEst;
        var closingTime = calendar.Last().TradingCloseTimeEst;
        MarketCalendar = calendar;
        return calendar;
    }

    /// <summary>
    /// can be used to wait till the market open
    /// </summary>
    /// <returns></returns>
    private async Task AwaitMarketOpen()
    {
        while (!(await alpacaTradingClient.GetClockAsync(token)).IsOpen)
        {
            await Task.Delay(60000);
        }
    }
    #endregion

    #region Order Handling Methods

    /// <summary>
    /// Delete a open order by order id
    /// </summary>
    /// <param name="orderId"></param>
    /// <returns></returns>
    public async Task<bool> DeleteOpenOrder(Guid orderId)
    {
        return await alpacaTradingClient.DeleteOrderAsync(orderId, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete all open order
    /// </summary>
    /// <param name="symbol"></param>
    /// <returns></returns>
    public async Task DeleteOpenOrders(string symbol)
    {
        var orders = await alpacaTradingClient.ListOrdersAsync(new ListOrdersRequest(), token).ConfigureAwait(false);
        foreach (var order in orders.ToList())
        {
            await alpacaTradingClient.DeleteOrderAsync(order.OrderId, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Submit Order of any type
    /// </summary>
    /// <param name="symbol"></param>
    /// <param name="quantity"></param>
    /// <param name="limitPrice"></param>
    /// <param name="orderSide"></param>
    /// <param name="orderType"></param>
    /// <param name="timeInForce"></param>
    /// <returns></returns>
    public async Task<(IOrder?, string?)> SubmitOrder(OrderSide orderSide, OrderType orderType, TimeInForce timeInForce, bool extendedHours, string symbol, OrderQuantity quantity, decimal? stopPrice,
        decimal? limitPrice, int? trailOffsetPercentage, decimal? trailOffsetDollars)
    {
        IOrder? order = null;
        string? message = null;

        try
        {
            switch (orderType)
            {
                case OrderType.Market:
                    message = $"Market {orderSide.ToString()} of {quantity.Value.ToString()} on {TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, easternZone).ToString()}, TimeInForce : {timeInForce.ToString()} Extended Hours {extendedHours.ToString()}";
                    order = await alpacaTradingClient.PostOrderAsync(new NewOrderRequest(symbol, quantity, orderSide, orderType, timeInForce) { ExtendedHours = extendedHours }).ConfigureAwait(false);
                    break;
                case OrderType.Limit:
                    message = $"Limit {orderSide.ToString()} of {quantity.Value.ToString()} @ {limitPrice.ToString()} on {TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, easternZone).ToString()}, TimeInForce : {timeInForce.ToString()} Extended Hours {extendedHours.ToString()}";
                    order = await alpacaTradingClient.PostOrderAsync(new NewOrderRequest(symbol, quantity, orderSide, orderType, timeInForce) { ExtendedHours = extendedHours, LimitPrice = limitPrice }).ConfigureAwait(false);
                    break;
                case OrderType.Stop:
                    message = $"Stop {orderSide.ToString()} of {quantity.Value.ToString()} @ stop price: {stopPrice.ToString()} on {TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, easternZone).ToString()}, TimeInForce : {timeInForce.ToString()} Extended Hours {extendedHours.ToString()}";
                    order = await alpacaTradingClient.PostOrderAsync(new NewOrderRequest(symbol, quantity, orderSide, orderType, timeInForce) { ExtendedHours = extendedHours, StopPrice = stopPrice }).ConfigureAwait(false);
                    break;
                case OrderType.StopLimit:
                    message = $"StopLimit {orderSide.ToString()} of {quantity.Value.ToString()} @ stop price {stopPrice.ToString()} and limit price {limitPrice.ToString()} on {TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, easternZone).ToString()}, TimeInForce : {timeInForce.ToString()} Extended Hours {extendedHours.ToString()}";
                    order = await alpacaTradingClient.PostOrderAsync(new NewOrderRequest(symbol, quantity, orderSide, orderType, timeInForce) { ExtendedHours = extendedHours, StopPrice = stopPrice, LimitPrice = limitPrice }).ConfigureAwait(false);
                    break;
                case OrderType.TrailingStop:
                    message = $"TrailingStop {orderSide.ToString()} of {quantity.Value.ToString()} @ stop price: {stopPrice.ToString()} and trailing {trailOffsetDollars.ToString()} {trailOffsetPercentage.ToString()} on {TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, easternZone).ToString()}, TimeInForce : {timeInForce.ToString()} Extended Hours {extendedHours.ToString()}";
                    order = await alpacaTradingClient.PostOrderAsync(new NewOrderRequest(symbol, quantity, orderSide, orderType, timeInForce) { ExtendedHours = extendedHours, StopPrice = stopPrice, TrailOffsetInDollars = trailOffsetDollars, TrailOffsetInPercent = trailOffsetPercentage }).ConfigureAwait(false);
                    break;
            }
            return (order, message);
        }
        catch (Exception ex)
        {
            //MessageBox.Show(ex.Message);
            _logger.LogInformation($"{Environment}  {message + ":" + ex.Message}");
            return (null, message + ":" + ex.Message);
        }
    }

    /// <summary>
    /// submits a new limit order
    /// </summary>
    /// <param name="symbol"></param>
    /// <param name="quantity"></param>
    /// <param name="price"></param>
    /// <param name="orderSide"></param>
    /// <returns></returns>
    public async Task SubmitLimitOrder(string symbol, long quantity, Decimal price, OrderSide orderSide)
    {
        if (quantity == 0)
        {
            return;
        }
        try
        {
            var order = await alpacaTradingClient.PostOrderAsync(orderSide.Limit(symbol, quantity, price), token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"{Environment}  {ex.Message}");
        }
    }

    /// <summary>
    /// submits a new market order
    /// </summary>
    /// <param name="symbol"></param>
    /// <param name="quantity"></param>
    /// <param name="price"></param>
    /// <param name="orderSide"></param>
    /// <returns></returns>
    public async Task SubmitMarketOrder(string symbol, long quantity, OrderSide orderSide)
    {
        if (quantity == 0)
        {
            return;
        }
        try
        {
            var order = await alpacaTradingClient.PostOrderAsync(orderSide.Market(symbol, quantity), token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"{Environment}  {ex.Message}");
        }
    }


    /// <summary>
    /// close a position at market
    /// </summary>
    /// <param name="symbol"></param>
    private async void ClosePositionAtMarket(string symbol)
    {
        try
        {
            var positionQuantity = (await alpacaTradingClient.GetPositionAsync(symbol)).IntegerQuantity;
            Console.WriteLine("Symbol {1}, Closing position at market price.", symbol);
            if (positionQuantity > 0)
            {
                await alpacaTradingClient.PostOrderAsync(
                    OrderSide.Sell.Market(symbol, positionQuantity), token);
            }
            else
            {
                await alpacaTradingClient.PostOrderAsync(
                    OrderSide.Buy.Market(symbol, Math.Abs(positionQuantity)), token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"{Environment} {symbol} {ex.Message}");
        }
    }

    /// <summary>
    /// Get latest trade for a symbol
    /// </summary>
    /// <param name="symbol"></param>
    /// <returns></returns>
    public async Task<ITrade?> GetLatestTrade(string symbol)
    {
        var asset = await GetAsset(symbol).ConfigureAwait(false);

        try
        {
            if (asset.Class == AssetClass.Crypto)
            {
                var ldr = new LatestDataRequest(symbol, SelectedCryptoExchange);
                return await alpacaCryptoDataClient.GetLatestTradeAsync(ldr, token).ConfigureAwait(false);
            }
            if (asset.Class == AssetClass.UsEquity)
            {
                return await alpacaDataClient.GetLatestTradeAsync(symbol, token).ConfigureAwait(false);
            }
        }
        catch { }

        return null;
    }


    /// <summary>
    /// Get latest quote for a symbol
    /// </summary>
    /// <param name="symbol"></param>
    /// <returns></returns>
    public async Task<IQuote?> GetLatestQuote(string symbol)
    {
        var asset = await GetAsset(symbol).ConfigureAwait(false);

        try
        {
            if (asset.Class == AssetClass.Crypto)
            {
                var ldr = new LatestDataRequest(symbol, SelectedCryptoExchange);
                return await alpacaCryptoDataClient.GetLatestQuoteAsync(ldr, token).ConfigureAwait(false);
            }
            if (asset.Class == AssetClass.UsEquity)
            {
                return await alpacaDataClient.GetLatestQuoteAsync(symbol, token).ConfigureAwait(false);
            }
        }
        catch { }

        return null;
    }

    public async Task<ISnapshot?> GetSnapshot(string symbol)
    {
        var asset = await GetAsset(symbol).ConfigureAwait(false);

        if (asset.Class == AssetClass.UsEquity)
        {
            return await alpacaDataClient.GetSnapshotAsync(asset.Symbol, token).ConfigureAwait(false);
        }
        if (asset.Class == AssetClass.Crypto)
        {
            var ieal = asset.Symbol.ToList();
            var sdr = new SnapshotDataRequest(asset.Symbol, SelectedCryptoExchange);
            return await alpacaCryptoDataClient.GetSnapshotAsync(sdr, token).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Get current position for a sysmbol
    /// </summary>
    /// <param name="symbol"></param>
    /// <returns></returns>
    public async Task<IPosition?> GetCurrentPosition(string symbol)
    {
        try
        {
            return await alpacaTradingClient.GetPositionAsync(symbol, token).ConfigureAwait(false);
        }
        catch { }

        return null;
    }

    #endregion

    #region Alpaca streaming Events at market level
    /// <summary>
    /// event handler to receive trade related data in your portfolio
    /// if its a new symbol then subscribe data for it
    /// </summary>
    /// <param name="obj"></param>
    private async void AlpacaStreamingClient_OnTradeUpdate(ITradeUpdate obj)
    {
        var asset = await GetAsset(obj.Order.Symbol).ConfigureAwait(false);
        if (obj.Order.OrderStatus == OrderStatus.Filled || obj.Order.OrderStatus == OrderStatus.PartiallyFilled)
        {
            IStock? stock = null;
            await Stock.Subscribe(this, obj.Order.Symbol, "Portfolio").ConfigureAwait(false);

            if (stock != null)
            {
                stock.TradeUpdate = obj;
            }

            var tr = obj.TimestampUtc == null ? "" : TimeZoneInfo.ConvertTimeFromUtc((DateTime)obj.TimestampUtc, easternZone).ToString();
            var tn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, easternZone).ToString();
            _logger.LogInformation($"Trade : {obj.Order.Symbol}, Current Qty: {obj.PositionQuantity}, Current Price: {obj.Price}, Trade Qty: {obj.Order.FilledQuantity}, " +
                $"Trade Side {obj.Order.OrderSide}, Fill Price: {obj.Order.AverageFillPrice} TradeId: {obj.Order.OrderId}, TimeEST: {tr}, Current Time: {tn}");

            await UpdateEnviromentData().ConfigureAwait(false);
        }
        if (obj.Order.OrderStatus == OrderStatus.New || obj.Order.OrderStatus == OrderStatus.Accepted || obj.Order.OrderStatus == OrderStatus.Canceled)
        {
            await UpdateOpenOrders().ConfigureAwait(false);
            await UpdateClosedOrders().ConfigureAwait(false);
        }
    }

    #endregion

    #region Account Method and UI Events

    /// <summary>
    /// Get Account data
    /// </summary>
    /// <returns></returns>
    public async Task<IAccount?> GetAccountDetails()
    {
        try
        {
            return await alpacaTradingClient.GetAccountAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError($"{Environment} {ex.Message}");
        }

        return null;
    }

    public delegate void AccountUpdatedEventHandler(object sender, AccountUpdatedEventArgs e);

    public event EventHandler AccountUpdated = default!;
    protected virtual void OnAccountUpdated(EventArgs e)
    {
        EventHandler handler = AccountUpdated;
        handler?.Invoke(this, e);
    }

    /// <summary>
    /// Send account data as event
    /// </summary>
    /// <returns></returns>
    public async Task UpdateAccounts()
    {

        var account = await GetAccountDetails().ConfigureAwait(false);

        AccountUpdatedEventArgs oauea = new()
        {
            Account = account
        };
        OnAccountUpdated(oauea);
    }
    #endregion

    #region Positions Method and UI Events
    /// <summary>
    /// generate a event for UI to list curent positions
    /// </summary>
    /// <returns></returns>
    public async Task<IReadOnlyCollection<IPosition>> ListPositions()
    {
        var positions = await alpacaTradingClient.ListPositionsAsync(token).ConfigureAwait(false);
        return positions;
    }

    public delegate void PositionUpdatedEventHandler(object sender, PositionUpdatedEventArgs e);

    public event EventHandler PositionUpdated = default!;
    protected virtual void OnPositionUpdated(EventArgs e)
    {
        EventHandler handler = PositionUpdated;
        handler?.Invoke(this, e);
    }

    /// <summary>
    /// send positions data as a event
    /// </summary>
    /// <returns></returns>
    public async Task UpdatePositions()
    {

        var positions = await ListPositions().ConfigureAwait(false);

        PositionUpdatedEventArgs opuea = new PositionUpdatedEventArgs
        {
            Positions = positions
        };
        OnPositionUpdated(opuea);
    }
    #endregion

    #region Closed Orders Methods and UI Events

    /// <summary>
    /// generate a event for UI to list last 50 closed position
    /// </summary>
    /// <returns></returns>
    public async Task<IReadOnlyCollection<IOrder>> ClosedOrders()
    {
        ListOrdersRequest request = new()
        {
            OrderStatusFilter = OrderStatusFilter.Closed,
            LimitOrderNumber = 50
        };
        var orders = await alpacaTradingClient.ListOrdersAsync(request, token).ConfigureAwait(false);
        return orders;
    }
    public delegate void ClosedOrderUpdatedEventHandler(object sender, ClosedOrderUpdatedEventArgs e);

    public event EventHandler ClosedOrderUpdated = default!;

    protected virtual void OnClosedOrderUpdated(EventArgs e)
    {
        EventHandler handler = ClosedOrderUpdated;
        handler?.Invoke(this, e);
    }

    /// <summary>
    /// send closed orders as event
    /// </summary>
    /// <returns></returns>
    public async Task UpdateClosedOrders()
    {

        var closedOrders = await ClosedOrders().ConfigureAwait(false);

        ClosedOrderUpdatedEventArgs ocouea = new ClosedOrderUpdatedEventArgs
        {
            ClosedOrders = closedOrders
        };
        OnClosedOrderUpdated(ocouea);
    }
    #endregion

    #region Open Orders Method and UI Events

    /// <summary>
    /// generate a event for UI to list open orders
    /// </summary>
    /// <returns></returns>
    public async Task<IReadOnlyCollection<IOrder>> OpenOrders()
    {
        ListOrdersRequest request = new()
        {
            OrderStatusFilter = OrderStatusFilter.Open
        };
        var orders = await alpacaTradingClient.ListOrdersAsync(request, token).ConfigureAwait(false);

        return orders;
    }

    public delegate void OpenOrderUpdatedEventHandler(object sender, OpenOrderUpdatedEventArgs e);

    public event EventHandler OpenOrderUpdated = default!;
    protected virtual void OnOpenOrderUpdated(EventArgs e)
    {
        EventHandler handler = OpenOrderUpdated;
        handler?.Invoke(this, e);
    }

    /// <summary>
    /// send open orders as a event
    /// </summary>
    /// <returns></returns>
    public async Task UpdateOpenOrders()
    {

        var openOrders = await OpenOrders();

        OpenOrderUpdatedEventArgs ooruea = new OpenOrderUpdatedEventArgs
        {
            OpenOrders = openOrders
        };
        OnOpenOrderUpdated(ooruea);
    }
    #endregion

    #region Watchlist Methods
    public async Task<IWatchList> CreateWatchList(string name, IEnumerable<string> symbols)
    {
        NewWatchListRequest newWatchListRequest = new NewWatchListRequest(name, symbols);
        return await alpacaTradingClient.CreateWatchListAsync(newWatchListRequest, token).ConfigureAwait(false);
    }
    public async Task<IWatchList> GetWatchList(string name)
    {
        return await alpacaTradingClient.GetWatchListByNameAsync(name, token).ConfigureAwait(false);
    }
    public async Task<IWatchList> UpdateWatchList(IWatchList wl, IEnumerable<IAsset> assets)
    {
        var symbols = assets.Select(x => x.Symbol).ToList();
        UpdateWatchListRequest updateWatchListRequest = new UpdateWatchListRequest(wl.WatchListId, wl.Name, symbols);
        return await alpacaTradingClient.UpdateWatchListByIdAsync(updateWatchListRequest, token).ConfigureAwait(false);
    }
    public async void DeleteItemFromWatchList(IWatchList wl, IAsset asset)
    {
        ChangeWatchListRequest<Guid> changeWatchListRequest = new ChangeWatchListRequest<Guid>(wl.WatchListId, asset.Symbol);
        await alpacaTradingClient.DeleteAssetFromWatchListByIdAsync(changeWatchListRequest, token).ConfigureAwait(false);
    }
    public async void AddItemToWatchList(IWatchList wl, string symbol)
    {
        ChangeWatchListRequest<Guid> changeWatchListRequest = new ChangeWatchListRequest<Guid>(wl.WatchListId, symbol);
        await alpacaTradingClient.AddAssetIntoWatchListByIdAsync(changeWatchListRequest, token).ConfigureAwait(false);
    }
    #endregion

    #region other methods

    /// <summary>
    /// Get positions for a list of symbols
    /// </summary>
    /// <returns></returns>
    public async Task<Dictionary<IAsset, IPosition?>> GetPositionsforAssetList(IEnumerable<IAsset> assets)
    {
        Dictionary<IAsset, IPosition?> positions = new();

        foreach (var asset in assets)
        {
            IPosition? position = null;
            try
            {
                position = await alpacaTradingClient.GetPositionAsync(asset.Symbol, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            positions.Add(asset, position);
        }

        return positions;
    }

    /// <summary>
    /// Generate events to refresh UI when eniviroment changes (Live, Paper)
    /// Seperate Events for Account data, open orders, closed orders and positions
    /// Called by UI when environment changes
    /// </summary>
    /// <returns></returns>
    public async Task UpdateEnviromentData()
    {
        await UpdateAccounts().ConfigureAwait(false);
        await UpdateOpenOrders().ConfigureAwait(false);
        await UpdateClosedOrders().ConfigureAwait(false);
        await UpdatePositions().ConfigureAwait(false);
    }

    /// <summary>
    /// get a list of symbols that have a position or open order
    /// called when a position or open order is updated
    /// used by UI to update Position and Open Order watchlist 
    /// </summary>
    /// <returns></returns>
    public async Task<Dictionary<IAsset, ISnapshot?>> PositionAndOpenOrderAssets()
    {
        Dictionary<IAsset, ISnapshot?> assetAndSnapShots = new();
        List<string> symbols = new();

        //all positions
        var positions = await ListPositions().ConfigureAwait(false);
        foreach (var position in positions.ToList())
        {
            symbols.Add(position.Symbol);
        }

        //all open orders
        var openOrders = await OpenOrders().ConfigureAwait(false);
        foreach (var order in openOrders.ToList())
        {
            symbols.Add(order.Symbol);
        }

        //find unique symbols and then get snapshots and subscribe
        var symbolList = new HashSet<string>(symbols);
        foreach (var symbol in symbolList)
        {
            var asset = await GetAsset(symbol).ConfigureAwait(false);

            if (asset != null)
            {
                try
                {
                    //get snapshots
                    var ss = await GetSnapshot(symbol).ConfigureAwait(false);

                    if (ss != null)
                    {
                        assetAndSnapShots.Add(asset, ss);
                    }

                    //subscribe
                    await Stock.Subscribe(this, asset.Symbol, "Portfolio").ConfigureAwait(false);
                }
                catch { }
            }
        }

        return assetAndSnapShots;
    }


    /// <summary>
    /// Get Asset of symbol
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public async Task<IAsset> GetAsset(string name)
    {
        return await alpacaTradingClient.GetAssetAsync(name, token).ConfigureAwait(false);
    }

    /// <summary>
    /// get a list of asset in the market
    /// </summary>
    /// <returns></returns>
    public async Task<IReadOnlyList<IAsset>> GetAssets(AssetClass ac)
    {
        var ar = new AssetsRequest();
        ar.AssetClass = ac;
        IReadOnlyList<IAsset> assets = await alpacaTradingClient.ListAssetsAsync(ar, token).ConfigureAwait(false);
        return assets;
    }

    /// <summary>
    /// List of Symbols with its Snapshots
    /// </summary>
    /// <param name="assets"></param>
    /// <param name="maxAssetsAtOneTime"></param>
    /// <returns></returns>
    public async Task<Dictionary<IAsset, ISnapshot?>> ListSnapShots(IEnumerable<IAsset?> assets, int maxAssetsAtOneTime)
    {
        //dictionary to hold ISnapshot for each symbol
        Dictionary<IAsset, ISnapshot?> keyValues = new();

        //List to hold ISnapshot
        List<ISnapshot> snapshots = new();

        //get ISnapshot of stock symbols for assetCount at a time
        for (int i = 0; i < assets.Where(x => x?.Class == AssetClass.UsEquity).Count(); i += maxAssetsAtOneTime)
        {
            var assetSubset = assets.Where(x => x?.Class == AssetClass.UsEquity).Skip(i).Take(maxAssetsAtOneTime);
            var stockSnapshots = await alpacaDataClient.ListSnapshotsAsync((IEnumerable<string>)assetSubset.Select(x => x?.Symbol), token).ConfigureAwait(false);
            foreach (var item in stockSnapshots)
            {
                var asset = assets.Where(x => x?.Symbol == item.Key).First();
                if (asset != null)
                    keyValues.Add(asset, item.Value);
            }
        }
        //get ISnapshot of crypto symbols for assetCount at a time
        for (int i = 0; i < assets.Where(x => x?.Class == AssetClass.Crypto).Count(); i += maxAssetsAtOneTime)
        {
            var assetSubset = assets.Where(x => x?.Class == AssetClass.Crypto).Skip(i).Take(maxAssetsAtOneTime);
            var sdlr = new SnapshotDataListRequest((IEnumerable<string>)assetSubset.Select(x => x?.Symbol), SelectedCryptoExchange);
            var cryptoSnapshots = await alpacaCryptoDataClient.ListSnapshotsAsync(sdlr, token).ConfigureAwait(false);
            foreach (var item in cryptoSnapshots)
            {
                var asset = assets.Where(x => x?.Symbol == item.Key).First();
                if (asset != null)
                    keyValues.Add(asset, item.Value);
            }
        }
        return keyValues;
    }

    /// <summary>
    /// Gets Latest Trades for a symbol list
    /// </summary>
    /// <param name="assets"></param>
    /// <param name="maxAssetsAtOneTime"></param>
    /// <returns></returns>
    public async Task<Dictionary<string, ITrade>> ListTrades(IEnumerable<IAsset?> assets, int maxAssetsAtOneTime)
    {
        //dictionary to hold ISnapshot for each symbol
        Dictionary<string, ITrade> keyValues = new();

        //List to hold ISnapshot
        List<ITrade> trades = new();

        //get ISnapshot of stock symbols for assetCount at a time
        for (int i = 0; i < assets.Where(x => x?.Class == AssetClass.UsEquity).Count(); i += maxAssetsAtOneTime)
        {
            var assetSubset = assets.Where(x => x?.Class == AssetClass.UsEquity).Skip(i).Take(maxAssetsAtOneTime);
            var stockTrades = await alpacaDataClient.ListLatestTradesAsync((IEnumerable<string>)assetSubset.Select(x => x?.Symbol), token).ConfigureAwait(false);
            foreach (var item in stockTrades)
            {
                keyValues.Add(item.Key, item.Value);
            }
        }
        //get ISnapshot of crypto symbols for assetCount at a time
        for (int i = 0; i < assets.Where(x => x?.Class == AssetClass.Crypto).Count(); i += maxAssetsAtOneTime)
        {
            var assetSubset = assets.Where(x => x?.Class == AssetClass.Crypto).Skip(i).Take(maxAssetsAtOneTime);
            var ldlr = new LatestDataListRequest((IEnumerable<string>)assetSubset.Select(x => x?.Symbol), SelectedCryptoExchange);
            var cryptoSnapshots = await alpacaCryptoDataClient.ListLatestTradesAsync(ldlr, token).ConfigureAwait(false);
            foreach (var item in cryptoSnapshots)
            {
                keyValues.Add(item.Key, item.Value);
            }
        }
        return keyValues;
    }

    /// <summary>
    /// Get Historical bars for a asset
    /// </summary>
    /// <param name="asset"></param>
    /// <param name="barTimeFrameUnit"></param>
    /// <param name="barTimeFrameCount"></param>
    /// <param name="toDate"></param>
    /// <returns></returns>
    public async Task<IEnumerable<IBar>> GetHistoricalBar(IAsset? asset, BarTimeFrame barTimeFrame, int noOfBars, DateTime toDate)
    {
        //get the FromDate based on barTimeFrameUnit, barTimeFrameCount and toDate  (barTimeFrame can be 20Day, 15Min, 5Weeks etc)
        var fromDate = GetTimeIntervalFrom(barTimeFrame, noOfBars, toDate);

        List<IBar> bars = new();
        if (asset?.Class == AssetClass.UsEquity)
        {
            var historicalBarsRequest = new HistoricalBarsRequest(asset.Symbol, fromDate, toDate, barTimeFrame);
            await foreach (var bar in alpacaDataClient.GetHistoricalBarsAsAsyncEnumerable(historicalBarsRequest, token))
            {
                bars.Add(bar);
            }
        }
        if (asset?.Class == AssetClass.Crypto)
        {
            var historicalBarsRequest = new HistoricalCryptoBarsRequest(asset.Symbol, fromDate, toDate, barTimeFrame);
            await foreach (var bar in alpacaCryptoDataClient.GetHistoricalBarsAsAsyncEnumerable(historicalBarsRequest, token))
            {
                bars.Add(bar);
            }
        }
        return bars;
    }

    /// <summary>
    /// List of symbols and its Bars
    /// </summary>
    /// <param name="assets"></param>
    /// <param name="barTimeFrameUnit"></param>
    /// <param name="barTimeFrameCount"></param>
    /// <param name="maxAssetsAtOneTime"></param>
    /// <param name="toDate"></param>
    /// <returns></returns>
    public async Task<Dictionary<IAsset, List<IBar>>> ListHistoricalBars(IEnumerable<IAsset> assets, BarTimeFrame barTimeFrame, int noOfBars, int maxAssetsAtOneTime, DateTime toDate)
    {
        //get the FromDate based on barTimeFrameUnit, barTimeFrameCount and toDate  (barTimeFrame can be 20Day, 15Min, 5Weeks etc)
        var fromDate = GetTimeIntervalFrom(barTimeFrame, noOfBars, toDate);

        //dictionary to hold Ibars for each symbol
        Dictionary<IAsset, List<IBar>> assetAndBars = new();

        //List to hold IBar
        List<IBar> bars = new();

        //get a historical Ibars of stock symbols for assetCount at a time
        for (int i = 0; i < assets.Where(x => x.Class == AssetClass.UsEquity).Count(); i += maxAssetsAtOneTime)
        {
            var assetSubset = assets.Where(x => x.Class == AssetClass.UsEquity).Skip(i).Take(maxAssetsAtOneTime);
            var historicalBarsRequest = new HistoricalBarsRequest(assetSubset.Select(x => x.Symbol), fromDate, toDate, barTimeFrame);
            await foreach (var bar in alpacaDataClient.GetHistoricalBarsAsAsyncEnumerable(historicalBarsRequest, token))
            {
                bars.Add(bar);
            }
        }
        //get a historical Ibars of crypto symbols for assetCount at a time
        for (int i = 0; i < assets.Where(x => x.Class == AssetClass.Crypto).Count(); i += maxAssetsAtOneTime)
        {
            var assetSubset = assets.Where(x => x.Class == AssetClass.Crypto).Skip(i).Take(maxAssetsAtOneTime);
            var historicalBarsRequest = new HistoricalCryptoBarsRequest(assetSubset.Select(x => x.Symbol), fromDate, toDate, barTimeFrame);
            await foreach (var bar in alpacaCryptoDataClient.GetHistoricalBarsAsAsyncEnumerable(historicalBarsRequest, token))
            {
                bars.Add(bar);
            }
        }
        assetAndBars = bars.GroupBy(x => x.Symbol).ToDictionary(g => assets.Where(a => a.Symbol == g.Key).Select(a => a).First(), g => g.ToList());
        return assetAndBars;
    }

    /// <summary>
    /// Calculates the from DateTime based on current Date and BartTimeFrame
    /// </summary>
    /// <param name="barTimeFrame"></param>
    /// <param name="toDate"></param>
    /// <returns></returns>
    public static DateTime GetTimeIntervalFrom(BarTimeFrame barTimeFrame, int noOfBars, DateTime toDate)
    {
        DateTime fromDate = toDate;
        switch (barTimeFrame.Unit)
        {
            case BarTimeFrameUnit.Minute:
                fromDate = toDate.AddMinutes(-barTimeFrame.Value * noOfBars);
                break;
            case BarTimeFrameUnit.Hour:
                fromDate = toDate.AddHours(-barTimeFrame.Value * noOfBars);
                break;
            case BarTimeFrameUnit.Day:
                fromDate = toDate.AddDays(-barTimeFrame.Value * noOfBars);
                break;
            case BarTimeFrameUnit.Week:
                fromDate = toDate.AddDays(-barTimeFrame.Value * 7 * noOfBars);
                break;
            case BarTimeFrameUnit.Month:
                fromDate = toDate.AddMonths(-barTimeFrame.Value * noOfBars);
                break;
        }

        return fromDate;
    }



    /// <summary>
    /// dispose
    /// </summary>
    public void Dispose()
    {
        alpacaTradingClient?.Dispose();
        alpacaDataClient?.Dispose();
        alpacaCryptoDataClient?.Dispose();

        alpacaStreamingClient?.Dispose();
        alpacaDataStreamingClient?.Dispose();
        alpacaCryptoStreamingClient?.Dispose();
    }

    #endregion
}

#region Event Arg classes
public class AccountUpdatedEventArgs : EventArgs
{
    public IAccount? Account { get; set; }
}

public class PositionUpdatedEventArgs : EventArgs
{
    public IReadOnlyCollection<IPosition>? Positions { get; set; }
}
public class ClosedOrderUpdatedEventArgs : EventArgs
{
    public IReadOnlyCollection<IOrder>? ClosedOrders { get; set; }
}
public class OpenOrderUpdatedEventArgs : EventArgs
{
    public IReadOnlyCollection<IOrder>? OpenOrders { get; set; }
}

#endregion

