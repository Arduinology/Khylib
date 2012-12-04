using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Khylib
{
    public class KestrelPartmodule : PartModule
    {
        public override void OnAwake()
        {
            Immortal.AddImmortal<Kestrel>();
        }
    }

    public class Kestrel : MonoBehaviour
    {
        private Window _kestrelWindow;
        private KestrelNetworker _networker;
        private const int Port = 25290;
        private bool _updateGui;

        public void Update()
        {
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
            _kestrelWindow.Contents.Clear();
            if (Network.isServer || Network.isClient)
            {
                _kestrelWindow.Contents.Add(new Label(Network.isServer ? "Server" : "Client"));
                _kestrelWindow.Contents.Add(new TextBox("", "", s => _networker.networkView.RPC("Alert", RPCMode.All, s)));
                if (Network.isServer)
                    _kestrelWindow.Contents.Add(new Scroller(Network.connections.Select(n => (IWindowContent)new Label(n.guid + " " + n.ipAddress)).ToArray()));
            }
            else
            {
                _kestrelWindow.Contents.Add(new TextBox("", ""));
                _kestrelWindow.Contents.Add(new Button("Connect (ip address)", ConnectToIp));
                _kestrelWindow.Contents.Add(new Button("Host session", HostSession));
            }
            _kestrelWindow.WindowRect = _kestrelWindow.WindowRect.Set(200, 200);
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
            Network.Connect(((TextBox)_kestrelWindow.Contents[0]).Value, Port);
        }

        private void RunNetInit()
        {
            _networker = new GameObject("KestrelNetworker", typeof(NetworkView), typeof(KestrelNetworker)).GetComponent<KestrelNetworker>();
            _networker.networkView.RPC("CreateVesselWatch", RPCMode.All, Network.AllocateViewID());
        }

        public void OnPlayerConnected(NetworkPlayer player)
        {
            foreach (var vesselWatch in FindObjectsOfType(typeof(VesselWatch)).Cast<VesselWatch>())
                _networker.networkView.RPC("CreateVesselWatch", player, vesselWatch.networkView.viewID);
            print(player.guid + " with ip " + player.ipAddress + " connected");
            _updateGui = true;
        }

        public void OnServerInitialized()
        {
            RunNetInit();
            print("Sucessfully hosted server");
            _updateGui = true;
        }

        public void OnConnectedToServer()
        {
            RunNetInit();
            print("Connected to server");
            _updateGui = true;
        }

        public void OnPlayerDisconnected(NetworkPlayer player)
        {
            foreach (var vesselWatch in FindObjectsOfType(typeof(VesselWatch)).Cast<VesselWatch>().Where(v => v.networkView.owner == player))
            {
                vesselWatch.KillVessel();
                Destroy(vesselWatch);
            }
            print(player.guid + " with ip " + player.ipAddress + " disconnected");
            _updateGui = true;
        }

        public void OnDisconnectedFromServer(NetworkDisconnection reason)
        {
            print("Disconnected: " + reason);
            ErrorPopup.Error("Disconnected from server: " + reason);
            _updateGui = true;
        }

        public void OnFailedToConnect(NetworkConnectionError reason)
        {
            print("Failed to connect: " + reason);
            ErrorPopup.Error("Failed to connect to server: " + reason);
            _updateGui = true;
        }
    }

    [RequireComponent(typeof(NetworkView))]
    public class KestrelNetworker : MonoBehaviour
    {
        public void Start()
        {
            networkView.observed = this;
            networkView.stateSynchronization = NetworkStateSynchronization.Off;
        }

        [RPC]
        public void Alert(string message, NetworkMessageInfo sender)
        {
            if (sender.sender == Network.player)
                return;
            ErrorPopup.Error(message);
            print(message);
        }

        [RPC]
        public void CreateVesselWatch(NetworkViewID id)
        {
            if (NetworkView.Find(id) == null)
                new GameObject("VesselWatch", typeof(NetworkView), typeof(VesselWatch)).GetComponent<NetworkView>().viewID = id;
        }
    }

    [RequireComponent(typeof(NetworkView))]
    public class VesselWatch : MonoBehaviour
    {
        public void Start()
        {
            networkView.observed = this;
        }

        private Vessel _watching;

        public void OnSerializeNetworkView(BitStream stream, NetworkMessageInfo info)
        {
            if (FlightGlobals.ActiveVessel == null)
            {
                var nullvec = Vector3.zero;
                var negOne = -1;
                stream.Serialize(ref nullvec);
                stream.Serialize(ref nullvec);
                stream.Serialize(ref negOne);
                return;
            }
            if (stream.isWriting)
            {
                if (_watching == null || _watching.isActiveVessel == false)
                    _watching = FlightGlobals.ActiveVessel;
                var pos = (Vector3)_watching.orbit.pos;
                var vel = (Vector3)_watching.orbit.vel;
                var bodyId = FlightGlobals.Bodies.IndexOf(_watching.orbit.referenceBody);
                stream.Serialize(ref pos);
                stream.Serialize(ref vel);
                stream.Serialize(ref bodyId);
            }
            else
            {
                Vector3 pos = new Vector3(), vel = new Vector3();
                var bodyId = 0;
                stream.Serialize(ref pos);
                stream.Serialize(ref vel);
                stream.Serialize(ref bodyId);
                if (bodyId == -1)
                {
                    KillVessel();
                    return;
                }
                if (_watching == null)
                {
                    var protovessel = new ProtoVessel(FlightGlobals.ActiveVessel);
                    protovessel.orbitSnapShot.meanAnomalyAtEpoch += 1;
                    var state = new FlightState { universalTime = Planetarium.GetUniversalTime() };
                    protovessel.Load(state);
                    _watching = protovessel.vesselRef;
                    print("Created vessel");
                }
                _watching.orbit.UpdateFromStateVectors(pos, vel, FlightGlobals.Bodies[bodyId], Planetarium.GetUniversalTime());
            }
        }

        public void KillVessel()
        {
            if (_watching != FlightGlobals.ActiveVessel)
                _watching.Die();
            _watching = null;
        }
    }
}
