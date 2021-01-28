﻿using PKHeX.Core;
using System;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Base.SwitchStick;
using static SysBot.Pokemon.PokeDataOffsets;

namespace SysBot.Pokemon
{
    public class EncounterBot : PokeRoutineExecutor
    {
        private readonly PokeTradeHub<PK8> Hub;
        private readonly BotCompleteCounts Counts;
        private readonly IDumper DumpSetting;
        private readonly int[] DesiredIVs;
        private readonly byte[] BattleMenuReady = { 0, 0, 0, 255 };

        public EncounterBot(PokeBotState cfg, PokeTradeHub<PK8> hub) : base(cfg)
        {
            Hub = hub;
            Counts = Hub.Counts;
            DumpSetting = Hub.Config.Folder;
            DesiredIVs = StopConditionSettings.InitializeTargetIVs(Hub);
        }

        private int encounterCount;
        private int catchCount;
        private byte[] pouchData = { 0 };

        public override async Task MainLoop(CancellationToken token)
        {
            Log("Identifying trainer data of the host console.");
            await IdentifyTrainer(token).ConfigureAwait(false);

            Log("Starting main EncounterBot loop.");
            Config.IterateNextRoutine();

            // Clear out any residual stick weirdness.
            await ResetStick(token).ConfigureAwait(false);

            if (Hub.Config.StopConditions.CatchEncounter)
                await CheckPokeBallCount(token).ConfigureAwait(false);

            var task = Hub.Config.Encounter.EncounteringType switch
            {
                EncounterMode.VerticalLine => WalkInLine(token),
                EncounterMode.HorizontalLine => WalkInLine(token),
                EncounterMode.Eternatus => DoEternatusEncounter(token),
                EncounterMode.LegendaryDogs => DoDogEncounter(token),
                EncounterMode.SoftReset => DoSoftResetEncounter(token),
                EncounterMode.Regigigas => DoSoftResetEncounter(token),
                EncounterMode.SoJCamp => DoSoJCampEncounter(token),
                _ => WalkInLine(token),
            };
            await task.ConfigureAwait(false);

            await ResetStick(token).ConfigureAwait(false);
            await DetachController(token).ConfigureAwait(false);
        }

        private async Task WalkInLine(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var attempts = await StepUntilEncounter(token).ConfigureAwait(false);
                if (attempts < 0) // aborted
                    continue;

                Log($"Encounter found after {attempts} attempts! Checking details...");

                // Reset stick while we wait for the encounter to load.
                await ResetStick(token).ConfigureAwait(false);

                var pk = await ReadUntilPresent(WildPokemonOffset, 2_000, 0_200, token).ConfigureAwait(false);
                if (pk == null)
                {
                    Log("Invalid data detected. Restarting loop.");

                    // Flee and continue looping.
                    while (await IsInBattle(token).ConfigureAwait(false))
                        await FleeToOverworld(token).ConfigureAwait(false);
                    continue;
                }

                // Offsets are flickery so make sure we see it 3 times.
                for (int i = 0; i < 3; i++)
                    await ReadUntilChanged(BattleMenuOffset, BattleMenuReady, 5_000, 0_100, true, token).ConfigureAwait(false);

                if (await HandleEncounter(pk, false, token).ConfigureAwait(false))
                    return;

                Log("Running away...");
                while (await IsInBattle(token).ConfigureAwait(false))
                    await FleeToOverworld(token).ConfigureAwait(false);
            }
        }

        private async Task DoEternatusEncounter(CancellationToken token)
        {
            while (!token.IsCancellationRequested && Config.NextRoutineType == PokeRoutineType.EncounterBot)
            {
                await SetStick(LEFT, 0, 20_000, 1_000, token).ConfigureAwait(false);
                await ResetStick(token).ConfigureAwait(false);

                var pk = await ReadUntilPresent(RaidPokemonOffset, 2_000, 0_200, token).ConfigureAwait(false);
                if (pk != null)
                {
                    if (await HandleEncounter(pk, true, token).ConfigureAwait(false))
                        return;
                }

                Connection.Log("Resetting Eternatus by restarting the game");
                await CloseGame(Hub.Config, token).ConfigureAwait(false);
                await StartGame(Hub.Config, token).ConfigureAwait(false);
            }
        }

