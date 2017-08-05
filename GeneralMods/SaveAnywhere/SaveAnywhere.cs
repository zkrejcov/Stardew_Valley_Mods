﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Omegasis.SaveAnywhere.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;

namespace Omegasis.SaveAnywhere
{
    /// <summary>The mod entry point.</summary>
    public class SaveAnywhere : Mod
    {
        /*********
        ** Properties
        *********/
        /// <summary>Provides methods for saving and loading game data.</summary>
        private SaveManager SaveManager;

        /// <summary>Provides methods for reading and writing the config file.</summary>
        private ConfigUtilities ConfigUtilities;

        /// <summary>Whether villager schedules should be reset now.</summary>
        private bool ShouldResetSchedules;

        /// <summary>The parsed schedules by NPC name.</summary>
        private readonly IDictionary<string, string> NpcSchedules = new Dictionary<string, string>();


        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            this.ConfigUtilities = new ConfigUtilities(this.Helper.DirectoryPath);

            SaveEvents.AfterLoad += this.SaveEvents_AfterLoad;
            ControlEvents.KeyPressed += this.ControlEvents_KeyPressed;
            GameEvents.UpdateTick += this.GameEvents_UpdateTick;
            TimeEvents.DayOfMonthChanged += this.TimeEvents_DayOfMonthChanged;
        }


        /*********
        ** Private methods
        *********/
        /// <summary>The method invoked after the player loads a save.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void SaveEvents_AfterLoad(object sender, EventArgs e)
        {
            // load config
            this.ConfigUtilities.LoadConfig();
            this.ConfigUtilities.WriteConfig();

            // load positions
            this.SaveManager = new SaveManager(Game1.player, this.Helper.DirectoryPath, this.Monitor, this.Helper.Reflection, onVillagersReset: () => this.ShouldResetSchedules = true);
            this.SaveManager.LoadPositions();
        }

        /// <summary>The method invoked when the game updates (roughly 60 times per second).</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void GameEvents_UpdateTick(object sender, EventArgs e)
        {
            // let save manager run background logic
            if (Context.IsWorldReady)
                this.SaveManager.Update();

            // reset NPC schedules
            if (Context.IsWorldReady && this.ShouldResetSchedules)
            {
                this.ShouldResetSchedules = false;
                this.ApplySchedules();
            }
        }

        /// <summary>The method invoked when <see cref="Game1.dayOfMonth"/> changes.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void TimeEvents_DayOfMonthChanged(object sender, EventArgsIntChanged e)
        {
            // reload NPC schedules
            this.ShouldResetSchedules = true;

            // update NPC schedules
            this.NpcSchedules.Clear();
            foreach (NPC npc in Utility.getAllCharacters())
            {
                if (!this.NpcSchedules.ContainsKey(npc.name))
                    this.NpcSchedules.Add(npc.name, this.ParseSchedule(npc));
            }
        }

        /// <summary>The method invoked when the presses a keyboard button.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void ControlEvents_KeyPressed(object sender, EventArgsKeyPressed e)
        {
            if (Game1.activeClickableMenu != null)
                return;

            if (e.KeyPressed.ToString() == this.ConfigUtilities.KeyBinding)
                this.SaveManager.SaveGameAndPositions();
        }

        /// <summary>Apply the NPC schedules to each NPC.</summary>
        private void ApplySchedules()
        {
            if (Game1.weatherIcon == Game1.weather_festival || Game1.isFestival() || Game1.eventUp)
                return;

            MethodInfo pathfind = typeof(NPC).GetMethod("pathfindToNextScheduleLocation", BindingFlags.NonPublic | BindingFlags.Instance);

            // apply for each NPC
            foreach (NPC npc in Utility.getAllCharacters())
            {
                if (npc.DirectionsToNewLocation != null || npc.isMoving() || npc.Schedule == null || npc.controller != null || npc is Horse)
                    continue;

                // get raw schedule from XNBs
                IDictionary<string, string> rawSchedule = this.GetRawSchedule(npc.name);
                if (rawSchedule == null)
                    continue;

                // get schedule data
                string scheduleData;
                if (!this.NpcSchedules.TryGetValue(npc.name, out scheduleData) || string.IsNullOrEmpty(scheduleData))
                {
                    this.Monitor.Log("THIS IS AWKWARD");
                    continue;
                }

                // get schedule script
                string script;
                if (!rawSchedule.TryGetValue(scheduleData, out script))
                    continue;

                // parse entries
                string[] entries = script.Split('/');
                int index = 0;
                foreach (string _ in entries)
                {
                    string[] fields = entries[index].Split(' ');

                    // handle GOTO command
                    if (fields.Contains("GOTO"))
                    {
                        for (int i = 0; i < fields.Length; i++)
                        {
                            string s = fields[i];
                            if (s == "GOTO")
                            {
                                rawSchedule.TryGetValue(fields[i + 1], out script);
                                string[] newEntries = script.Split('/');
                                fields = newEntries[0].Split(' ');
                            }
                        }
                    }

                    // parse schedule script
                    SchedulePathDescription schedulePathDescription;
                    try
                    {
                        if (Convert.ToInt32(fields[0]) > Game1.timeOfDay) break;
                        string endMap = Convert.ToString(fields[1]);
                        int x = Convert.ToInt32(fields[2]);
                        int y = Convert.ToInt32(fields[3]);
                        int endFacingDir = Convert.ToInt32(fields[4]);
                        schedulePathDescription = (SchedulePathDescription)pathfind.Invoke(npc, new object[] { npc.currentLocation.name, npc.getTileX(), npc.getTileY(), endMap, x, y, endFacingDir, null, null });
                        index++;
                    }
                    catch (Exception ex)
                    {
                        //this.Monitor.Log($"Error pathfinding NPC {npc.name}: {ex}", LogLevel.Error);
                        continue;
                    }

                    npc.DirectionsToNewLocation = schedulePathDescription;
                    npc.controller = new PathFindController(npc.DirectionsToNewLocation.route, npc, Utility.getGameLocationOfCharacter(npc))
                    {
                        finalFacingDirection = npc.DirectionsToNewLocation.facingDirection,
                        endBehaviorFunction = null
                    };
                }
            }
        }

