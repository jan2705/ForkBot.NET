﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Net;
using PKHeX.Core;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public static class QueueExtensions
    {
        private const uint MaxTradeCode = 99999999;

        public static async Task AddToQueueAsync(this SocketCommandContext Context, int code, string trainer, RequestSignificance sig, PK8 trade, PokeRoutineType routine, PokeTradeType type, SocketUser trader)
        {
            if ((uint)code > MaxTradeCode)
            {
                await Context.Channel.SendMessageAsync("Trade code should be 00000000-99999999!").ConfigureAwait(false);
                return;
            }

            IUserMessage test;
            try
            {
                const string helper = "I've added you to the queue! I'll message you here when your trade is starting.";
                test = await trader.SendMessageAsync(helper).ConfigureAwait(false);
            }
            catch (HttpException ex)
            {
                await Context.Channel.SendMessageAsync($"{ex.HttpCode}: {ex.Reason}!").ConfigureAwait(false);
                var noAccessMsg = Context.User == trader ? "You must enable private messages in order to be queued!" : "The mentioned user must enable private messages in order for them to be queued!";
                await Context.Channel.SendMessageAsync(noAccessMsg).ConfigureAwait(false);
                return;
            }

            // Try adding
            var result = Context.AddToTradeQueue(trade, code, trainer, sig, routine, type, trader, out var msg);

            // Notify in channel
            await Context.Channel.SendMessageAsync(msg).ConfigureAwait(false);
            // Notify in PM to mirror what is said in the channel.
            await trader.SendMessageAsync(msg).ConfigureAwait(false);

            // Clean Up
            if (result)
            {
                // Delete the user's join message for privacy
                if (!Context.IsPrivate)
                    await Context.Message.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
            }
            else
            {
                // Delete our "I'm adding you!", and send the same message that we sent to the general channel.
                await test.DeleteAsync().ConfigureAwait(false);
            }
        }

        public static async Task AddToQueueAsync(this SocketCommandContext Context, int code, string trainer, RequestSignificance sig, PK8 trade, PokeRoutineType routine, PokeTradeType type)
        {
            await AddToQueueAsync(Context, code, trainer, sig, trade, routine, type, Context.User).ConfigureAwait(false);
        }

        private static bool AddToTradeQueue(this SocketCommandContext Context, PK8 pk8, int code, string trainerName, RequestSignificance sig, PokeRoutineType type, PokeTradeType t, SocketUser trader, out string msg)
        {
            var user = trader;
            var userID = user.Id;
            var name = user.Username;

            var trainer = new PokeTradeTrainerInfo(trainerName);
            var notifier = new DiscordTradeNotifier<PK8>(pk8, trainer, code, user, Context);
            var detail = new PokeTradeDetail<PK8>(pk8, trainer, notifier, t, code: code, sig == RequestSignificance.Favored);
            var trade = new TradeEntry<PK8>(detail, userID, type, name);

            var hub = SysCordInstance.Self.Hub;
            var Info = hub.Queues.Info;
            var added = Info.AddToTradeQueue(trade, userID, sig == RequestSignificance.Sudo);

            if (added == QueueResultAdd.AlreadyInQueue)
            {
                msg = "Sorry, you are already in the queue.";
                return false;
            }

            var position = Info.CheckPosition(userID, type);

            var ticketID = "";
            if (TradeStartModule.IsStartChannel(Context.Channel.Id))
                ticketID = $", unique ID: {detail.ID}";

            var pokeName = "";
            if (t == PokeTradeType.Specific || t == PokeTradeType.TradeCord && pk8.Species != 0)
                pokeName = $" Receiving: {(hub.Config.Trade.ItemMuleSpecies == (Species)pk8.Species && pk8.HeldItem != 0 ? $"{(Species)pk8.Species + " (" + ShowdownSet.GetShowdownText(pk8).Split('@','\n')[1].Trim() + ")"}" : $"{(Species)pk8.Species}")}.";
            msg = $"{user.Mention} - Added to the {type} queue{ticketID}. Current Position: {position.Position}.{pokeName}";

            var botct = Info.Hub.Bots.Count;
            if (position.Position > botct)
            {
                var eta = Info.Hub.Config.Queues.EstimateDelay(position.Position, botct);
                msg += $" Estimated: {eta:F1} minutes.";
            }
            return true;
        }
    }
}