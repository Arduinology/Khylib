using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.IO;
using UnityEngine;

namespace Khylib
{
    public abstract class Panel : Window
    {
        private static Krakensbane _krakensbane;

        public static Krakensbane Krakensbane
        {
            get { return _krakensbane ?? (_krakensbane = (Krakensbane)UnityEngine.Object.FindObjectOfType(typeof(Krakensbane))); }
        }

        protected Panel(string name)
            : base(name)
        {
            WindowRect = new Rect(200, 100, 300, 200);
        }

        public virtual void Update()
        {
        }

        public override bool IsActive
        {
            get
            {
                return HyperEditBehavior.IsEnabled;
            }
        }
    }

    public static class ErrorPopup
    {
        public static void Error(string message)
        {
            PopupDialog.SpawnPopupDialog("Error", message, "Close", true, HighLogic.Skin);
        }
    }

    public class OrbitEditor : Panel
    {
        private Orbit _setTo;
        private int _mode;
        private const int MaxModes = 3;

        public OrbitEditor()
            : base("Orbital editor")
        {
            NextMode();
        }

        private void NextMode()
        {
            _mode = (_mode + 1) % MaxModes;
            Refresh();
        }

        private void Refresh()
        {
            var orbit = FlightGlobals.ActiveVessel.orbit;
            switch (_mode)
            {
                case 0:
                    Contents = new List<IWindowContent>
                                   {
                                       new Button("Complex mode", NextMode),
                                       new Button("Refresh", Refresh),
                                       new TextBox("inc", orbit.inclination.ToString(CultureInfo.InvariantCulture)),
                                       new TextBox("e", orbit.eccentricity.ToString(CultureInfo.InvariantCulture)),
                                       new TextBox("sma", orbit.semiMajorAxis.ToString(CultureInfo.InvariantCulture)),
                                       new TextBox("lan", orbit.LAN.ToString(CultureInfo.InvariantCulture)),
                                       new TextBox("w", orbit.argumentOfPeriapsis.ToString(CultureInfo.InvariantCulture)),
                                       new TextBox("mEp", orbit.meanAnomalyAtEpoch.ToString(CultureInfo.InvariantCulture)),
                                       new TextBox("epoch", ""),
                                       new TextBox("body", orbit.referenceBody.bodyName),
                                       new Button("Set", SetComplex)
                                   };
                    break;
                case 1:
                    Contents = new List<IWindowContent>
                                   {
                                       new Button("Simple mode", NextMode),
                                       new Button("Refresh", Refresh),
                                       new TextBox("altitude", orbit.altitude.ToString(CultureInfo.InvariantCulture)),
                                       new TextBox("body", orbit.referenceBody.bodyName),
                                       new Button("Set", SetSimple)
                                   };
                    break;
                case 2:
                    Contents = new List<IWindowContent>
                                   {
                                       new Button("Graphical mode", NextMode),
                                       new TextBox("body", orbit.referenceBody.bodyName),
                                       new Slider("inc", 0, 360, FindField<Slider, float>("inc"), SliderUpdate),
                                       new Slider("e", 0, Mathf.PI/2 - 0.001f, FindField<Slider, float>("e"), SliderUpdate),
                                       new Slider("pe", 0.01f, 1, FindField<Slider, float>("pe"), SliderUpdate),
                                       new Slider("lan", 0, 360, FindField<Slider, float>("lan"), SliderUpdate),
                                       new Slider("w", 0, 360, FindField<Slider, float>("w"), SliderUpdate),
                                       new Slider("mEp", 0, Mathf.PI*2, FindField<Slider, float>("mEp"), SliderUpdate)
                                   };
                    break;
            }
            WindowRect = new Rect(WindowRect.xMin, WindowRect.yMin, WindowRect.xMax - WindowRect.xMin, 10);
        }

        public override void Update()
        {
            if (_setTo != null)
            {
                if (FlightGlobals.ActiveVessel.Landed)
                    FlightGlobals.ActiveVessel.Landed = false;
                if (FlightGlobals.ActiveVessel.Splashed)
                    FlightGlobals.ActiveVessel.Splashed = false;
                foreach (var part in FlightGlobals.ActiveVessel.parts.ToArray().Where(part => part.Modules.OfType<LaunchClamp>().Any()))
                    part.Die();
                _setTo.UpdateFromUT(Planetarium.GetUniversalTime());
                FlightGlobals.ActiveVessel.GoOnRails();
                var diff = _setTo.getTruePositionAtUT(Planetarium.GetUniversalTime()) - FlightGlobals.ActiveVessel.orbit.getTruePositionAtUT(Planetarium.GetUniversalTime());
                Krakensbane.Teleport(diff);
                FlightGlobals.ActiveVessel.SetPosition(_setTo.getPositionAtUT(Planetarium.GetUniversalTime()));
                FlightGlobals.ActiveVessel.orbit.IndirectSet(_setTo, Planetarium.GetUniversalTime());
                FlightGlobals.ActiveVessel.GoOffRails();
                _setTo = null;
            }
            base.Update();
        }

