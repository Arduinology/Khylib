using System;
using System.Collections.Generic;
using System.Linq;
using KSP.IO;
using UnityEngine;

namespace Khylib
{
    public class KestrelPartmodule : PartModule
    {
        public override void OnAwake()
        {
            Immortal.AddImmortal<NetworkView>();
            Immortal.AddImmortal<Kestrel>();
        }
    }

    [RequireComponent(typeof(NetworkView))]
    public class Kestrel : MonoBehaviour
    {
        public const int NetworkVersion = 1;
        public const int UtCompressRatio = 10000;
        private Window _kestrelWindow;
        private Window _chatWindow;
        private const int Port = 25290;
        private bool _updateGui;
        private int _lastUpdateSecond = DateTime.UtcNow.Second;
        private int _lastTimewarpIndex = -1;

        public void Start()
        {
            networkView.observed = this;
            networkView.stateSynchronization = NetworkStateSynchronization.Off;
        }

        public void Update()
        {
            var runPerSecondUpdates = _lastUpdateSecond != DateTime.UtcNow.Second;
            if (runPerSecondUpdates)
                _lastUpdateSecond = DateTime.UtcNow.Second;
            if (Network.isServer)
            {
                if (FlightGlobals.fetch != null && FlightGlobals.Vessels != null)
                    foreach (var vessel in FlightGlobals.Vessels.Where(v => v.networkView == null))
                        CreateVessel(vessel);
                if (runPerSecondUpdates || TimeWarp.fetch != null && _lastTimewarpIndex != TimeWarp.CurrentRateIndex)
                {
                    var ut = Planetarium.GetUniversalTime();
                    var longTime = (long)(ut * UtCompressRatio);
                    networkView.RPC("SetGlobals", RPCMode.Others, (int)(longTime / uint.MaxValue), (int)longTime, TimeWarp.CurrentRateIndex);
                    _lastTimewarpIndex = TimeWarp.CurrentRateIndex;
                }
            }
            else if (Network.isClient && FlightGlobals.fetch != null)
            {
                var toKill = new List<Vessel>();
                foreach (var vessel in FlightGlobals.Vessels.Where(v => v != FlightGlobals.ActiveVessel))
                {
                    if (vessel.networkView == null)
                        toKill.Add(vessel);
                    else if (vessel.networkView.isMine)
                        vessel.networkView.RPC("GiveOwnership", RPCMode.Server);
                }
                foreach (var vessel in toKill)
                    vessel.Die();
                if (FlightGlobals.ActiveVessel != null)
                {
                    if (FlightGlobals.ActiveVessel.networkView == null)
                        CreateVessel(FlightGlobals.ActiveVessel);
                    else if (FlightGlobals.ActiveVessel.networkView.isMine == false)
                        FlightGlobals.ActiveVessel.GetComponent<VesselNetworker>().GiveOwnership();
                }
            }
            if (_updateGui)
            {
                RefreshWindow();
                _updateGui = false;
            }
            if (!Input.GetKeyDown(KeyCode.F11))
                return;
            if (_kestrelWindow == null)
            {
                _kestrelWindow = new Window("Kestrel") { WindowRect = new Rect(200, 200, 200, 200), Contents = new List<IWindowContent>() };
                _updateGui = true;
            }
            else
                _kestrelWindow.IsRendered = !_kestrelWindow.IsRendered;
        }

        private void RefreshWindow()
        {
            if (_kestrelWindow == null)
                _kestrelWindow = new Window("Kestrel") { WindowRect = new Rect(200, 200, 200, 200), Contents = new List<IWindowContent>() };
            _kestrelWindow.Contents.Clear();
            if (Network.isServer || Network.isClient)
            {
                _kestrelWindow.Contents.Add(new Label(Network.isServer ? "Server" : "Client"));
                if (Network.isServer)
                    _kestrelWindow.Contents.Add(new Scroller(Network.connections.Select(n => (IWindowContent)new Label(n.ipAddress)).ToArray()));
                _kestrelWindow.Contents.Add(new Button("Toggle chat window", ToggleChatWindow));
                _kestrelWindow.Contents.Add(new Button("Disconnect", Disconnect));
            }
            else
            {
                _kestrelWindow.Contents.Add(new TextBox("", ""));
                _kestrelWindow.Contents.Add(new Button("Connect (ip address)", ConnectToIp));
                _kestrelWindow.Contents.Add(new Button("Host session", HostSession));
            }
            _kestrelWindow.WindowRect = _kestrelWindow.WindowRect.Set(200, 200);
        }

        private static void Disconnect()
        {
            Network.Disconnect(1000);
        }

        private void ToggleChatWindow()
        {
            if (_chatWindow == null)
                _chatWindow = new Window("Kestrel Chat") { WindowRect = new Rect(200, 200, 400, 300), Contents = new List<IWindowContent> { new Scroller(new IWindowContent[] { new Label("") }), new TextBox("", "", SendChatMessage) } };
            else
                _chatWindow.IsRendered = !_chatWindow.IsRendered;
        }

