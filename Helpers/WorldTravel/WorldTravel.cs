﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using Buddy.Coroutines;
using Clio.Utilities;
using ff14bot;
using ff14bot.Behavior;
using ff14bot.Managers;
using ff14bot.Navigation;
using ff14bot.Objects;
using ff14bot.RemoteWindows;
using LlamaLibrary.Enums;
using LlamaLibrary.Extensions;
using LlamaLibrary.Helpers.Housing;
using LlamaLibrary.Helpers.Ping;
using LlamaLibrary.Logging;
using LlamaLibrary.RemoteAgents;
using LlamaLibrary.RemoteWindows;

namespace LlamaLibrary.Helpers.WorldTravel
{
    public static class WorldTravel
    {
        private static readonly LLogger Log = new(nameof(WorldTravel), Colors.Chocolate);

        private const TravelCity DefaultStart = TravelCity.Cheapest;

        private readonly static ushort[] ValidZones = new ushort[] { 129, 130, 132 };

        private static bool InValidZone => ValidZones.Contains(WorldManager.ZoneId);

        public static async Task OpenWorldTravelMenu(TravelCity travelCity = DefaultStart)
        {
            if (WorldTravelSelect.Instance.IsOpen)
            {
                Log.Information("World Travel Already Open");
                return;
            }

            if (!InValidZone)
            {
                Log.Information($"Travel city: {travelCity}");
                travelCity = (TravelCity)WorldManager.ZoneId switch
                {
                    TravelCity.Limsa    => TravelCity.Limsa,
                    TravelCity.Uldah    => TravelCity.Uldah,
                    TravelCity.Gridania => TravelCity.Gridania,
                    _                   => travelCity
                };

                if (travelCity == TravelCity.Cheapest)
                {
                    var cheapest = WorldManager.AvailableLocations.Where(i => ValidZones.Contains(i.ZoneId)).OrderBy(i => i.GilCost);

                    if (cheapest.Any())
                    {
                        travelCity = (TravelCity)cheapest.First().ZoneId;
                        Log.Information($"Cheapest Zone Found: {travelCity}");
                    }
                    else
                    {
                        Log.Error("No valid zones found");
                        return;
                    }
                }

                var ae = 8;
                switch (travelCity)
                {
                    case TravelCity.Limsa:
                        ae = 8;
                        break;
                    case TravelCity.Uldah:
                        ae = 9;
                        break;
                    case TravelCity.Gridania:
                        ae = 2;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(travelCity), travelCity, null);
                }

                Log.Information($"Traveling to {travelCity}. Calling Teleport {ae}");
                var result = await CommonTasks.Teleport((uint)ae);
                Log.Information($"Result from teleport: {result}");
                if (!result)
                {
                    Log.Error("Unable to teleport");
                    return;
                }
            }

            if (!InValidZone)
            {
                Log.Error("Not in a valid zone");
                return;
            }

            AgentWorldTravelSelect.Instance.Toggle();

            await Coroutine.Wait(5000, () => WorldTravelSelect.Instance.IsOpen && AgentWorldTravelSelect.Instance.ChoicesPointer != IntPtr.Zero);
            await Coroutine.Sleep(500);
        }

        public static async Task<bool> GetTo(WorldLocation worldLocation, TravelCity travelCity = TravelCity.Uldah)
        {
            if (WorldHelper.CurrentWorldId != (int)worldLocation.World && !await GoToWorld(worldLocation.World, travelCity))
            {
                return false;
            }

            if (WorldHelper.CurrentWorldId == (int)worldLocation.World && Navigator.AtLocation(worldLocation.Location.Coordinates))
            {
                return true;
            }

            return await Navigation.GetTo(worldLocation.Location);
        }

        public static async Task<bool> GoToWorld(World world, TravelCity travelCity = DefaultStart)
        {
            return await GoToWorld((ushort)world, travelCity);
        }