        private async Task DoDogEncounter(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Log("Looking for a new dog...");

                // At the start of each loop, an A press is needed to exit out of a prompt.
                await Click(A, 0_200, token).ConfigureAwait(false);
                await SetStick(LEFT, 0, 30000, 1_000, token).ConfigureAwait(false);

                // Encounters Zacian/Zamazenta and clicks through all the menus.
                while (!await IsInBattle(token).ConfigureAwait(false))
                    await Click(A, 0_300, token).ConfigureAwait(false);

                Log("Encounter started! Checking details...");
                var pk = await ReadUntilPresent(LegendaryPokemonOffset, 2_000, 0_200, token).ConfigureAwait(false);
                if (pk == null)
                {
                    Log("Invalid data detected. Restarting loop.");
                    continue;
                }

                // Get rid of any stick stuff left over so we can flee properly.
                await ResetStick(token).ConfigureAwait(false);

                // Wait for the entire cutscene.
                await Task.Delay(15_000, token).ConfigureAwait(false);

                // Offsets are flickery so make sure we see it 3 times.
                for (int i = 0; i < 3; i++)
                    await ReadUntilChanged(BattleMenuOffset, BattleMenuReady, 5_000, 0_100, true, token).ConfigureAwait(false);

                if (await HandleEncounter(pk, true, token).ConfigureAwait(false))
                    return;

                Log("Running away...");
                while (await IsInBattle(token).ConfigureAwait(false))
                    await FleeToOverworld(token).ConfigureAwait(false);

                // Extra delay to be sure we're fully out of the battle.
                await Task.Delay(0_250, token).ConfigureAwait(false);
            }
        }

        private async Task<int> StepUntilEncounter(CancellationToken token)
        {
            Log("Walking around until an encounter...");
            int attempts = 0;
            while (!token.IsCancellationRequested && Config.NextRoutineType == PokeRoutineType.EncounterBot)
            {
                if (!await IsInBattle(token).ConfigureAwait(false))
                {
                    switch (Hub.Config.Encounter.EncounteringType)
                    {
                        case EncounterMode.VerticalLine:
                            await SetStick(LEFT, 0, -30000, 2_400, token).ConfigureAwait(false);
                            await SetStick(LEFT, 0, 0, 0_100, token).ConfigureAwait(false); // reset

                            // Quit early if we found an encounter on first sweep.
                            if (await IsInBattle(token).ConfigureAwait(false))
                                break;

                            await SetStick(LEFT, 0, 30000, 2_400, token).ConfigureAwait(false);
                            await SetStick(LEFT, 0, 0, 0_100, token).ConfigureAwait(false); // reset
                            break;
                        case EncounterMode.HorizontalLine:
                            await SetStick(LEFT, -30000, 0, 2_400, token).ConfigureAwait(false);
                            await SetStick(LEFT, 0, 0, 0_100, token).ConfigureAwait(false); // reset

                            // Quit early if we found an encounter on first sweep.
                            if (await IsInBattle(token).ConfigureAwait(false))
                                break;

                            await SetStick(LEFT, 30000, 0, 2_400, token).ConfigureAwait(false);
                            await SetStick(LEFT, 0, 0, 0_100, token).ConfigureAwait(false); // reset
                            break;
                    }

                    attempts++;
                    if (attempts % 10 == 0)
                        Log($"Tried {attempts} times, still no encounters.");
                }

                if (await IsInBattle(token).ConfigureAwait(false))
                    return attempts;
            }

            return -1; // aborted
        }

        private async Task<bool> HandleEncounter(PK8 pk, bool legends, CancellationToken token)
        {
            encounterCount++;
            Log($"Encounter: {encounterCount}{Environment.NewLine}{ShowdownSet.GetShowdownText(pk)}{Environment.NewLine}{(StopConditionSettings.HasMark(pk, out RibbonIndex mark) ? $"Mark: {mark.ToString().Replace("Mark", "")}{Environment.NewLine}" : "")}");
            TradeExtensions.EncounterLogs(pk);
            if (legends)
                Counts.AddCompletedLegends();
            else
                Counts.AddCompletedEncounters();

            if (DumpSetting.Dump && !string.IsNullOrEmpty(DumpSetting.DumpFolder))
                DumpPokemon(DumpSetting.DumpFolder, legends ? "legends" : "encounters", pk);

            if (StopConditionSettings.EncounterFound(pk, DesiredIVs, Hub.Config.StopConditions))
            {
                Log(Hub.Config.StopConditions.CatchEncounter && (Hub.Config.Encounter.EncounteringType == EncounterMode.VerticalLine || Hub.Config.Encounter.EncounteringType == EncounterMode.HorizontalLine) ?
                    "Result found! Attempting to catch..." : $"{(!Hub.Config.StopConditions.PingOnMatch.Equals(string.Empty) ? $"<@{Hub.Config.StopConditions.PingOnMatch}>\n" : "")}Result found! Stopping routine execution; restart the bot(s) to search again.");
                if (Hub.Config.StopConditions.CaptureVideoClip)
                {
                    await Task.Delay(Hub.Config.StopConditions.ExtraTimeWaitCaptureVideo, token).ConfigureAwait(false);
                    await PressAndHold(CAPTURE, 2_000, 1_000, token).ConfigureAwait(false);
                }

                if (Hub.Config.StopConditions.CatchEncounter && (Hub.Config.Encounter.EncounteringType == EncounterMode.VerticalLine || Hub.Config.Encounter.EncounteringType == EncounterMode.HorizontalLine))
                    await CatchWildPokemon(pk, token).ConfigureAwait(false);

                return true;
            }

            return false;
        }

        private async Task ResetStick(CancellationToken token)
        {
            // If aborting the sequence, we might have the stick set at some position. Clear it just in case.
            await SetStick(LEFT, 0, 0, 0_500, token).ConfigureAwait(false); // reset
        }

        private async Task FleeToOverworld(CancellationToken token)
        {
            // This routine will always escape a battle.
            await Click(DUP, 0_400, token).ConfigureAwait(false);
            await Click(A, 0_400, token).ConfigureAwait(false);
            await Click(B, 0_400, token).ConfigureAwait(false);
            await Click(B, 0_400, token).ConfigureAwait(false);
        }

        private async Task CatchWildPokemon(PK8 pk, CancellationToken token)
        {
            var check = await ReadPokemon(WildPokemonOffset, token).ConfigureAwait(false);
            if (encounterCount != 0 && encounterCount % catchCount == 0)
            {
                Log($"Ran out of Master Balls to catch {SpeciesName.GetSpeciesName(pk.Species, 2)}.");
                if (Hub.Config.StopConditions.InjectPokeBalls)
                {
                    Log("Restoring original pouch data.");
                    await Connection.WriteBytesAsync(pouchData, PokeBallOffset, token).ConfigureAwait(false);
                    await Task.Delay(0_500, token).ConfigureAwait(false);
                }
                else
                {
                    Log($"{(!Hub.Config.StopConditions.PingOnMatch.Equals(string.Empty) ? $"<@{Hub.Config.StopConditions.PingOnMatch}>\n" : "")}Result found! Stopping routine execution; restart the bot(s) to search again.");
                    return;
                }
            }

            await SetLastUsedBall(Ball.Master, token).ConfigureAwait(false);
            await Click(B, 1_000, token).ConfigureAwait(false);
            await Click(X, 1_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false); // Throw ball

            await Click(B, 1_000, token).ConfigureAwait(false);
            await Click(B, 1_000, token).ConfigureAwait(false); // Just in case we didn't

            await Click(X, 1_000, token).ConfigureAwait(false);
            await Click(A, 1_000, token).ConfigureAwait(false); // Attempt again to be sure
            while (!await IsOnOverworld(Hub.Config, token).ConfigureAwait(false) && check.Species != 0)
                await Click(B, 0_400, token).ConfigureAwait(false);

            if (await IsOnOverworld(Hub.Config, token).ConfigureAwait(false) && !await IsInBattle(token).ConfigureAwait(false))
            {
                Log($"{(!Hub.Config.StopConditions.PingOnMatch.Equals(string.Empty) ? $"<@{Hub.Config.StopConditions.PingOnMatch}>\n" : "")}Caught {SpeciesName.GetSpeciesName(pk.Species, 2)}! Resuming routine...");
                await WalkInLine(token).ConfigureAwait(false);
            }
        }

        private async Task CheckPokeBallCount(CancellationToken token)
        {
            if (Hub.Config.StopConditions.CatchEncounter)
            {
                Log("Checking Poké Ball count...");
                pouchData = await Connection.ReadBytesAsync(PokeBallOffset, 116, token).ConfigureAwait(false);
                var counts = EncounterCount.GetBallCounts(pouchData);
                catchCount = counts.PossibleCatches(Ball.Master);

                if (catchCount == 0)
                {
                    Log("Insufficient Master Ball count. Please obtain at least one before starting.");
                    return;
                }
            }
        }

        private async Task DoSoftResetEncounter(CancellationToken token)
        {
            while (!token.IsCancellationRequested && Config.NextRoutineType == PokeRoutineType.EncounterBot)
            {
                while (!await IsInBattle(token).ConfigureAwait(false))
                    await Click(A, 0_500, token).ConfigureAwait(false);

                var pk = await ReadUntilPresent(Hub.Config.Encounter.EncounteringType == EncounterMode.SoftReset ? WildPokemonOffset : RaidPokemonOffset, 2_000, 0_200, token).ConfigureAwait(false);
                if (pk == null)
                {
                    Log("Invalid data detected. Restarting loop.");
                    continue;
                }
                else
                {
                    if (await HandleEncounter(pk, true, token).ConfigureAwait(false))
                        return;
                }

                Log($"Resetting {SpeciesName.GetSpeciesNameGeneration(pk.Species, 2, 8)} by restarting the game");
                await CloseGame(Hub.Config, token).ConfigureAwait(false);
                Log("Restarting the game and mashing A until in battle!");
                await StartGame(Hub.Config, token, true).ConfigureAwait(false);
            }
        }

        private async Task DoSoJCampEncounter(CancellationToken token)
        {
            bool campEntered = false;
            while (!token.IsCancellationRequested && Config.NextRoutineType == PokeRoutineType.EncounterBot)
            {
                await Click(X, 2_000, token).ConfigureAwait(false);
                if (!campEntered)
                {
                    await Click(DDOWN, 0_600, token).ConfigureAwait(false);
                    await Click(DRIGHT, 0_600, token).ConfigureAwait(false);
                    campEntered = true;
                }

                Log("Entering camp...");
                await Click(A, 12_000, token).ConfigureAwait(false);
                await Click(B, 2_000, token).ConfigureAwait(false);
                await Click(A, 0_500, token).ConfigureAwait(false);

                while (!await IsInBattle(token).ConfigureAwait(false))
                    await Task.Delay(2_000).ConfigureAwait(false);

                var pk = await ReadUntilPresent(WildPokemonOffset, 2_000, 0_200, token).ConfigureAwait(false);
                if (pk == null)
                {
                    Log("Invalid data detected. Restarting loop.");
                    continue;
                }
                else
                {
                    if (await HandleEncounter(pk, true, token).ConfigureAwait(false))
                        return;
                }

                Log("Fleeing from battle...");
                while (await IsInBattle(token).ConfigureAwait(false))
                    await FleeToOverworld(token).ConfigureAwait(false);

                while (!await IsOnOverworld(Hub.Config, token).ConfigureAwait(false))
                    await Task.Delay(2_000).ConfigureAwait(false);
            }
        }
    }
}