        private void SendChatMessage(string s)
        {
            networkView.RPC("OnChatMessage", RPCMode.All, "A name", s);
        }

        private void HostSession()
        {
            if (Network.isClient || Network.isServer)
            {
                ErrorPopup.Error("Cannot host server: Already connected");
                return;
            }
            Network.InitializeServer(10, Port, !Network.HavePublicAddress());
        }

        private void ConnectToIp()
        {
            if (Network.isClient || Network.isServer)
            {
                ErrorPopup.Error("Cannot connect: Already connected");
                return;
            }
            if (FlightGlobals.fetch != null && FlightGlobals.ActiveVessel != null)
            {
                ErrorPopup.Error("Cannot connect while in flight mode, please go to KSC.");
                return; // best solution would be main menu option, but I'm not Squad
            }
            Network.Connect(((TextBox)_kestrelWindow.Contents[0]).Value, Port);
        }

        private void CreateVessel(Vessel vessel)
        {
            var id = Network.AllocateViewID();
            vessel.gameObject.AddNetworkView(id);
            vessel.gameObject.AddComponent<VesselNetworker>();
            var cfg = new ConfigNode();
            vessel.protoVessel.Save(cfg);
            networkView.RPC("AllocVessel", RPCMode.Others, id, IOUtils.SerializeToBinary(cfg));
        }

        public void OnPlayerConnected(NetworkPlayer player)
        {
            foreach (var nv in FlightGlobals.Vessels.Select(v => v.networkView).Where(n => n != null))
                nv.SetScope(player, false);
            networkView.RPC("VersionCheck", player, FlightState.lastCompatibleMajor, FlightState.lastCompatibleMinor, FlightState.lastCompatibleRev, NetworkVersion);
            print(player.guid + " with ip " + player.ipAddress + " connected");
            _updateGui = true;
        }

        public void OnServerInitialized()
        {
            print("Sucessfully hosted server");
            _updateGui = true;
        }

        public void OnConnectedToServer()
        {
            print("Connected to server");
            _updateGui = true;
        }

        public void OnPlayerDisconnected(NetworkPlayer player)
        {
            print(player.guid + " with ip " + player.ipAddress + " disconnected");
            _updateGui = true;
        }

        public void OnDisconnectedFromServer(NetworkDisconnection reason)
        {
            print("Disconnected: " + reason);
            ErrorPopup.Error("Disconnected from server: " + reason);
            var game = GamePersistence.LoadGame("kestrel", HighLogic.SaveFolder, false, false);
            if (game == null)
                print("ERROR: Kestrel.sfs persistence file not found! Not reverting to original state!");
            else
            {
                HighLogic.CurrentGame = game;
                HighLogic.CurrentGame.flightState.Load();
            }
            _updateGui = true;
        }

        public void OnFailedToConnect(NetworkConnectionError reason)
        {
            print("Failed to connect: " + reason);
            ErrorPopup.Error("Failed to connect to server: " + reason);
            _updateGui = true;
        }

        [RPC]
        public void VersionCheck(int kspMajor, int kspMinor, int kspRev, int kestrel)
        {
            if (Network.isServer)
            {
                print("Warning: VersionCheck called on server");
                return;
            }
            if (FlightState.lastCompatibleMajor != kspMajor || FlightState.lastCompatibleMinor != kspMinor || FlightState.lastCompatibleRev != kspRev)
            {
                ErrorPopup.Error("Incompatible KSP versions, disconnected");
                print("Incompatible KSP versions\nMine: " + FlightState.lastCompatibleMajor + "." + FlightState.lastCompatibleMinor + "." + FlightState.lastCompatibleRev + "\nTheirs: " + kspMajor + "." + kspMinor + "." + kspRev);
                Network.Disconnect(200);
            }
            else if (NetworkVersion != kestrel)
            {
                ErrorPopup.Error("Incompatible Kestrel versions, disconnected");
                print("Incompatible Kestrel versions\nMine: " + NetworkVersion + "\nTheirs: " + kestrel);
                Network.Disconnect(200);
            }
            else
            {
                print("Saving : " + GamePersistence.SaveGame("kestrel", HighLogic.SaveFolder, SaveMode.OVERWRITE));
                foreach (var vessel in FlightGlobals.Vessels.ToArray())
                    vessel.Die();
                networkView.RPC("Handshake", RPCMode.Server);
            }
        }

        [RPC]
        public void Handshake(NetworkMessageInfo info)
        {
            if (Network.isServer)
            {
                foreach (var vessel in FlightGlobals.Vessels.Where(v => v.networkView != null))
                {
                    var cfg = new ConfigNode();
                    vessel.protoVessel.Save(cfg);
                    networkView.RPC("AllocVessel", info.sender, vessel.networkView.viewID, IOUtils.SerializeToBinary(cfg));
                }
                foreach (var nv in FlightGlobals.Vessels.Select(v => v.networkView).Where(n => n != null))
                    nv.SetScope(info.sender, true);
            }
            else
                print("Warning: Handshake called on client");
        }

