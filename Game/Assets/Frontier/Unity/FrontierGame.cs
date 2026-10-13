using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using Frontier.Networking;

namespace Mayo.Frontier.Presentation
{
    [Serializable] sealed class FrontierSnapshot { public World state; public bool started; }
    public sealed class FrontierGame : MonoBehaviour
    {
        public GameProfile Profile {get;private set;}
        public ProgressData Progress {get;private set;}
        public World State {get;private set;}
        public Simulation Rules {get;private set;}
        public FrontierWorldView View {get;private set;}
        public FrontierUI UI {get;private set;}
        public string ScreenName {get;private set;}="title";
        public string Notice="";
        public bool IsClient {get{return lan!=null&&!lan.IsHost;}}
        public bool IsNetworked {get{return lan!=null;}}
        public int LocalPlayerId {get{return lan==null?0:lan.LocalPlayerId;}}
        public int ChapterChoice=1;
        public string HostAddress="192.168.1.101";
        public int Port {get;private set;}
        public string ConnectionLabel {get{return lan==null?"Solo campaign":lan.IsHost?"LAN host · "+lan.PeerIds.Length+" / 3 guests · port "+lan.Port:lan.IsConnected?"LAN connected · player "+lan.LocalPlayerId:"Connecting to LAN host…";}}
        public FrontierProgress Store {get;private set;}
        public InputActionMap Gameplay {get;private set;}
        LanSession lan;IDisposable discovery;FrontierAudio audio;
        InputAction move,aim,interact,attack,build,craft,pause,journal,cancel;
        int sequence,observedWin=-1;
        float inputClock,snapshotClock,hudClock,fireClock,inputGuardUntil;
        public bool SessionStarted {get;private set;}
        string buildChoice="",craftChoice="bandage",interactionChoice="";
        InputActionRebindingExtensions.RebindingOperation rebind;
        readonly Queue<Action> callbacks=new Queue<Action>();
        bool quitting;
        public bool QaActive {get;private set;}
        public bool RebindingActive {get{return rebind!=null;}}
        public int SnapshotsReceived {get;private set;}
        public int CommandsAccepted {get;private set;}
        public static string Arg(string key){string prefix="-"+key+"=";return Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length)??"";}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Boot(){if(FindAnyObjectByType<FrontierGame>()==null)new GameObject("FrontierGame").AddComponent<FrontierGame>();}
        void Awake()
        {
            var text=Resources.Load<TextAsset>("FrontierProfiles");if(text==null)throw new InvalidOperationException("FrontierProfiles is missing.");
            var catalog=JsonUtility.FromJson<GameProfiles>(text.text);string selected=Arg("frontier-game");
            if(string.IsNullOrEmpty(selected)){var external=Path.Combine(Application.dataPath,"../../Resources/frontier-profile.json");if(File.Exists(external))selected=JsonUtility.FromJson<GameProfile>(File.ReadAllText(external)).id;}
            if(string.IsNullOrEmpty(selected))selected=Resources.Load<TextAsset>("FrontierDefault")?.text.Trim();
            Profile=catalog.games.FirstOrDefault(g=>g.id==selected)??catalog.games[0];Port=47800+Array.IndexOf(catalog.games,Profile)*2;
            string directory=Arg("frontier-user-dir");QaActive=!string.IsNullOrEmpty(Arg("frontier-report"))||!string.IsNullOrEmpty(Arg("frontier-capture"))||!string.IsNullOrEmpty(Arg("frontier-lan-role"));
            if(QaActive&&(string.IsNullOrEmpty(directory)||directory.IndexOf("GameWorkbenchQA",StringComparison.Ordinal)<0))throw new InvalidOperationException("Native diagnostics require an isolated GameWorkbenchQA user directory.");
            Store=new FrontierProgress(string.IsNullOrEmpty(directory)?Path.Combine(Application.persistentDataPath,"Campaigns"):directory,Profile.id);Progress=Store.Load();Notice=Store.Notice;ChapterChoice=Progress.unlocked;
            ConfigureInput();ApplySettings();View=new GameObject("FrontierWorld").AddComponent<FrontierWorldView>();View.Initialize(Profile.id,Profile.accent,Profile.setting);if(View.ViewCamera.GetComponent<AudioListener>()==null)View.ViewCamera.gameObject.AddComponent<AudioListener>();audio=gameObject.AddComponent<FrontierAudio>();
            Rules=new Simulation(Profile.Rules());State=Rules.State;UI=new GameObject("FrontierUI").AddComponent<FrontierUI>();UI.Initialize(this);UI.Show("title");
            if(QaActive)gameObject.AddComponent<FrontierNativeProbe>().Initialize(this);
        }
        void ConfigureInput()
        {
            Gameplay=new InputActionMap("Gameplay");move=Gameplay.AddAction("Move",InputActionType.Value,expectedControlLayout:"Vector2");move.AddCompositeBinding("2DVector").With("Up","<Keyboard>/w").With("Down","<Keyboard>/s").With("Left","<Keyboard>/a").With("Right","<Keyboard>/d");move.AddBinding("<Gamepad>/leftStick");
            aim=Gameplay.AddAction("Aim",InputActionType.Value,"<Gamepad>/rightStick");
            interact=Gameplay.AddAction("Interact",InputActionType.Button,"<Keyboard>/e");interact.AddBinding("<Gamepad>/buttonSouth");
            attack=Gameplay.AddAction("Attack",InputActionType.Button,"<Mouse>/leftButton");attack.AddBinding("<Gamepad>/rightTrigger");attack.AddBinding("<Gamepad>/buttonWest");
            build=Gameplay.AddAction("Build",InputActionType.Button,"<Keyboard>/b");build.AddBinding("<Gamepad>/leftShoulder");
            craft=Gameplay.AddAction("Craft",InputActionType.Button,"<Keyboard>/q");craft.AddBinding("<Gamepad>/rightShoulder");
            pause=Gameplay.AddAction("Pause",InputActionType.Button,"<Keyboard>/escape");pause.AddBinding("<Gamepad>/start");
            journal=Gameplay.AddAction("Journal",InputActionType.Button,"<Keyboard>/j");journal.AddBinding("<Gamepad>/select");cancel=Gameplay.AddAction("Cancel",InputActionType.Button,"<Gamepad>/buttonEast");
            // Persisted remaps identify bindings by GUID. Runtime-created bindings need
            // stable identities across processes, before any overrides are restored.
            using(var hash=SHA256.Create())foreach(var action in Gameplay.actions)for(int index=0;index<action.bindings.Count;index++){var binding=action.bindings[index];byte[] digest=hash.ComputeHash(Encoding.UTF8.GetBytes("FrontierInput/v1/"+action.name+"/"+binding.name+"/"+binding.path+"/"+binding.groups));binding.id=new Guid(digest.Take(16).ToArray());action.ChangeBinding(index).To(binding);}
            Gameplay.Enable();
            if(!string.IsNullOrEmpty(Progress.bindings))try{Gameplay.LoadBindingOverridesFromJson(Progress.bindings);}catch(Exception){Notice="Saved bindings could not be read; defaults are available.";}
        }
        public void ApplySettings(){AudioListener.volume=Progress.volume;QualitySettings.SetQualityLevel(Mathf.RoundToInt(Progress.quality*(QualitySettings.names.Length-1)/2f));Screen.fullScreenMode=Progress.fullScreen?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed;}
        public void SaveSettings(){Progress.bindings=Gameplay.SaveBindingOverridesAsJson();Store.Save(Progress);if(Store.Notice!="")Notice=Store.Notice;}
        void Update()
        {
            lock(callbacks)while(callbacks.Count>0)callbacks.Dequeue()();lan?.Pump();
            if(!QaActive&&!RebindingActive&&Time.unscaledTime>=inputGuardUntil)
            {
                DispatchPanelInput();
                if(ScreenName=="play"&&State!=null&&State.phase=="active"&&Time.unscaledTime>=inputGuardUntil)ReadGameplay();
            }
            if(!QaActive&&SessionStarted&&(lan==null||lan.IsHost)){Rules?.Tick(Mathf.Min(Time.unscaledDeltaTime,.1f));State=Rules?.State;}
            if(lan!=null&&lan.IsHost&&Rules!=null){snapshotClock+=Time.unscaledDeltaTime;if(snapshotClock>=.1f){snapshotClock=0;lan.BroadcastSnapshot(JsonUtility.ToJson(new FrontierSnapshot{state=Rules.State,started=SessionStarted}));}}
            if(State!=null&&State.phase=="won"&&observedWin!=State.chapter){observedWin=State.chapter;Progress.completedMask|=1<<(State.chapter-1);Progress.unlocked=Mathf.Max(Progress.unlocked,Mathf.Min(3,State.chapter+1));Store.Save(Progress);if(!QaActive)Show("result");}
            if(State!=null&&State.phase=="lost"&&ScreenName=="play"&&!QaActive)Show("result");
            hudClock+=Time.unscaledDeltaTime;if(hudClock>.15f){hudClock=0;UI.RefreshHUD();}
        }
        void LateUpdate(){if(State!=null)View.Render(State,LocalPlayerId);}
        void DispatchPanelInput()
        {
            if(pause.WasPressedThisFrame()){if(ScreenName=="play")OpenPanel("pause");else if(ScreenName=="pause"||ScreenName=="journal")Resume();else if(ScreenName!="title")UI.Back();}
            if(cancel.WasPressedThisFrame()&&ScreenName!="play"&&ScreenName!="title")UI.Back();
            if(journal.WasPressedThisFrame()){if(ScreenName=="play")OpenPanel("journal");else if(ScreenName=="journal")Resume();}
        }
        void ReadGameplay()
        {
            inputClock+=Time.unscaledDeltaTime;fireClock+=Time.unscaledDeltaTime;Vector2 direction=move.ReadValue<Vector2>(),look=aim.ReadValue<Vector2>();
            Player p=Rules!=null&&!IsClient?Rules.PlayerById(LocalPlayerId):State.players.Find(x=>x.id==LocalPlayerId);
            if(p==null)return;
            // Isometric movement maps the stick to camera-relative ground axes.
            Vector3 forward=Vector3.ProjectOnPlane(View.ViewCamera.transform.forward,Vector3.up).normalized,right=Vector3.ProjectOnPlane(View.ViewCamera.transform.right,Vector3.up).normalized;
            Vector3 motion=right*direction.x+forward*direction.y;Vector3 target=right*look.x+forward*look.y;
            if(look.sqrMagnitude<.1f&&Mouse.current!=null&&Mouse.current.delta.ReadValue().sqrMagnitude>.01f){Ray ray=View.ViewCamera.ScreenPointToRay(Mouse.current.position.ReadValue());var plane=new Plane(Vector3.up,Vector3.zero);if(plane.Raycast(ray,out float distance)){Vector3 point=ray.GetPoint(distance);target=point-new Vector3(p.x,0,p.z);}}
            if(target.sqrMagnitude<.01f)target=new Vector3(p.faceX,0,p.faceZ);
            if(inputClock>=.05f){inputClock=0;Send(new Command{kind="move",x=motion.x,z=motion.z,aimX=target.x,aimZ=target.z},false);}
            bool over=attack.activeControl?.device is Mouse&&EventSystem.current!=null&&EventSystem.current.IsPointerOverGameObject();
            if(interact.WasPressedThisFrame())Send(new Command{kind="interact",option=interactionChoice});
            if(attack.IsPressed()&&!over&&fireClock>=.15f){fireClock=0;Send(new Command{kind="attack",aimX=target.x,aimZ=target.z},false);}
            if(build.WasPressedThisFrame())Send(new Command{kind="build",x=p.x+p.faceX*2.2f,z=p.z+p.faceZ*2.2f,option=buildChoice});
            if(craft.WasPressedThisFrame())Send(new Command{kind="craft",option=craftChoice});
        }
        public ActionResult Send(Command command,bool feedback=true)
        {
            var current=State?.players?.Find(p=>p.id==LocalPlayerId);if(current!=null)sequence=Math.Max(sequence,current.lastSequence);command.sequence=++sequence;
            if(IsClient){if(lan.SendCommand(JsonUtility.ToJson(command)))return new ActionResult(true,"Sent to host.");return new ActionResult(false,"Not connected to host.");}
            var result=Rules.Apply(0,command);if(result.accepted){CommandsAccepted++;if(command.kind=="attack"||command.kind=="interact")audio.Cue(command.kind);}if(feedback&&!string.IsNullOrEmpty(result.reason))Notice=result.reason;State=Rules.State;return result;
        }
        public void StartSolo(){Disconnect();sequence=0;observedWin=-1;Rules=new Simulation(Profile.Rules(),Mathf.Clamp(ChapterChoice,1,Progress.unlocked));State=Rules.State;Show("story");}
        public void BeginChapter(){SessionStarted=true;Show("play");}
        public void Advance(){Send(new Command{kind="advance"});SessionStarted=false;Show("story");}
        public void Retry(){Send(new Command{kind="restart"});SessionStarted=false;observedWin=-1;Show("story");}
        public void OpenPanel(string screen){if(State?.phase=="active"){Send(new Command{kind="move"},false);if(!IsNetworked)Send(new Command{kind="pause"},false);}Show(screen);}
        public void Resume(){if(!IsNetworked&&State.phase=="paused")Send(new Command{kind="pause"},false);Show("play");}
        public void Show(string screen){ScreenName=screen;inputGuardUntil=Time.unscaledTime+.15f;UI.Show(screen);}
        public void SelectBuild(string option){buildChoice=option;Notice="Build selected: "+option;Resume();}
        public void SelectInteraction(string option){interactionChoice=option;Notice="Interaction selected: "+(option==""?"default":option);Resume();}
        public void SelectCraft(string option){craftChoice=option;Notice="Craft selected: "+option;Resume();}
        public void AssignResident(int targetId,string role)
        {
            bool restorePause=!IsNetworked&&State?.phase=="paused";
            if(restorePause){var resumed=Send(new Command{kind="pause"},false);if(!resumed.accepted){Notice=resumed.reason;UI.RefreshHUD();return;}}
            try
            {
                var result=Send(new Command{kind="assign",targetId=targetId,option=role});
                if(IsClient)Notice=result.accepted?"Role request sent to host. Current role updates after approval; return to the community centre if unchanged.":result.reason;
            }
            finally{if(restorePause&&!IsNetworked&&State?.phase=="active")Send(new Command{kind="pause"},false);}
            UI.RefreshHUD();
        }
        public void BackToTitle(){Disconnect();Rules=new Simulation(Profile.Rules());State=Rules.State;Show("title");}
        public void NewCampaign(){Progress.unlocked=1;Progress.completedMask=0;Store.Save(Progress);ChapterChoice=1;StartSolo();}
        public void Host()
        {
            Disconnect();Rules=new Simulation(Profile.Rules(),Mathf.Clamp(ChapterChoice,1,Progress.unlocked));State=Rules.State;sequence=0;observedWin=-1;
            try{lan=LanSession.Host(Profile.id,Port,"0.0.0.0",3);WireLan();try{discovery=LanDiscovery.Advertise(Profile.id,Port,Profile.title,Port+1);}catch(Exception e){Notice="Direct-address hosting ready; discovery unavailable: "+e.Message;}Show("lobby");}catch(Exception e){Notice="Could not host: "+e.Message;Disconnect();Show("lan");}
        }
        public void Join()
        {
            Disconnect();sequence=0;observedWin=-1;Rules=null;State=null;
            try{lan=LanSession.Join(Profile.id,HostAddress,Port);WireLan();Show("connecting");}catch(Exception e){Notice=e.Message;Show("lan");}
        }
        void WireLan()
        {
            lan.PeerJoined+=id=>{if(!Rules.AddPlayer(id)){lan.DisconnectPeer(id);return;}Notice="Player "+id+" joined.";};
            lan.PeerLeft+=id=>{if(IsClient){Notice="The host disconnected. Rejoin or start a new session.";Disconnect();Rules=null;State=null;Show("lan");}else{Rules?.Disconnect(id);Notice="Player "+id+" disconnected.";}};
            lan.CommandReceived+=(id,json)=>{if(!SessionStarted)return;try{var command=JsonUtility.FromJson<Command>(json);if(command==null||!new[]{"move","aim","interact","attack","build","craft","assign"}.Contains(command.kind)){lan.DisconnectPeer(id);return;}var result=Rules.Apply(id,command);if(result.accepted)CommandsAccepted++;}catch(Exception){lan.DisconnectPeer(id);}};
            lan.SnapshotReceived+=json=>{try{var snapshot=JsonUtility.FromJson<FrontierSnapshot>(json);var next=snapshot?.state;if(next==null||next.configId!=Profile.id||next.players==null||next.players.Count>4)throw new InvalidDataException("Host sent an incompatible snapshot.");int priorChapter=State?.chapter??0;string priorPhase=State?.phase;State=next;SessionStarted=snapshot.started;SnapshotsReceived++;if(ScreenName=="connecting"||ScreenName=="lobby"||priorChapter!=next.chapter||(ScreenName=="result"&&priorPhase!=next.phase)){string nextScreen=SessionStarted?"play":"lobby";if(ScreenName!=nextScreen)Show(nextScreen);}}catch(Exception e){Notice=e.Message;Disconnect();Show("lan");}};
            lan.Faulted+=reason=>{Notice=reason;if(IsClient){Disconnect();Show("lan");}};
        }
        public void Disconnect(){SessionStarted=false;discovery?.Dispose();discovery=null;lan?.Dispose();lan=null;}
        public void ScanRooms(Action<LanRoom[]> done)
        {
            Task.Run(()=>{try{var rooms=LanDiscovery.Scan(Profile.id,700,Port+1);lock(callbacks)callbacks.Enqueue(()=>done(rooms));}catch(Exception e){lock(callbacks)callbacks.Enqueue(()=>{Notice=e.Message;done(new LanRoom[0]);});}});
        }
        public void Rebind(string actionName)
        {
            if(rebind!=null)return;var action=Gameplay.FindAction(actionName);action.Disable();Notice="Press a keyboard key, or Escape to cancel.";UI.Show("options");
            rebind=action.PerformInteractiveRebinding(0).WithCancelingThrough("<Keyboard>/escape").WithControlsExcluding("<Mouse>/position").WithControlsExcluding("<Mouse>/delta").OnComplete(op=>{op.Dispose();rebind=null;action.Enable();SaveSettings();Notice="Binding saved.";Show("options");}).OnCancel(op=>{op.Dispose();rebind=null;action.Enable();Notice="Binding unchanged.";Show("options");});rebind.Start();
        }
        public void RestoreBindings(){Gameplay.RemoveAllBindingOverrides();SaveSettings();UI.Show("options");}
        public void SetNativeState(Simulation simulation){Rules=simulation;State=simulation.State;sequence=0;}
        public void ReadNativeInput(){if(Time.unscaledTime<inputGuardUntil)return;DispatchPanelInput();if(ScreenName=="play"&&State?.phase=="active")ReadGameplay();}
        public void RefreshNativeState(){State=Rules?.State??State;}
        public void Quit(){quitting=true;SaveSettings();Disconnect();Application.Quit();}
        void OnApplicationQuit(){if(!quitting)SaveSettings();Disconnect();}
        void OnDestroy(){rebind?.Dispose();Gameplay?.Dispose();Disconnect();}
    }
}
