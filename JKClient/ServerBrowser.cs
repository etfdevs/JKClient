﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace JKClient {
	public sealed class ServerBrowser : NetClient {
		private const string MasterServerName = "masterjk3.ravensoft.com";
		private const string MasterServerName2 = "master.jkhub.org";
		private const string MasterServerName3 = "masterjk2.ravensoft.com";
		private const string MasterServerName4 = "master.jk2mv.org";
		private const int RefreshTimeout = 3000;
		private readonly KeyValuePair<string, ushort> []masterServers;
		private readonly Dictionary<NetAddress, ServerInfo> globalServers;
		private TaskCompletionSource<IEnumerable<ServerInfo>> getListTCS, refreshListTCS;
		private long serverRefreshTimeout = 0L;
		public ServerBrowser() {
			this.masterServers = new KeyValuePair<string, ushort>[] {
				new KeyValuePair<string, ushort>(ServerBrowser.MasterServerName, NetSystem.PortMasterJA),
				new KeyValuePair<string, ushort>(ServerBrowser.MasterServerName2, NetSystem.PortMasterJA),
				new KeyValuePair<string, ushort>(ServerBrowser.MasterServerName2, NetSystem.PortMasterJO),
				new KeyValuePair<string, ushort>(ServerBrowser.MasterServerName3, NetSystem.PortMasterJO),
				new KeyValuePair<string, ushort>(ServerBrowser.MasterServerName4, NetSystem.PortMasterJO)
			};
			this.globalServers = new Dictionary<NetAddress, ServerInfo>(new NetAddressComparer());
		}
		private protected override async Task Run() {
			const int frameTime = 8;
			while (true) {
				this.GetPacket();
				if (this.serverRefreshTimeout != 0 && this.serverRefreshTimeout < Common.Milliseconds) {
					this.getListTCS?.TrySetResult(this.globalServers.Values);
					this.getListTCS = null;
					this.refreshListTCS?.TrySetResult(this.globalServers.Values);
					this.refreshListTCS = null;
					this.serverRefreshTimeout = 0;
				}
				await Task.Delay(frameTime);
			}
		}
		public async Task<IEnumerable<ServerInfo>> GetNewList() {
			this.serverRefreshTimeout = Common.Milliseconds + ServerBrowser.RefreshTimeout;
			this.getListTCS?.TrySetCanceled();
			this.getListTCS = new TaskCompletionSource<IEnumerable<ServerInfo>>();
			this.globalServers.Clear();
			foreach (var masterServer in this.masterServers) {
				var address = NetSystem.StringToAddress(masterServer.Key, masterServer.Value);
				if (address == null) {
					continue;
				}
				foreach (ProtocolVersion protocol in Enum.GetValues(typeof(ProtocolVersion))) {
					if (protocol == ProtocolVersion.Unknown) {
						continue;
					}
					this.OutOfBandPrint(address, $"getservers {protocol.ToString("d")}");
				}
			}
			return await this.getListTCS.Task;
		}
		public async Task<IEnumerable<ServerInfo>> RefreshList() {
			if (this.globalServers.Count <= 0) {
				return await this.GetNewList();
			}
			this.serverRefreshTimeout = Common.Milliseconds + ServerBrowser.RefreshTimeout;
			this.refreshListTCS?.TrySetCanceled();
			this.refreshListTCS = new TaskCompletionSource<IEnumerable<ServerInfo>>();
			foreach (var server in this.globalServers) {
				var serverInfo = server.Value;
				serverInfo.InfoSet = false;
				serverInfo.Start = Common.Milliseconds;
				this.OutOfBandPrint(serverInfo.Address, "getinfo xxx");
			}
			return await this.refreshListTCS.Task;
		}
		private protected override unsafe void PacketEvent(NetAddress address, Message msg) {
			fixed (byte* b = msg.Data) {
				if (msg.CurSize >= 4 && *(int*)b == -1) {
					msg.BeginReading(true);
					msg.ReadLong();
					string s = msg.ReadStringLineAsString();
					var command = new Command(s);
					string c = command.Argv(0);
					if (string.Compare(c, "infoResponse", true) == 0) {
						this.ServerInfoPacket(address, msg);
					} else if (string.Compare(c, "statusResponse", true) == 0) {
						this.ServerStatusResponse(address, msg);
					} else if (string.Compare(c, 0, "getserversResponse", 0, 18, false) == 0) {
						this.ServersResponsePacket(address, msg);
					}
				}
			}
		}
		private unsafe void ServersResponsePacket(NetAddress address, Message msg) {
			fixed (byte *b = msg.Data) {
				byte *buffptr = b;
				byte *buffend = buffptr + msg.CurSize;
				do {
					if (*buffptr == 92) { //'\\'
						break;
					}
					buffptr++;
				} while (buffptr < buffend);
				while (buffptr + 1 < buffend) {
					if (*buffptr != 92) { //'\\'
						break;
					}
					buffptr++;
					byte []ip = new byte[4];
					if (buffend - buffptr < ip.Length + sizeof(ushort) + 1) {
						break;
					}
					for (int i = 0; i < ip.Length; i++) {
						ip[i] = *buffptr++;
					}
					int port = (*buffptr++) << 8;
					port += *buffptr++;
					var serverInfo = new ServerInfo() {
						Address = new NetAddress(ip, (ushort)port),
						Start = Common.Milliseconds
					};
					this.globalServers[serverInfo.Address] = serverInfo;
					this.OutOfBandPrint(serverInfo.Address, "getinfo xxx");
					if (*buffptr != 92 && *buffptr != 47) { //'\\' '/'
						break;
					}
				}
			}
		}
		private void ServerStatusResponse(NetAddress address, Message msg) {
			var infoString = new InfoString(msg.ReadStringLineAsString());
			if (this.globalServers.ContainsKey(address)) {
				var serverInfo = this.globalServers[address];
				if (infoString["version"].Contains("v1.03")) {
					serverInfo.Version = ClientVersion.JO_v1_03;
				}
				this.serverRefreshTimeout = Common.Milliseconds + ServerBrowser.RefreshTimeout;
			}
		}
		private void ServerInfoPacket(NetAddress address, Message msg) {
			var infoString = new InfoString(msg.ReadStringAsString());
			if (this.globalServers.ContainsKey(address)) {
				var serverInfo = this.globalServers[address];
				if (serverInfo.InfoSet) {
					return;
				}
				serverInfo.Ping = (int)(Common.Milliseconds - serverInfo.Start);
				serverInfo.SetInfo(infoString);
				if (serverInfo.Protocol == ProtocolVersion.Protocol15) {
					this.OutOfBandPrint(serverInfo.Address, "getstatus");
				}
				this.serverRefreshTimeout = Common.Milliseconds + ServerBrowser.RefreshTimeout;
			}
		}
	}
}
