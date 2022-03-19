﻿
using Alpaca.Markets;

namespace AlpacaDashboard
{
    public interface IStock
    {
        IAsset Asset { get; set; }
        string? AskExchange { get; set; }
        decimal? AskPrice { get; set; }
        decimal? AskSize { get; set; }
        string? BidExchange { get; set; }
        decimal? BidPrice { get; set; }
        decimal? BidSize { get; set; }
        decimal? Close { get; set; }
        decimal? High { get; set; }
        decimal? Last { get; set; }
        decimal? Low { get; set; }
        decimal? MarketValue { get; set; }
        decimal? Open { get; set; }
        decimal? OpenPositionValue { get; set; }
        decimal? Qty { get; set; }
        bool subscribed { get; set; }
        decimal? Volume { get; set; }
        decimal? Vwap { get; set; }
    }
}