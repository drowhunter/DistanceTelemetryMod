using Centrifuge.Distance.Game;

using Reactor.API.Attributes;
using Reactor.API.Configuration;
using Reactor.API.Interfaces.Systems;
using Reactor.API.Logging;
using Reactor.API.Runtime.Patching;

using System;
using System.Net;
using System.Runtime.InteropServices;

using TelemetryLibrary;

using UnityEngine;

namespace Distance.TelemetryMod
{
    /// <summary>
    /// The mod's main class containing its entry point
    /// </summary>
    [ModEntryPoint(ModID)]
    public sealed class Mod : MonoBehaviour, IDisposable
    {
        public const string ModID = "com.drowmods.DistanceTelemetryMod";
        public static Mod Instance { get; private set; }

        public IManager Manager { get; private set; }

        public Log Logger { get; private set; }

        private Settings _settings;

        string conn_host = "";
        int conn_port = -1;
        int packetId = 0;

        static UdpTelemetry<DistanceTelemetryData> udp;

        /// <summary>
        /// Method called as soon as the mod is loaded.
        /// WARNING:	Do not load asset bundles/textures in this function
        ///				The unity assets systems are not yet loaded when this
        ///				function is called. Loading assets here can lead to
        ///				unpredictable behaviour and crashes!
        /// </summary>
        public void Initialize(IManager manager)
        {
            InitializeSettings();

            // Do not destroy the current game object when loading a new scene
            DontDestroyOnLoad(this);

            Instance = this;

            Manager = manager;

            // Create a log file
            Logger = LogManager.GetForCurrentAssembly();

            Logger.Info("Telemetry Mod Init");

            RuntimePatcher.AutoPatch();
        }

        /// <summary>
        /// Method called after
        /// <c>GameManager.Start()</c>
        /// This initialisation method is the same as
        /// the Spectrum mod loader initialisation procedure.
        /// </summary>
        public void LateInitialize(IManager manager)
        {
            // Code here...
            var scene = Game.SceneName;
            
            if (!string.IsNullOrEmpty(conn_host?.Trim()) && conn_port > 0 && conn_port < 65536)
            {
                Logger.Info($"Sending Telemetry to {conn_host}:{conn_port}");

                udp = new UdpTelemetry<DistanceTelemetryData>(new UdpTelemetryConfig
                {
                    SendAddress = new IPEndPoint(IPAddress.Parse(conn_host), conn_port)
                });
            }
            else
            {
                Logger.Error("Invalid connection settings");
            }

            Initialize(manager);
        }

        public void InitializeSettings()
        {
            _settings = new Settings("telemetry");

            if (!_settings.ContainsKey("Host"))
            {
                _settings["Host"] = "127.0.0.1";
            }
            else
            {
                conn_host = _settings.GetItem<string>("Host");
            }
            if (!_settings.ContainsKey("Port"))
            {
                _settings["Port"] = 12345;
            }
            else
            {
                conn_port = _settings.GetItem<int>("Port");
            }

            _settings.Save();
        }

        private static Vector3 previousVelocity = Vector3.zero;
        private static Vector3 previousLocalVelocity = Vector3.zero;

        