        public void SliderUpdate(float value)
        {
            var body = FlightGlobals.Bodies.FirstOrDefault(c => c.bodyName.ToLower() == (FindField<TextBox, string>("body") ?? "").ToLower());
            if (body == null)
            {
                ErrorPopup.Error("Unknown body");
                return;
            }
            var pe = (double)FindField<Slider, float>("pe");
            var ratio = body.sphereOfInfluence / (body.Radius + body.maxAtmosphereAltitude);
            pe = Math.Pow(ratio, pe) / ratio;
            pe *= body.sphereOfInfluence;
            var e = Math.Tan(FindField<Slider, float>("e"));
            var semimajor = pe / (1 - e);
            _setTo = Core.CreateOrbit(FindField<Slider, float>("inc"),
                                      e,
                                      semimajor,
                                      FindField<Slider, float>("lan"),
                                      FindField<Slider, float>("w"),
                                      FindField<Slider, float>("mEp"),
                                      FlightGlobals.ActiveVessel.orbit.epoch,
                                      body);
        }

        public void SetComplex()
        {
            double inc, e, sma, lan, w, mEp, epoch;
            var epochText = FindField<TextBox, string>("epoch");
            if (epochText.ToLower() == "now")
                epoch = Planetarium.GetUniversalTime();
            else if (string.IsNullOrEmpty(epochText))
                epoch = FlightGlobals.ActiveVessel.orbit.epoch;
            else if (UnitParser.Parse(epochText, "s", out epoch) == false)
            {
                ErrorPopup.Error("An orbital parameter was not a number");
                return;
            }
            if (UnitParser.Parse(FindField<TextBox, string>("inc"), "", out inc) == false ||
                UnitParser.Parse(FindField<TextBox, string>("e"), "", out e) == false ||
                UnitParser.Parse(FindField<TextBox, string>("sma"), "m", out sma) == false ||
                UnitParser.Parse(FindField<TextBox, string>("lan"), "", out lan) == false ||
                UnitParser.Parse(FindField<TextBox, string>("w"), "", out w) == false ||
                UnitParser.Parse(FindField<TextBox, string>("mEp"), "", out mEp) == false)
            {
                ErrorPopup.Error("An orbital parameter was not a number");
                return;
            }

            var body = FlightGlobals.Bodies.FirstOrDefault(c => c.bodyName.ToLower() == (FindField<TextBox, string>("body") ?? "").ToLower());
            if (body == null)
            {
                ErrorPopup.Error("Unknown body");
                return;
            }
            _setTo = Core.CreateOrbit(inc, e, sma, lan, w, mEp, epoch, body);
        }

        public void SetSimple()
        {
            double altitude;
            if (UnitParser.Parse(FindField<TextBox, string>("altitude"), "m", out altitude) == false)
            {
                ErrorPopup.Error("Altitude was not a number");
                return;
            }
            var body = FlightGlobals.Bodies.FirstOrDefault(c => c.bodyName.ToLower() == (FindField<TextBox, string>("body") ?? "").ToLower());
            if (body == null)
            {
                ErrorPopup.Error("Unknown body");
                return;
            }
            _setTo = Core.CreateOrbit(0, 0, altitude + body.Radius, 0, 0, 0, FlightGlobals.ActiveVessel.orbit.epoch, body);
        }
    }

    public class DebrisController : Panel
    {
        private Selector<Vessel> _vesselSelector;
        private Selector<ProtoCrewMember> _reviver;
        private Vessel _selected;
        public DebrisController()
            : base("Debris Controller")
        {
            UpdateContents();
        }

        public void UpdateContents()
        {
            Contents = new List<IWindowContent>
                           {
                               new Label("Editing " + (_selected == null ? "nothing" : _selected.vesselName)),
                               new Button("Select debris", SelectDebris),
                               new Button("Switch control to debris", SwitchControl),
                               new Button("Delete", Delete),
                               new Button("Revive a kerbal", Revive)
                           };
        }

        private void Revive()
        {
            if (_reviver == null || _reviver.IsRendered == false)
                _reviver = new Selector<ProtoCrewMember>("Revive", KerbalCrewRoster.CrewRoster.Where(k => k.rosterStatus == ProtoCrewMember.RosterStatus.DEAD), k => k.name, ReviveKerbal);
        }

        private static void ReviveKerbal(ProtoCrewMember protoCrewMember)
        {
            protoCrewMember.StartRespawnPeriod(0);
        }

        public void SelectDebris()
        {
            if (_vesselSelector == null || !_vesselSelector.IsRendered)
                _vesselSelector = new Selector<Vessel>("Select debris", FlightGlobals.Vessels.Where(v => v != FlightGlobals.ActiveVessel), v => v.vesselName, v =>
                                                                                                                                                                  {
                                                                                                                                                                      _selected = v;
                                                                                                                                                                      UpdateContents();
                                                                                                                                                                  });
        }