        [RPC]
        public void AllocVessel(NetworkViewID id, byte[] binaryCfg)
        {
            if (FlightGlobals.Vessels.Any(v => v.networkView != null && v.networkView.viewID == id))
                return;
            var cfg = (ConfigNode)IOUtils.DeserializeFromBinary(binaryCfg);
            var protovessel = new ProtoVessel(cfg, HighLogic.CurrentGame.flightState);
            protovessel.orbitSnapShot.meanAnomalyAtEpoch += 1;
            protovessel.Load(HighLogic.CurrentGame.flightState);
            var vessel = protovessel.vesselRef;
            vessel.gameObject.AddNetworkView(id);
            vessel.gameObject.AddComponent<VesselNetworker>();
        }

        [RPC]
        public void SetGlobals(int largeUt, int smallUt, int timewarpIndex, NetworkMessageInfo info)
        {
            var packetTime = Network.time - info.timestamp;
            var longUt = largeUt * uint.MaxValue + smallUt;
            Planetarium.SetUniversalTime((double)longUt / UtCompressRatio + packetTime * TimeWarp.CurrentRate);
            TimeWarp.SetRate(timewarpIndex, true);
        }

        [RPC]
        public void OnChatMessage(string sender, string message)
        {
            ((Label)((Scroller)_chatWindow.Contents[0]).Contents[0]).Text += "<" + sender + "> " + message + "\n";
        }
    }

    [RequireComponent(typeof(Vessel), typeof(NetworkView))]
    public class VesselNetworker : MonoBehaviour
    {
        public void Start()
        {
            networkView.observed = this;
            networkView.stateSynchronization = NetworkStateSynchronization.Unreliable;
        }

        public void OnSerializeNetworkView(BitStream stream, NetworkMessageInfo info)
        {
            var vessel = GetComponent<Vessel>();
            if (stream.isWriting)
            {
                var eccentricity = (float)vessel.orbit.eccentricity;
                var semiMajorAxis = (float)vessel.orbit.semiMajorAxis;
                var inclination = (float)vessel.orbit.inclination;
                var lan = (float)vessel.orbit.LAN;
                var argumentOfPeriapsis = (float)vessel.orbit.argumentOfPeriapsis;
                var meanAnomalyAtEpoch = (float)vessel.orbit.meanAnomalyAtEpoch;
                var epoch = (float)vessel.orbit.epoch;
                var bodyId = FlightGlobals.Bodies.IndexOf(vessel.orbit.referenceBody);
                stream.Serialize(ref eccentricity, float.Epsilon);
                stream.Serialize(ref semiMajorAxis, float.Epsilon);
                stream.Serialize(ref inclination, float.Epsilon);
                stream.Serialize(ref lan, float.Epsilon);
                stream.Serialize(ref argumentOfPeriapsis, float.Epsilon);
                stream.Serialize(ref meanAnomalyAtEpoch, float.Epsilon);
                stream.Serialize(ref epoch, float.Epsilon);
                stream.Serialize(ref bodyId);
            }
            else
            {
                float eccentricity = 0f, semiMajorAxis = 0f, inclination = 0f, lan = 0f, argumentOfPeriapsis = 0f;
                var meanAnomalyAtEpoch = 0f;
                var epoch = 0f;
                var bodyId = 0;
                stream.Serialize(ref eccentricity, float.Epsilon);
                stream.Serialize(ref semiMajorAxis, float.Epsilon);
                stream.Serialize(ref inclination, float.Epsilon);
                stream.Serialize(ref lan, float.Epsilon);
                stream.Serialize(ref argumentOfPeriapsis, float.Epsilon);
                stream.Serialize(ref meanAnomalyAtEpoch, float.Epsilon);
                stream.Serialize(ref epoch, float.Epsilon);
                stream.Serialize(ref bodyId);
                var newOrbit = new Orbit(inclination, eccentricity, semiMajorAxis, lan, argumentOfPeriapsis, meanAnomalyAtEpoch, epoch, FlightGlobals.Bodies[bodyId]);
                newOrbit.UpdateFromUT(Planetarium.GetUniversalTime());
                if (vessel.packed)
                {
                    if (vessel.Landed)
                        vessel.Landed = false;
                    if (vessel.Splashed)
                        vessel.Splashed = false;
                    vessel.orbit.UpdateFromOrbitAtUT(newOrbit, Planetarium.GetUniversalTime(), newOrbit.referenceBody);
                }
                else
                {
                    vessel.SetPosition(newOrbit.pos);
                    vessel.SetWorldVelocity(newOrbit.vel);
                }
            }
        }

        [RPC]
        public void GiveOwnership()
        {
            networkView.RPC("SetOwner", RPCMode.All, Network.AllocateViewID());
        }

        [RPC]
        public void SetOwner(NetworkViewID id)
        {
            networkView.viewID = id;
        }

        public void OnDestroy()
        {
            if (Network.peerType != NetworkPeerType.Disconnected)
                networkView.RPC("KillSelf", RPCMode.Others);
        }

        [RPC]
        public void KillSelf()
        {
            var vessel = GetComponent<Vessel>();
            if (vessel.state != Vessel.State.DEAD)
                vessel.Die();
        }
    }
}