        /// <summary>Get an NPC's raw schedule data from the XNB files.</summary>
        /// <param name="npcName">The NPC name whose schedules to read.</param>
        /// <returns>Returns the NPC schedule if found, else <c>null</c>.</returns>
        private IDictionary<string, string> GetRawSchedule(string npcName)
        {
            try
            {
                return Game1.content.Load<Dictionary<string, string>>($"Characters\\schedules\\{npcName}");
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Load the raw schedule data for an NPC.</summary>
        /// <param name="npc">The NPC whose schedule to read.</param>
        private string ParseSchedule(NPC npc)
        {
            // set flags
            if (npc.name.Equals("Robin") || Game1.player.currentUpgrade != null)
                npc.isInvisible = false;
            if (npc.name.Equals("Willy") && Game1.stats.DaysPlayed < 2u)
                npc.isInvisible = true;
            else if (npc.Schedule != null)
                npc.followSchedule = true;

            // read schedule data
            IDictionary<string, string> schedule = this.GetRawSchedule(npc.name);
            if (schedule == null)
                return "";

            // do stuff
            if (npc.isMarried())
            {
                string dayName = Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth);
                if ((npc.name.Equals("Penny") && (dayName.Equals("Tue") || dayName.Equals("Wed") || dayName.Equals("Fri"))) || (npc.name.Equals("Maru") && (dayName.Equals("Tue") || dayName.Equals("Thu"))) || (npc.name.Equals("Harvey") && (dayName.Equals("Tue") || dayName.Equals("Thu"))))
                {
                    FieldInfo field = typeof(NPC).GetField("nameofTodaysSchedule", BindingFlags.NonPublic | BindingFlags.Instance);
                    field.SetValue(npc, "marriageJob");
                    return "marriageJob";
                }
                if (!Game1.isRaining && schedule.ContainsKey("marriage_" + Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)))
                {
                    FieldInfo field = typeof(NPC).GetField("nameofTodaysSchedule", BindingFlags.NonPublic | BindingFlags.Instance);
                    field.SetValue(npc, "marriage_" + Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth));
                    return "marriage_" + Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth);
                }
                npc.followSchedule = false;
                return null;
            }
            else
            {
                if (schedule.ContainsKey(Game1.currentSeason + "_" + Game1.dayOfMonth))
                    return Game1.currentSeason + "_" + Game1.dayOfMonth;
                int i;
                for (i = (Game1.player.friendships.ContainsKey(npc.name) ? (Game1.player.friendships[npc.name][0] / 250) : -1); i > 0; i--)
                {
                    if (schedule.ContainsKey(Game1.dayOfMonth + "_" + i))
                        return Game1.dayOfMonth + "_" + i;
                }
                if (schedule.ContainsKey(string.Empty + Game1.dayOfMonth))
                    return string.Empty + Game1.dayOfMonth;
                if (npc.name.Equals("Pam") && Game1.player.mailReceived.Contains("ccVault"))
                    return "bus";
                if (Game1.isRaining)
                {
                    if (Game1.random.NextDouble() < 0.5 && schedule.ContainsKey("rain2"))
                        return "rain2";
                    if (schedule.ContainsKey("rain"))
                        return "rain";
                }
                List<string> list = new List<string> { Game1.currentSeason, Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth) };
                i = (Game1.player.friendships.ContainsKey(npc.name) ? (Game1.player.friendships[npc.name][0] / 250) : -1);
                while (i > 0)
                {
                    list.Add(string.Empty + i);
                    if (schedule.ContainsKey(string.Join("_", list)))
                    {
                        return string.Join("_", list);
                    }
                    i--;
                    list.RemoveAt(list.Count - 1);
                }
                if (schedule.ContainsKey(string.Join("_", list)))
                {
                    return string.Join("_", list);
                }
                if (schedule.ContainsKey(Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)))
                {
                    return Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth);
                }
                if (schedule.ContainsKey(Game1.currentSeason))
                {
                    return Game1.currentSeason;
                }
                if (schedule.ContainsKey("spring_" + Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)))
                {
                    return "spring_" + Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth);
                }
                list.RemoveAt(list.Count - 1);
                list.Add("spring");
                i = (Game1.player.friendships.ContainsKey(npc.name) ? (Game1.player.friendships[npc.name][0] / 250) : -1);
                while (i > 0)
                {
                    list.Add(string.Empty + i);
                    if (schedule.ContainsKey(string.Join("_", list)))
                        return string.Join("_", list);
                    i--;
                    list.RemoveAt(list.Count - 1);
                }
                if (schedule.ContainsKey("spring"))
                    return "spring";
                return null;
            }
        }
    }
}