        private void SwitchControl()
        {
            if (_selected == null)
            {
                ErrorPopup.Error("No vessel selected");
                return;
            }
            FlightGlobals.SetActiveVessel(_selected);
        }

        private void Delete()
        {
            if (_selected == null)
            {
                ErrorPopup.Error("No vessel selected");
                return;
            }
            _selected.Die();
            _selected = null;
            UpdateContents();
        }
    }

    public class Lander : Panel
    {
        public Lander()
            : base("Lander")
        {
            Contents = new List<IWindowContent>
            {
                new TextBox("latitude", "0"),
                new TextBox("longitude", "0"),
                new TextBox("altitude", "50"),
                new Button("Land at coords", Land),
                new Button("Cancel landing", () => _landing = false),
                new Button("Set to current position", SetToCurrentPosition),
                new Button("Select object on ground...", SelectGroundObject),
                new TextBox("Save as", "", SaveCoords),
                new Button("Load coordanates...", LoadCoords),
                new Button("Delete coordanates...", DeleteCoords)
            };
            _saved = File.Exists<Core>("SavedLatLon.txt") ?
                File.ReadAllLines<Core>("SavedLatLon.txt").Select(SavedLatLon.Parse).Where(saved => saved != null).ToList() :
                new List<SavedLatLon>();
        }

        private LatLon _toLandAt;
        private bool _landing;
        private Selector<Transform> _selector;
        private readonly List<SavedLatLon> _saved;
        private Selector<SavedLatLon> _savedSelector;

        private void LoadCoords()
        {
            if (_savedSelector != null)
                _savedSelector.CloseWindow();
            _savedSelector = new Selector<SavedLatLon>("Load coordanates", _saved.Where(saved => saved.Body == FlightGlobals.ActiveVessel.mainBody), saved => saved.Name, saved =>
            {
                SetField<TextBox, string>("latitude", saved.LatLon.Latitude.ToString(CultureInfo.InvariantCulture));
                SetField<TextBox, string>("longitude", saved.LatLon.Longitude.ToString(CultureInfo.InvariantCulture));
                SetField<TextBox, string>("Save as", saved.Name);
            });
        }

        private void DeleteCoords()
        {
            if (_savedSelector != null)
                _savedSelector.CloseWindow();
            _savedSelector = new Selector<SavedLatLon>("Delete coordanates", _saved, saved => saved.Name + " on " + saved.Body.bodyName, saved =>
                                                                                                                                    {
                                                                                                                                        _saved.Remove(saved);
                                                                                                                                        SaveList();
                                                                                                                                    });
        }

        private void SaveCoords(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Contains(' '))
            {
                ErrorPopup.Error("Name cannot be empty or contain whitespace");
                return;
            }
            double latitude, longitude;
            if (UnitParser.Parse(FindField<TextBox, string>("latitude"), "", out latitude) == false || UnitParser.Parse(FindField<TextBox, string>("longitude"), "", out longitude) == false)
            {
                ErrorPopup.Error("A parameter was not a number");
                return;
            }
            var alreadyAdded = _saved.Where(saved => saved.Body == FlightGlobals.ActiveVessel.mainBody).FirstOrDefault(saved => saved.Name.ToLower() == s.ToLower());
            if (alreadyAdded == null)
            {
                alreadyAdded = new SavedLatLon();
                _saved.Add(alreadyAdded);
            }
            alreadyAdded.Body = FlightGlobals.ActiveVessel.mainBody;
            alreadyAdded.LatLon = new LatLon { Latitude = latitude, Longitude = longitude };
            alreadyAdded.Name = s;
            SaveList();
            ErrorPopup.Error("Saved " + s);
        }

        private void SaveList()
        {
            File.WriteAllLines<Core>(_saved.Select(s => s.ToString()).ToArray(), "SavedLatLon.txt");
        }

        private void SetToCurrentPosition()
        {
            SetField<TextBox, string>("latitude", FlightGlobals.ActiveVessel.latitude.ToString(CultureInfo.InvariantCulture));
            SetField<TextBox, string>("longitude", FlightGlobals.ActiveVessel.longitude.ToString(CultureInfo.InvariantCulture));
        }

        private void SelectGroundObject()
        {
            if (_selector != null)
                _selector.CloseWindow();
            _selector = new Selector<Transform>("Select object", FindInterestingThings(FlightGlobals.ActiveVessel.mainBody), a => a.name, a =>
                                         {
                                             SetField<TextBox, string>("latitude", FlightGlobals.ActiveVessel.mainBody.GetLatitude(a.position).ToString(CultureInfo.InvariantCulture));
                                             SetField<TextBox, string>("longitude", FlightGlobals.ActiveVessel.mainBody.GetLongitude(a.position).ToString(CultureInfo.InvariantCulture));
                                         });
        }

