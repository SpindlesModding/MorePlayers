namespace MorePlayers {
	using BepInEx;
	using BepInEx.Configuration;
	using BepInEx.Logging;
	using HarmonyLib;
	using Photon.Pun;
	using Photon.Realtime;
	using Steamworks.Data;
	using Steamworks;
	using UnityEngine;
	using System.Reflection;
	using System;

	[BepInPlugin(modGUID, modName, modVersion)]
	public class Plugin : BaseUnityPlugin {
		public const string modGUID = "spindles.MorePlayersImproved";
		public const string modName = "MorePlayersImproved";
		public const string modVersion = "1.0.2";

		private readonly Harmony harmony = new Harmony(modGUID);

		public static ConfigEntry<int> configMaxPlayers;
		public static ConfigEntry<bool> configPublicLobbySupport;

		public static ManualLogSource mls;

		void Awake() {
			mls = BepInEx.Logging.Logger.CreateLogSource(modGUID);
			mls.LogInfo($"{modGUID} has awoken!");

			configMaxPlayers = Config.Bind
			(
				"General",
				"MaxPlayers",
				10,
				"The max amount of players allowed in a server"
			);

			configPublicLobbySupport = Config.Bind
			(
				"General",
				"PublicLobbySupport",
				true,
				"Toggles if public lobby support is enabled"
			);

			harmony.PatchAll(typeof(TryJoiningRoomPatch));
			harmony.PatchAll(typeof(HostLobbyPatch));
			mls.LogInfo($"{modGUID} loaded main patches!");

			if (typeof(SteamManager).GetRuntimeMethod("UnlockLobby", new Type[] { typeof(bool) }) != null) {
				harmony.PatchAll(typeof(OnConnectedToMasterPatch));
				mls.LogInfo($"{modGUID} enabled PublicLobbySupport!");
			}
			mls.LogInfo($"{modGUID} is ready now!");
		}

		[HarmonyPatch(typeof(NetworkConnect), "TryJoiningRoom")]
		public class TryJoiningRoomPatch {
			static bool Prefix(ref string ___RoomName) {
				string networkPassword = Traverse.Create(DataDirector.instance).Field("networkPassword").GetValue() as string;
				if (string.IsNullOrEmpty(___RoomName)) {
					mls.LogError("RoomName is null or empty, using previous method!");
					return true;
				}

				if (configMaxPlayers.Value == 0) {
					mls.LogError("The MaxPlayers config is null or empty, using previous method!");
					return true;
				}

				Debug.Log("Trying to join room: " + ___RoomName);
				ExitGames.Client.Photon.Hashtable hashtable = new ExitGames.Client.Photon.Hashtable();
				hashtable.Add("PASSWORD", networkPassword);
				PhotonNetwork.LocalPlayer.SetCustomProperties(hashtable);
				RoomOptions roomOptions = new RoomOptions
				{
					MaxPlayers = configMaxPlayers.Value,
					IsVisible = false
				};
				ExitGames.Client.Photon.Hashtable hashtable2 = new ExitGames.Client.Photon.Hashtable();
				hashtable2.Add("PASSWORD", networkPassword);
				roomOptions.CustomRoomProperties = hashtable2;
				PhotonNetwork.JoinOrCreateRoom(___RoomName, roomOptions, TypedLobby.Default);

				return false;
			}
		}

		[HarmonyPatch(typeof(NetworkConnect), "OnConnectedToMaster")]
		public class OnConnectedToMasterPatch {
			static bool Prefix() {
				if (!configPublicLobbySupport.Value) {
					return true;
				}

				Lobby SM_currentLobby = (Lobby)Traverse.Create(SteamManager.instance).Field("currentLobby").GetValue();

				bool GM_connectRandom = (bool)Traverse.Create(GameManager.instance).Field("connectRandom").GetValue();

				string DD_networkPassword = (string)Traverse.Create(DataDirector.instance).Field("networkPassword").GetValue();
				string DD_networkServerName = (string)Traverse.Create(DataDirector.instance).Field("networkServerName").GetValue();
				string DD_networkJoinServerName = (string)Traverse.Create(DataDirector.instance).Field("networkJoinServerName").GetValue();

				if (string.IsNullOrEmpty(DD_networkServerName)) {
					mls.LogError("RoomName is null or empty, using previous method!");
					return true;
				}

				if (configMaxPlayers.Value == 0) {
					mls.LogError("The MaxPlayers config is null or empty, using previous method!");
					return true;
				}

				Debug.Log("Connected to Master Server");
				if (GM_connectRandom) {
					if (!string.IsNullOrEmpty(DD_networkServerName)) {
						Debug.Log("I am creating a custom open lobby named: " + DD_networkServerName);
						RoomOptions roomOptions = new RoomOptions();
						roomOptions.CustomRoomPropertiesForLobby = new string[1] { "server_name" };
						roomOptions.CustomRoomProperties = new ExitGames.Client.Photon.Hashtable {
							{
								"server_name",
								"[Modded] " + DD_networkServerName
							} 
						};
						roomOptions.MaxPlayers = configMaxPlayers.Value;
						roomOptions.IsVisible = true;
						PhotonNetwork.CreateRoom(null, roomOptions, DataDirector.instance.customLobby);
						return false;
					} else {
						return true;
					}
				} else {
					return true;
				}
			}
		}

		[HarmonyPatch(typeof(SteamManager), "HostLobby")]
		public class HostLobbyPatch {
			static bool Prefix() {
				HostLobbyAsync();
				return false;
			}

			static async void HostLobbyAsync() {
				Debug.Log("Steam: Hosting lobby...");
				Lobby? lobby = await SteamMatchmaking.CreateLobbyAsync(configMaxPlayers.Value);

				if (!lobby.HasValue) {
					Debug.LogError("Lobby created but not correctly instantiated.");
					return;
				}

				lobby.Value.SetPublic();
				lobby.Value.SetJoinable(b: false);
			}
		}
	}
}