        public static async Task<bool> GoToWorld(ushort worldId, TravelCity travelCity = DefaultStart)
        {
            if (!WorldHelper.CheckDC((World)worldId))
            {
                return false;
            }

            if (WorldHelper.CurrentWorldId == worldId)
            {
                return true;
            }

            if (PartyManager.IsInParty && !PartyManager.CrossRealm)
            {
                Log.Information("Getting out of party");
                ChatManager.SendChat("/pcmd leave");
                if (!await Coroutine.Wait(5000, () => SelectYesno.IsOpen))
                {
                    Log.Error("Could not leave party...RIP");
                    return false;
                }

                SelectYesno.Yes();

                if (!await Coroutine.Wait(5000, () => PartyManager.IsInParty && !PartyManager.CrossRealm))
                {
                    Log.Error("Could not leave party...RIP");
                    return false;
                }
            }

            Core.Me.SetRun();

            if (travelCity == TravelCity.Cheapest)
            {
            }

            await OpenWorldTravelMenu(travelCity);

            if (WorldTravelSelect.Instance.IsOpen)
            {
                var Choices = AgentWorldTravelSelect.Instance.Choices;

                if (AgentWorldTravelSelect.Instance.CurrentWorld != worldId)
                {
                    for (var i = 0; i < Choices.Length; i++)
                    {
                        if (Choices[i].WorldID != worldId)
                        {
                            continue;
                        }

                        Log.Information($"Going to: {((World)Choices[i].WorldID).WorldName()}");
                        WorldTravelSelect.Instance.SelectWorld(i);
                        await Coroutine.Wait(5000, () => SelectYesno.IsOpen);
                        if (SelectYesno.IsOpen)
                        {
                            SelectYesno.Yes();
                            await Coroutine.Wait(1_200_000, () => WorldTravelFinderReady.Instance.IsOpen);
                            if (WorldTravelFinderReady.Instance.IsOpen)
                            {
                                await Coroutine.Wait(-1, () => !WorldTravelFinderReady.Instance.IsOpen);
                                await Coroutine.Sleep(2000);
                                if (CommonBehaviors.IsLoading)
                                {
                                    await Coroutine.Wait(-1, () => !CommonBehaviors.IsLoading);
                                }

                                await Coroutine.Sleep(2000);
                                //Log.Information("Waiting for ping to update");
                                await PingChecker.UpdatePing();
                                Log.Information($"CurrentWorld: {WorldHelper.CurrentWorld.WorldName()} Ping: {PingChecker.CurrentPing}");
                            }
                        }

                        break;
                    }
                }

                if (WorldTravelSelect.Instance.IsOpen)
                {
                    WorldTravelSelect.Instance.Close();
                    await Coroutine.Sleep(500);
                }
            }

            if (WorldHelper.IsOnHomeWorld)
            {
                HousingHelper.UpdateResidenceArray();
            }

            await Coroutine.Sleep(500);
            return WorldHelper.CurrentWorldId == worldId;
        }

        public static async Task<bool> MakeSureHome(TravelCity travelCity = DefaultStart)
        {
            if (WorldHelper.CurrentWorldId == WorldHelper.HomeWorldId)
            {
                return true;
            }

            return await GoToWorld(WorldHelper.HomeWorldId, travelCity);
        }

        public static async Task<GameObject?> GetAE(TravelCity travelCity = DefaultStart)
        {
            uint id = 0;
            id = travelCity switch
            {
                TravelCity.Limsa    => 8,
                TravelCity.Uldah    => 9,
                TravelCity.Gridania => 2,
                _                   => throw new ArgumentOutOfRangeException(nameof(travelCity), travelCity, null),
            };
            return await Navigation.GetToAE(id);
        }

        public static bool SelectWorldVisit()
        {
            var test = 0;
            foreach (var line in Conversation.GetConversationList)
            {
                if (line.Contains(Translator.VisitAnotherWorldServer))
                {
                    break;
                }

                test++;
            }

            if (test != Conversation.GetConversationList.Count)
            {
                Conversation.SelectLine((uint)test);
                return true;
            }

            return false;
        }
    }
}