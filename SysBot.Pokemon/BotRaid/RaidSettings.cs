﻿using PKHeX.Core;
using System.ComponentModel;

namespace SysBot.Pokemon
{
    public class RaidSettings
    {
        private const string Hosting = nameof(Hosting);
        public override string ToString() => "Raid Bot Settings";

        [Category(Hosting), Description("Minimum amount of seconds to wait before starting a raid. Ranges from 0 to 180 seconds.")]
        public int MinTimeToWait { get; set; } = 90;

        [Category(Hosting), Description("Minimum Link Code to host the raid with. Set this to -1 to host with no code.")]
        public int MinRaidCode { get; set; } = 8180;

        [Category(Hosting), Description("Maximum Link Code to host the raid with. Set this to -1 to host with no code.")]
        public int MaxRaidCode { get; set; } = 8199;

        [Category(Hosting), Description("Optional description of the raid the bot is hosting. Uses automatic Pokémon detection if left blank.")]
        public string RaidDescription { get; set; } = string.Empty;

        [Category(Hosting), Description("Echoes each party member as they lock into a Pokémon.")]
        public bool EchoPartyReady { get; set; } = false;

        [Category(Hosting), Description("Echoes when we invite others and when we start the raid.")]
        public bool EchoRaidNotifications { get; set; } = false;

        [Category(Hosting), Description("Allows the bot to echo your Friend Code if set.")]
        public string FriendCode { get; set; } = string.Empty;

        [Category(Hosting), Description("Number of friend requests to accept each time.")]
        public int NumberFriendsToAdd { get; set; } = 0;

        [Category(Hosting), Description("Number of friends to delete each time.")]
        public int NumberFriendsToDelete { get; set; } = 0;

        [Category(Hosting), Description("Number of raids to host before trying to add/remove friends. Setting a value of 1 will tell the bot to host one raid, then start adding/removing friends.")]
        public int InitialRaidsToHost { get; set; } = 0;

        [Category(Hosting), Description("Number of raids to host between trying to add friends.")]
        public int RaidsBetweenAddFriends { get; set; } = 0;

        [Category(Hosting), Description("Number of raids to host between trying to delete friends.")]
        public int RaidsBetweenDeleteFriends { get; set; } = 0;

        [Category(Hosting), Description("Number of row to start trying to add friends.")]
        public int RowStartAddingFriends { get; set; } = 1;

        [Category(Hosting), Description("Number of row to start trying to delete friends.")]
        public int RowStartDeletingFriends { get; set; } = 1;

        [Category(Hosting), Description("The Nintendo Switch profile you are using to manage friends. For example, set this to 2 if you are using the second profile.")]
        public int ProfileNumber { get; set; } = 1;

        [Category(Hosting), Description("When set, the bot will create a text file with current Raid Code for OBS.")]
        public bool RaidLog { get; set; } = false;

        [Category(Hosting), Description("If using USB-Botbase, quit out the raid by toggling airplane mode. For regular hosting and soft-lock AutoRoll.")]
        public bool AirplaneQuitout { get; set; } = false;

        [Category(Hosting), Description("When set, the bot will roll species and set date to 2000, resetting it once it reaches 2060.")]
        public bool AutoRoll { get; set; } = false;

        [Category(Hosting), Description("If AutoRoll enabled, specify Pokémon species to stop rolling on and to soft-lock host via airplane mode.")]
        public Species AutoRollSpecies { get; set; } = Species.None;

        /// <summary>
        /// Gets a random trade code based on the range settings.
        /// </summary>
        public int GetRandomRaidCode() => Util.Rand.Next(MinRaidCode, MaxRaidCode + 1);
    }
}