        public void Update() {

            var car = GameObject.Find("LocalCar");
            if (car == null) return;            
            //Logger.Info("Car found " + car.name);

            var cRigidbody = car.GetComponent<Rigidbody>();
            if (cRigidbody == null) return;
            
            //Logger.Info("cRigidbody found ");
            var car_logic = car.GetComponent<CarLogic>();
            if (car_logic == null) return;             
            //Logger.Info("CarLogic found ");


            var playerdata = FindObjectOfType<PlayerDataLocal>();
            if (playerdata == null) return;
            
            //Logger.Info("PlayerDataLocal found ");

            var stats = car_logic?.CarStats_;
            if (stats == null) return;
            //LocalPlayerControlledCar localPlayerControlledCar = GetComponent<LocalPlayerControlledCar>();

            

            Quaternion rotation = cRigidbody.rotation;
            Vector3 eulerAngles = rotation.eulerAngles;
            Vector3 angularVelocity = cRigidbody.angularVelocity;

            Vector3 localAngularVelocity = cRigidbody.transform.InverseTransformDirection(cRigidbody.angularVelocity);
            Vector3 localVelocity = cRigidbody.transform.InverseTransformDirection(cRigidbody.velocity);

            Vector3 lgforce = (localVelocity - previousLocalVelocity) / Time.fixedDeltaTime / 9.81f;
            previousLocalVelocity = localVelocity;

            var centripetalForce = cRigidbody.velocity.magnitude * cRigidbody.angularVelocity.magnitude * Math.Sign(localAngularVelocity.y);
            

            Vector3 gforce = (cRigidbody.velocity - previousVelocity) / Time.fixedDeltaTime / 9.81f;
            previousVelocity = cRigidbody.velocity;

            //cRigidbody.velocity.magnitude;
            var velocity = cRigidbody.velocity;

            if (udp != null)
            {
                udp.Send(new DistanceTelemetryData
                {
                    PacketId = packetId,
                    KPH = Vehicle.VelocityKPH,
                    Yaw = hemiCircle(car.transform.rotation.eulerAngles.y),
                    Pitch = hemiCircle(car.transform.rotation.eulerAngles.x),
                    Roll = -hemiCircle(car.transform.rotation.eulerAngles.z),
                    Sway = centripetalForce,
                    Velocity = localVelocity,
                    Accel = lgforce,
                    Inputs = new Inputs
                    {
                        Gas = car_logic.CarDirectives_.Gas_,
                        Brake = car_logic.CarDirectives_.Brake_,
                        Steer = car_logic.CarDirectives_.Steer_,
                        Boost = car_logic.CarDirectives_.Boost_,
                        Wings = car_logic.Wings_.WingsOpen_,
                        Grip = car_logic.CarDirectives_.Grip_
                    },
                    Mass = cRigidbody.mass,
                    Finished = playerdata.Finished_,
                    AllWheelsOnGround = car_logic.CarStats_.AllWheelsContacting_,
                    isActiveAndEnabled = stats.isActiveAndEnabled, 
                    TireFL = new Tire
                    {
                        Contact = stats.wheelFL_.IsInContactSmooth_ ,
                        Position = stats.wheelFL_.hubTrans_.localPosition.y
                    },
                    TireFR = new Tire
                    {
                        Contact = stats.wheelFR_.IsInContactSmooth_,
                        Position = stats.wheelFR_.hubTrans_.localPosition.y
                    },
                    TireBL = new Tire
                    {
                        Contact = stats.wheelBL_.IsInContactSmooth_,
                        Position = stats.wheelBL_.hubTrans_.localPosition.y
                    },
                    TireBR = new Tire
                    {
                        Contact = stats.wheelBR_.IsInContactSmooth_,
                        Position = stats.wheelBR_.hubTrans_.localPosition.y
                    }
                });
            }
        }

        private static float hemiCircle(float angle)
        {
            return angle >= 180 ? angle - 360 : angle;
        }

        public void Dispose()
        {
            udp?.Dispose();
            packetId = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct DistanceTelemetryData
    {
        public int PacketId;
        public float KPH;
        public float Mass;
        public float Yaw;
        public float Pitch;
        public float Roll;
        public float Sway;
        public Vector3 Velocity;
        public Vector3 Accel;
        public Inputs Inputs;
        public bool Finished;
        public bool AllWheelsOnGround;
        public bool isActiveAndEnabled;
        public bool Grav;
        public float AngularDrag;
        public Tire TireFL;
        public Tire TireFR;
        public Tire TireBL;
        public Tire TireBR;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Tire
    {
        public bool Contact;
        public float Position;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Inputs
    {
        public float Gas;
        public float Brake;
        public float Steer;
        public bool Boost;
        public bool Grip;
        public bool Wings;
    }
}



