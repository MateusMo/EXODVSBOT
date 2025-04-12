using Binance.Net.Enums;
using CryptoExchange.Net.CommonObjects;
using ExodvsBot.Domain.Dto;
using ExodvsBot.Domain.Enums;
using ExodvsBot.Repository.Files;
using ExodvsBot.Services.Binance;
using ExodvsBot.Services.Calculos;
using ExodvsBot.Services.Telegram;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace ExodvsBot.Runner
{
    public class Runner
    {
        // Tamanho máximo das listas
        private const int MaxLogs = 1000;
        private const int MaxOcorrencias = 1000;

        public static List<string> Logs { get; } = new List<string>();
        public static List<OcorrenciaDto> Ocorrencias { get; } = new List<OcorrenciaDto>();

        public static async Task RunAsyncn(
            CancellationToken cancellationToken,
            StartSettingsDto settings,
            Action<Exception> errorHandler = null)
        {
            FileManagement.CreateFile();
            FileManagement.UpdateFileWithTranslatedWords();
            var binance = new BinanceRequests(settings.txtApiKey, settings.txtApiSecret);
            var buySell = new BuySell(settings.txtApiKey, settings.txtApiSecret);
            var calculos = new Calculos();
            var ocorrencias = await FileManagement.ReadFile();
            Ocorrencias.AddRange(ocorrencias);
            Logs.Add("😎 Trying to connect to your account...");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {

                        // Espera o período para rodar
                        await definePausa(settings.numBuyRSI, settings.numSellRSI, calculos.CalcularRSI(await binance.GetHistoricalPrices("BTCUSDT", (KlineInterval)settings.cmbKlineInterval, await calculos.DefinirQuantidadeDeCandles(settings.cmbKlineInterval, 14)), 14));
                        
                        bool hasInternetConnection = await CheckInternetConnectionAsync();
                        bool IsApiKeyValid = await binance.TestKey("BTCUSDT");
                        if (hasInternetConnection && IsApiKeyValid)
                        {
                            Run(binance, settings, calculos, buySell);
                        }
                        if (!hasInternetConnection)
                        {
                            Logs.Add($"----------------------------------------");
                            Logs.Add("💀 No internet connection...");

                            // Limpa as listas se excederem o tamanho máximo
                            ClearMemory(Logs, MaxLogs);
                            ClearMemory(Ocorrencias, MaxOcorrencias);
                        }
                        if (!IsApiKeyValid)
                        {
                            Logs.Add($"----------------------------------------");
                            Logs.Add("💀 And the ip configured on binance...");
                            Logs.Add("💀 Check your computer ip...");
                            Logs.Add("💀 Check if you wrote correctly the keys...");
                            Logs.Add("💀 Your api key is not valid...");
                        }

                    }
                    catch (OperationCanceledException)
                    {
                        // O bot foi cancelado
                        break;
                    }
                    catch (Exception ex)
                    {
                        errorHandler?.Invoke(ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation normal
            }
        }



        private static async void Run(BinanceRequests binance,
            StartSettingsDto settings,
            Calculos calculos,
            BuySell buySell)
        {
            try
            {
                if (settings.cmbKlineInterval == KlineIntervalEnum.Automatic)
                {
                    settings.cmbKlineInterval = await AutomaticKline(binance, calculos);
                }

                // Busca preço atual do bitcoin
                decimal bitcoinPrice = await binance.GetAssetPrice();
                // Busca 10 últimos preços por minuto
                var PrecosCurtoPrazo = await binance.GetHistoricalPrices("BTCUSDT", (KlineInterval)settings.cmbKlineInterval, 20);
                // Busca 50 últimos preços por minuto
                var PrecosLongoPrazo = await binance.GetHistoricalPrices("BTCUSDT", (KlineInterval)settings.cmbKlineInterval, 50);
                //define quantidade de candles
                int quantidadeCandles = await calculos.DefinirQuantidadeDeCandles(settings.cmbKlineInterval, 14);
                // Busca 14 últimos preços por minuto para RSI
                var precosParaRSI = await binance.GetHistoricalPrices("BTCUSDT", (KlineInterval)settings.cmbKlineInterval, quantidadeCandles);
                // Busca 20 últimos preços para bandas de Bollinger
                var precosParaBandas = await binance.GetHistoricalPrices("BTCUSDT", (KlineInterval)settings.cmbKlineInterval, 20);
                // Busca o volume dos últimos 50 minutos
                var volumeList = await binance.GetVolumeData("BTCUSDT", (KlineInterval)settings.cmbKlineInterval, 50);
                // Inicia cálculos
                decimal rsi = calculos.CalcularRSI(precosParaRSI, 14);
                // Aqui você pode adicionar a lógica de compra/venda com base nos cálculos
                var decisao = await Decisao.TomarDecisao(bitcoinPrice, rsi, settings.numBuyRSI, settings.numSellRSI, settings.cmbStoploss, settings.cmbTakeProfit);
                //operação
                var ocorrencia = await buySell.IniciarOperacao(decisao);

                if (ocorrencia.Executou)
                {
                    ocorrencia.Data = DateTime.Now;
                    ocorrencia.PrecoBitcoin = bitcoinPrice;
                    ocorrencia.Decisao = decisao;
                    FileManagement.Write(ocorrencia);
                    Ocorrencias.Add(ocorrencia);
                }

                var decision = string.Empty;
                if (decisao == "Keep")
                {
                    decision = "Keep Position";
                }
                else if (decisao == "Sell" && ocorrencia.Executou)
                {
                    if (settings.txtTelegramChatId.Any())
                    {
                        await TelegramSender.SendMessage(settings.txtTelegramToken, settings.txtTelegramChatId, $"Selling 🤑. UsdtBallance {ocorrencia.SaldoUsdt.ToString("0.00")}");
                    }

                    decision = "Sell Position";
                }
                else if (decisao == "Buy" && ocorrencia.Executou)
                {
                    if (settings.txtTelegramChatId.Any())
                    {
                        await TelegramSender.SendMessage(settings.txtTelegramToken, settings.txtTelegramChatId, $"Buying 🤑. UsdtBallance {ocorrencia.SaldoUsdt.ToString("0.00")}");
                    }

                    decision = "Buy Position";
                }

                Logs.Add($"----------------------------------------");
                Logs.Add("🤖 Analysing... " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                Logs.Add($"🤑 BTC Price: {bitcoinPrice.ToString("0.00")}");
                Logs.Add($"🤔 Decision: {decision}");
                Logs.Add($"📊 RSI: {rsi.ToString("0.00")}");
                Logs.Add($"💰 UsdBalance: {ocorrencia.SaldoUsdt.ToString("0.00")}");

                // Limpa as listas se excederem o tamanho máximo
                ClearMemory(Logs, MaxLogs);
                ClearMemory(Ocorrencias, MaxOcorrencias);

            }
            catch (Exception)
            {

                throw;
            }
        }
        private static async Task definePausa(int buyRsi, int sellRsi, decimal rsi)
        {
            if (
                ((rsi - buyRsi) < 1 || (rsi - buyRsi) < -1)
                ||
                ((sellRsi - rsi < 1) || (sellRsi - rsi < -1))
                )
            {
                Logs.Add($"🤑 Rsi is close - Speed mode");
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            else
            {
                Logs.Add($"😎 Rsi is far - Slow mode");
                await Task.Delay(TimeSpan.FromSeconds(60));
            }
        }

        private static async Task<KlineIntervalEnum> AutomaticKline(
            BinanceRequests binance,
            Calculos calculos
            )
        {
            var dadosPrecos = await binance.GetHistoricalPrices("BTCUSDT", KlineInterval.OneHour, 168); // 1 semana de dados (7 dias * 24 horas)
            var dadosVolume = await binance.GetVolumeData("BTCUSDT", KlineInterval.OneHour, 168);
            var intervaloRecomendado = calculos.CalcularMelhorIntervalo(dadosPrecos, dadosVolume);

            return intervaloRecomendado;
        }

        // Método para limpar a memória das listas
        private static void ClearMemory<T>(List<T> list, int maxSize)
        {
            if (list.Count > maxSize)
            {
                list.RemoveRange(0, list.Count - maxSize);
            }
        }

        public static async Task<bool> CheckInternetConnectionAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // Tenta fazer uma requisição a um servidor confiável
                    HttpResponseMessage response = await client.GetAsync("http://www.google.com");
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                // Se ocorrer uma exceção, assume-se que não há conexão com a internet
                return false;
            }
        }
    }
}