        // List ripped from ISA mapsat. Hey, its open source.
        private static readonly string[] InterestingThingIgnores =
            new[]
                {
                    "_",
                    "Box",
                    "Cube",
                    "taxiway",
                    "Cylinder",
                    "Line",
                    "Quad",
                    "puerta",
                    "CelestialBody",
                    "GameObject",
                    "Xp",
                    "Xn",
                    "Yp",
                    "Yn",
                    "Zp",
                    "Zn",
                    "Launchpad",
                    "Runway",
                    "Coolant",
                    "OceanFX",
                    "SurfaceQuadUV",
                    "Scatter",
                    "launchpad",
                    "Water Tower",
                    "tuberia",
                    "runway",
                    "SPH",
                    "spc_",
                    "crawler",
                    "groundPlane",
                    "tracking",
                    "Tracking",
                    "base",
                    "vehicle",
                    "Vehicle",
                    "ZZZZ",
                    "antena",
                    "Platform",
                    "LaunchPad",
                    "VertexPlanet",
                    "Clouds",
                    "Destruction"
                };

        private static IEnumerable<Transform> FindInterestingThings(CelestialBody body)
        {
            return body.GetComponentsInChildren<Transform>().Where(child => !InterestingThingIgnores.Any(i => child.name.StartsWith(i)) && !child.name.StartsWith(body.name));
        }

        public override void Update()
        {
            if (_landing)
            {
                if (FlightGlobals.ActiveVessel.LandedOrSplashed)
                    _landing = false;
                else
                {
                    var accel = FlightGlobals.ActiveVessel.srf_velocity * -0.5 + FlightGlobals.ActiveVessel.upAxis * -0.5;
                    FlightGlobals.ActiveVessel.ChangeWorldVelocity(accel);
                }
            }
            if (_toLandAt != null)
            {
                double raisedAlt;
                if (UnitParser.Parse(FindField<TextBox, string>("altitude"), "m", out raisedAlt) == false)
                {
                    ErrorPopup.Error("Altitude was not a number");
                    _toLandAt = null;
                    return;
                }
                var alt = FlightGlobals.ActiveVessel.mainBody.pqsController.GetSurfaceHeight(
                    QuaternionD.AngleAxis(_toLandAt.Longitude, Vector3d.down) *
                    QuaternionD.AngleAxis(_toLandAt.Latitude, Vector3d.forward) * Vector3d.right) -
                          FlightGlobals.ActiveVessel.mainBody.pqsController.radius;
                alt = Math.Max(alt, 0); // Underwater!
                var diff = FlightGlobals.ActiveVessel.mainBody.GetWorldSurfacePosition(_toLandAt.Latitude, _toLandAt.Longitude, alt + raisedAlt) - FlightGlobals.ActiveVessel.GetWorldPos3D();
                if (FlightGlobals.ActiveVessel.Landed)
                    FlightGlobals.ActiveVessel.Landed = false;
                else if (FlightGlobals.ActiveVessel.Splashed)
                    FlightGlobals.ActiveVessel.Splashed = false;
                foreach (var part in FlightGlobals.ActiveVessel.parts.ToArray().Where(part => part.Modules.OfType<LaunchClamp>().Any()))
                    part.Die();
                Krakensbane.Teleport(diff);
                FlightGlobals.ActiveVessel.ChangeWorldVelocity(-FlightGlobals.ActiveVessel.obt_velocity);
                //var up = (FlightGlobals.ActiveVessel.CoM - FlightGlobals.ActiveVessel.mainBody.position).normalized;
                //var forward = Vector3.Cross(up, Vector3.down);
                //var targetRot = Quaternion.LookRotation(forward, up);
                _toLandAt = null;
                _landing = true;
            }
            base.Update();
        }

        private void Land()
        {
            double latitude, longitude;
            if (UnitParser.Parse(FindField<TextBox, string>("latitude"), "", out latitude) == false || UnitParser.Parse(FindField<TextBox, string>("longitude"), "", out longitude) == false)
            {
                ErrorPopup.Error("A parameter was not a number");
                return;
            }
            _toLandAt = new LatLon { Latitude = latitude, Longitude = longitude };
        }

        class LatLon
        {
            public double Latitude;
            public double Longitude;
        }

        class SavedLatLon
        {
            public string Name;
            public CelestialBody Body;
            public LatLon LatLon;

            public static SavedLatLon Parse(string s)
            {
                var split = s.Split(null);
                if (split.Length != 4)
                {
                    MonoBehaviour.print("Failed to parse SavedLatLon\n" + s);
                    return null;
                }
                var retval = new SavedLatLon
                                 {
                                     Name = split[0],
                                     Body = FlightGlobals.Bodies.FirstOrDefault(c => c.bodyName.Replace(" ", "") == split[1]),
                                     LatLon = new LatLon()
                                 };
                if (retval.Body == null || double.TryParse(split[2], out retval.LatLon.Latitude) == false || double.TryParse(split[3], out retval.LatLon.Longitude) == false)
                {
                    MonoBehaviour.print("Failed to parse SavedLatLon\n" + s);
                    return null;
                }
                return retval;
            }

