#!/bin/sh
# Runs the service from the working tree against the timing database on 127.0.0.1:5434, with every sync off: it migrates the
# database, serves gRPC on localhost:5237 and calls no exchange. T-Invest stays enabled because the settings demand one sync of
# instruments and candles; its endpoint is a closed local port and its token a dummy, so it only fails to connect.
set -eu

root=$(cd "$(dirname "$0")/../.." && pwd)
project="$root/backend/src/ProfitFox.MarketData.Presentation"

dotnet build "$project" -c Release --nologo -v quiet

export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__MarketData="Host=localhost;Port=5434;Database=marketdata;Username=marketdata;Password=marketdata"
export Serilog__MinimumLevel__Default=Warning
export TInvestApi__Endpoint=https://127.0.0.1:9
export TInvestApi__Token=dummy-for-local-timing

for sync in InstrumentSync__Bybit CandleSync__Bybit CandleStream__Bybit CandleStream__TInvest BuyVolumeSync__Bybit \
    TickerCollection__Bybit OpenInterestSync__Bybit FundingPaymentSync__Bybit LongShortRatioSync__Bybit LiquidationStream__Bybit; do
    export "${sync}__Enabled=false"
done

cd "$project"
exec dotnet bin/Release/net10.0/ProfitFox.MarketData.Presentation.dll