            public override string ToString()
            {
                return Name + " " + Body.bodyName.Replace(" ", "") + " " + LatLon.Latitude + " " + LatLon.Longitude;
            }
        }
    }

    public class PlanetaryEditor : Panel
    {
        public PlanetaryEditor()
            : base("Planetary editor")
        {
            WindowRect = new Rect(WindowRect.xMin, WindowRect.yMin, WindowRect.width, 500);
            Contents = new List<IWindowContent>
            {
                new Button("Refresh", Refresh),
                new TextBox("editing", ""), 
                new TextBox("inc", ""),
                new TextBox("e", ""),
                new TextBox("sma", ""),
                new TextBox("lan", ""),
                new TextBox("w", ""),
                new TextBox("mEp", ""),
                new TextBox("body", ""), 
                new Button("Set", Set),
                new Button("Save state...", SaveState),
                new Button("Load state...", LoadState)
            };
            Refresh();
        }

        public void Refresh()
        {
            var body = FlightGlobals.Bodies.FirstOrDefault(c => c.bodyName.ToLower() == (FindField<TextBox, string>("editing") ?? "").ToLower()) ?? FlightGlobals.ActiveVessel.orbit.referenceBody;
            var orbit = body.orbit;
            SetField<TextBox, string>("editing", body.bodyName);
            SetField<TextBox, string>("inc", orbit.inclination.ToString(CultureInfo.InvariantCulture));
            SetField<TextBox, string>("e", orbit.eccentricity.ToString(CultureInfo.InvariantCulture));
            SetField<TextBox, string>("sma", orbit.semiMajorAxis.ToString(CultureInfo.InvariantCulture));
            SetField<TextBox, string>("lan", orbit.LAN.ToString(CultureInfo.InvariantCulture));
            SetField<TextBox, string>("w", orbit.argumentOfPeriapsis.ToString(CultureInfo.InvariantCulture));
            SetField<TextBox, string>("mEp", orbit.meanAnomalyAtEpoch.ToString(CultureInfo.InvariantCulture));
            SetField<TextBox, string>("body", orbit.referenceBody.bodyName);
        }

        private static void SaveState()
        {
            var writer = TextWriter.CreateForType<Core>("OrbitState.txt");
            foreach (var body in FlightGlobals.Bodies.Where(body => body.orbitDriver != null))
            {
                MonoBehaviour.print("Saving " + body.bodyName);
                var orbit = body.orbit;
                var s = body.bodyName + " ";
                s += orbit.inclination + " ";
                s += orbit.eccentricity + " ";
                s += orbit.semiMajorAxis + " ";
                s += orbit.LAN + " ";
                s += orbit.argumentOfPeriapsis + " ";
                s += orbit.meanAnomalyAtEpoch + " ";
                s += orbit.epoch + " ";
                s += orbit.referenceBody.bodyName;
                writer.WriteLine(s);
            }
            writer.Flush();
            writer.Close();
            writer.Dispose();
            MonoBehaviour.print("Done saving planetary state");
        }

        public static void LoadState()
        {
            var reader = TextReader.CreateForType<Core>("OrbitState.txt");
            if (reader == null)
            {
                MonoBehaviour.print("OrbitState.txt not found, not setting anything");
                return;
            }
            while (reader.EndOfStream == false)
            {
                var read = reader.ReadLine();
                if (string.IsNullOrEmpty(read) || read.StartsWith("//"))
                    continue;
                var s = read.Split(null);
                if (s.Length != 9)
                {
                    MonoBehaviour.print("WARNING: OrbitState line invalid-\n" + read);
                    continue;
                }
                var body = FlightGlobals.Bodies.First(c => c.bodyName == s[0]);
                var referenceBody = FlightGlobals.Bodies.First(c => c.bodyName == s[8]);
                var orbit = Core.CreateOrbit(double.Parse(s[1]),
                                      double.Parse(s[2]),
                                      double.Parse(s[3]),
                                      double.Parse(s[4]),
                                      double.Parse(s[5]),
                                      double.Parse(s[6]),
                                      double.Parse(s[7]),
                                      referenceBody);
                MonoBehaviour.print("Setting " + body.bodyName);
                SetPlanet(body, orbit);
            }
            MonoBehaviour.print("All bodies set to new position");
        }

        private void Set()
        {
            var editing = FlightGlobals.Bodies.FirstOrDefault(c => c.bodyName.ToLower() == (FindField<TextBox, string>("editing") ?? "").ToLower());
            if (editing == null)
            {
                ErrorPopup.Error("Unknown editing body");
                return;
            }
            double inc, e, sma, lan, w, mEp;
            if (UnitParser.Parse(FindField<TextBox, string>("inc"), "", out inc) == false ||
                UnitParser.Parse(FindField<TextBox, string>("e"), "", out e) == false ||
                UnitParser.Parse(FindField<TextBox, string>("sma"), "m", out sma) == false ||
                UnitParser.Parse(FindField<TextBox, string>("lan"), "", out lan) == false ||
                UnitParser.Parse(FindField<TextBox, string>("w"), "", out w) == false ||
                UnitParser.Parse(FindField<TextBox, string>("mEp"), "", out mEp) == false)
            {
                ErrorPopup.Error("An orbital parameter was not a number");
                return;
            }
            var body = FlightGlobals.Bodies.FirstOrDefault(c => c.bodyName.ToLower() == (FindField<TextBox, string>("body") ?? "").ToLower());
            if (body == null)
            {
                ErrorPopup.Error("Unknown body");
                return;
            }
            for (var parentCheckBody = body; ; parentCheckBody = parentCheckBody.referenceBody)
            {
                if (parentCheckBody == editing)
                {
                    ErrorPopup.Error("Error: Self-referential orbit");
                    return;
                }
                if (parentCheckBody.orbitDriver == null)
                    break;
            }
            var newOrbit = Core.CreateOrbit(inc, e, sma, lan, w, mEp, editing.orbit.epoch, body);
            SetPlanet(editing, newOrbit);
            Refresh();
        }

        private static void SetPlanet(CelestialBody editing, Orbit newOrbit)
        {
            var oldBody = editing.referenceBody;
            editing.orbit.DirectSet(newOrbit, Planetarium.GetUniversalTime());
            if (oldBody == newOrbit.referenceBody)
                return;
            oldBody.orbitingBodies.Remove(editing);
            newOrbit.referenceBody.orbitingBodies.Add(editing);
        }
    }

    public class Rendezvous : Panel
    {
        private Orbit _target;
        private double _leadTime = 0.1;

        public Rendezvous()
            : base("Rendezvous")
        {
            Contents = new List<IWindowContent>
                           {
                               new TextBox("Lead time", "0.1", LeadTimeChange),
                               new Scroller(new IWindowContent[]
                                                {
                                                    new CustomDisplay(() =>
                                                                          {
                                                                              foreach (var vessel in FlightGlobals.Vessels.Where(vessel => vessel != FlightGlobals.ActiveVessel)
                                                                                  .Where(vessel => GUILayout.Button(vessel.vesselName)))
                                                                                  _target = vessel.orbit;
                                                                          })
                                                })
                           };
        }

        private void LeadTimeChange(string s)
        {
            double newLeadTime;
            if (UnitParser.Parse(s, "s", out newLeadTime))
                _leadTime = newLeadTime;
            else if (s == "TO THE BOULDER")
                _target = GameObject.Find("Magic Boulder").GetComponent<OrbitDriver>().orbit;
            else
                ErrorPopup.Error("Lead time was not a number");
        }

        public override void Update()
        {
            if (_target == null)
                return;
            if (FlightGlobals.ActiveVessel.Landed)
                FlightGlobals.ActiveVessel.Landed = false;
            if (FlightGlobals.ActiveVessel.Splashed)
                FlightGlobals.ActiveVessel.Splashed = false;
            foreach (var part in FlightGlobals.ActiveVessel.parts.ToArray().Where(part => part.Modules.OfType<LaunchClamp>().Any()))
                part.Die();
            FlightGlobals.ActiveVessel.GoOnRails();
            var diff = _target.getTruePositionAtUT(Planetarium.GetUniversalTime()) - FlightGlobals.ActiveVessel.orbit.getTruePositionAtUT(Planetarium.GetUniversalTime());
            Krakensbane.Teleport(diff);
            FlightGlobals.ActiveVessel.SetPosition(_target.getPositionAtUT(Planetarium.GetUniversalTime() + _leadTime));
            FlightGlobals.ActiveVessel.orbit.IndirectSet(_target, Planetarium.GetUniversalTime() + _leadTime);
            FlightGlobals.ActiveVessel.GoOffRails();
            _target = null;
        }

        protected override GUILayoutOption[] WindowOptions
        {
            get
            {
                return new[] { GUILayout.Width(300), GUILayout.Height(300) };
            }
        }
    }

    public class VelocityChanger : Panel
    {
        private bool _orbitReference = true;

        public VelocityChanger()
            : base("Velocity Changer")
        {
            Contents = new List<IWindowContent>
                           {
                               new Button(_orbitReference ? "relative to orbit" : "relative to surface", SwapOrbitReference),
                               new TextBox(_orbitReference ? "prograde" : "up", "0"),
                               new TextBox(_orbitReference ? "normal+" : "north", "0"),
                               new TextBox(_orbitReference ? "rad+" : "east", "0"),
                               new Button("Apply force", ApplyForce),
                               new Button("Kill orbital velocity", KillOrbitVel),
                               new Button("Kill surface velocity", KillSurfVel),
                               new Button("Kill vertical velocity", KillVertVel),
                               new Button("Kill horizontal velocity", KillHorizVel)
                           };
        }

        public void SwapOrbitReference()
        {
            _orbitReference = !_orbitReference;
            ((Button)Contents[0]).Text = _orbitReference ? "relative to orbit" : "relative to surface";
            ((TextBox)Contents[1]).Name = _orbitReference ? "prograde" : "up";
            ((TextBox)Contents[2]).Name = _orbitReference ? "normal+" : "north";
            ((TextBox)Contents[3]).Name = _orbitReference ? "rad+" : "east";
        }

        private static void KillHorizVel()
        {
            FlightGlobals.ActiveVessel.ChangeWorldVelocity(Vector3d.Dot(FlightGlobals.ActiveVessel.srf_velocity, FlightGlobals.ActiveVessel.upAxis) * FlightGlobals.ActiveVessel.upAxis - FlightGlobals.ActiveVessel.srf_velocity);
        }

        private static void KillVertVel()
        {
            FlightGlobals.ActiveVessel.ChangeWorldVelocity(-Vector3d.Dot(FlightGlobals.ActiveVessel.srf_velocity, FlightGlobals.ActiveVessel.upAxis) * FlightGlobals.ActiveVessel.upAxis);
        }

        private static void KillSurfVel()
        {
            FlightGlobals.ActiveVessel.ChangeWorldVelocity(-FlightGlobals.ActiveVessel.srf_velocity);
        }

        private static void KillOrbitVel()
        {
            FlightGlobals.ActiveVessel.ChangeWorldVelocity(-FlightGlobals.ActiveVessel.obt_velocity);
        }

        private void ApplyForce()
        {
            double prgVel, nrmVel, radVel;
            if (UnitParser.Parse(FindField<TextBox, string>("prograde") ?? FindField<TextBox, string>("up"), "m/s", out prgVel) == false ||
                UnitParser.Parse(FindField<TextBox, string>("normal+") ?? FindField<TextBox, string>("north"), "m/s", out nrmVel) == false ||
                UnitParser.Parse(FindField<TextBox, string>("rad+") ?? FindField<TextBox, string>("east"), "m/s", out radVel) == false)
            {
                ErrorPopup.Error("A force parameter was not a number");
                return;
            }
            Vector3d prograde, normal, rad;
            if (_orbitReference)
            {
                prograde = FlightGlobals.ActiveVessel.orbit.GetWorldSpaceVel().normalized;
                normal = Vector3d.Cross(prograde, FlightGlobals.ActiveVessel.upAxis);
                rad = Vector3d.Cross(normal, prograde);
            }
            else
            {
                prograde = FlightGlobals.ActiveVessel.upAxis;
                rad = FlightGlobals.ActiveVessel.mainBody.getRFrmVel(FlightGlobals.ActiveVessel.GetWorldPos3D()).normalized;
                normal = Vector3d.Cross(prograde, rad);
            }
            var finalForce = prograde * prgVel + normal * nrmVel + rad * radVel;
            FlightGlobals.ActiveVessel.ChangeWorldVelocity(finalForce);
        }
    }

    public class ConstantForcer : Panel
    {
        private bool _submarining;
        private bool _antigravity;
        private double _upForce;
        private double _forwardForce;

        public ConstantForcer()
            : base("Constant Forcer")
        {
            WindowRect = new Rect(WindowRect.xMin, WindowRect.yMin, 200, 100);
            Contents = new List<IWindowContent> { new Toggle("Submarine", OnSubChange),
                new Toggle("Antigravity", OnAntigravityChange),
                new TextBox("Force up", "0", ForceDownChange),
                new Label("Max forwards force (throttle)"),
                new TextBox("", "0", ForceForwardsChange) };
        }

        private void ForceForwardsChange(string s)
        {
            double newForce;
            if (UnitParser.Parse(s, "m/s/s", out newForce))
                _forwardForce = newForce;
            else
                ErrorPopup.Error("Force down was not a number");
        }

        private void ForceDownChange(string s)
        {
            double newForce;
            if (UnitParser.Parse(s, "m/s/s", out newForce))
                _upForce = newForce;
            else
                ErrorPopup.Error("Force down was not a number");
        }

        public override void Update()
        {
            foreach (var part in FlightGlobals.ActiveVessel.parts.Where(part => part.rigidbody != null))
            {
                if (_submarining && FlightGlobals.ActiveVessel.Splashed)
                {
                    if (part.partBuoyancy != null)
                        part.rigidbody.AddForce(-part.partBuoyancy.effectiveForce, ForceMode.Force);
                    part.rigidbody.AddForce(FlightGlobals.ActiveVessel.perturbation - FlightGlobals.ActiveVessel.acceleration, ForceMode.Acceleration);
                }
                if (_antigravity)
                    part.rigidbody.AddForce(FlightGlobals.ActiveVessel.perturbation - FlightGlobals.ActiveVessel.acceleration, ForceMode.Acceleration);
                if (Math.Abs(_upForce) > 1E-10)
                    part.rigidbody.AddForce(FlightGlobals.ActiveVessel.upAxis * _upForce, ForceMode.Acceleration);
                if (Math.Abs(_forwardForce) > 1E-10)
                    part.rigidbody.AddForce((Vector3d)FlightGlobals.ActiveVessel.transform.up * _forwardForce * FlightInputHandler.state.mainThrottle, ForceMode.Acceleration);
            }
        }

        private void OnSubChange(bool b) { _submarining = b; }
        private void OnAntigravityChange(bool b) { _antigravity = b; }
    }

    // to those of you looking to implement drifiting in your own plugins (better than I did here):
    // for each part that has a ModuleLandingGear, change module.wheelCollider.sidewaysFriction to something else.
    public class Drifter : Panel
    {
        public Drifter()
            : base("Drifter")
        {
            Contents = new List<IWindowContent>
                           {
                               new TextBox("extremumSlip", "0.3", ExtremumSlip), // default: 1
                               new TextBox("extremumValue", "750", ExtremumValue), // default: 2000
                               new TextBox("asymptoteSlip", "0.4", AsymptoteSlip), // default: 2
                               new TextBox("asymptoteValue", "30", AsymptoteValue), // default: 1000
                               new TextBox("stiffness", "0.1", Stiffness), // default: 0.1
                               new Button("Set all", SetAll)
                           };
        }

        private void SetAll()
        {
            ExtremumSlip(FindField<TextBox, string>("extremumSlip"));
            ExtremumValue(FindField<TextBox, string>("extremumValue"));
            AsymptoteSlip(FindField<TextBox, string>("asymptoteSlip"));
            AsymptoteValue(FindField<TextBox, string>("asymptoteValue"));
            Stiffness(FindField<TextBox, string>("stiffness"));
        }

        private void ExtremumSlip(string s)
        {
            float n;
            if (float.TryParse(s, out n) == false)
                ErrorPopup.Error("ExtremumSlip was not a number");
            foreach (var collider in FlightGlobals.ActiveVessel.parts.Select(p => p.Modules.OfType<ModuleLandingGear>().FirstOrDefault()).Where(m => m != null).Select(w => w.wheelCollider))
            {
                var curve = collider.sidewaysFriction;
                curve.extremumSlip = n;
                collider.sidewaysFriction = curve;
            }
        }

        private void ExtremumValue(string s)
        {
            float n;
            if (float.TryParse(s, out n) == false)
                ErrorPopup.Error("ExtremumValue was not a number");
            foreach (var collider in FlightGlobals.ActiveVessel.parts.Select(p => p.Modules.OfType<ModuleLandingGear>().FirstOrDefault()).Where(m => m != null).Select(w => w.wheelCollider))
            {
                var curve = collider.sidewaysFriction;
                curve.extremumValue = n;
                collider.sidewaysFriction = curve;
            }
        }

        private void AsymptoteSlip(string s)
        {
            float n;
            if (float.TryParse(s, out n) == false)
                ErrorPopup.Error("AsymptoteSlip was not a number");
            foreach (var collider in FlightGlobals.ActiveVessel.parts.Select(p => p.Modules.OfType<ModuleLandingGear>().FirstOrDefault()).Where(m => m != null).Select(w => w.wheelCollider))
            {
                var curve = collider.sidewaysFriction;
                curve.asymptoteSlip = n;
                collider.sidewaysFriction = curve;
            }
        }

        private void AsymptoteValue(string s)
        {
            float n;
            if (float.TryParse(s, out n) == false)
                ErrorPopup.Error("AsymptoteValue was not a number");
            foreach (var collider in FlightGlobals.ActiveVessel.parts.Select(p => p.Modules.OfType<ModuleLandingGear>().FirstOrDefault()).Where(m => m != null).Select(w => w.wheelCollider))
            {
                var curve = collider.sidewaysFriction;
                curve.asymptoteValue = n;
                collider.sidewaysFriction = curve;
            }
        }

        private void Stiffness(string s)
        {
            float n;
            if (float.TryParse(s, out n) == false)
                ErrorPopup.Error("Stiffness was not a number");
            foreach (var collider in FlightGlobals.ActiveVessel.parts.Select(p => p.Modules.OfType<ModuleLandingGear>().FirstOrDefault()).Where(m => m != null).Select(w => w.wheelCollider))
            {
                var curve = collider.sidewaysFriction;
                curve.stiffness = n;
                collider.sidewaysFriction = curve;
            }
        }
    }

    public class ResourceManager : Panel
    {
        public ResourceManager()
            : base("Resource Manager")
        {
            RefreshShipResources();
        }

        public void RefreshShipResources()
        {
            Contents = new List<IWindowContent> { new Button("Refresh resources on ship", RefreshShipResources), new Label("Refill:") };
            Contents.AddRange(FlightGlobals.ActiveVessel.parts.SelectMany(p => p.Resources.list.Select(r => r.info)).Distinct().Select(i => (IWindowContent)new Button(i.name, () => Refill(i))));
            WindowRect = new Rect(WindowRect.xMin, WindowRect.yMin, WindowRect.width, 10);
        }

        public void Refill(PartResourceDefinition resource)
        {
            foreach (var part in FlightGlobals.ActiveVessel.parts.Where(p => p.Resources.Contains(resource.id)))
            {
                var toSet = part.Resources.Get(resource.id);
                part.TransferResource(resource.id, toSet.maxAmount - toSet.amount);
            }
        }
    }
}
