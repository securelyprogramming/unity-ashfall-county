using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem;
using Frontier.Networking;

namespace Mayo.Frontier.Presentation
{
    public sealed class FrontierUI:MonoBehaviour
    {
        FrontierGame game;Canvas canvas;RectTransform root,menu,menuRoot,hud;
        LanRoom[] foundRooms=new LanRoom[0];TMP_FontAsset font;
        TMP_Text hudTitle,hudObjective,hudVitals,hudResources,hudContext,hudMessage,hudNetwork;
        UnityEngine.UI.Image healthFill;
        readonly List<UnityEngine.UI.Button> buttons=new List<UnityEngine.UI.Button>();
        string previous="title",menuBack="title";Color accent;
        public void Initialize(FrontierGame owner)
        {
            game=owner;ColorUtility.TryParseHtmlString(game.Profile.accent,out accent);
            font=Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");if(font==null)throw new InvalidOperationException("The redistributable LiberationSans UI font is missing.");
            var go=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));go.transform.SetParent(transform,false);root=go.GetComponent<RectTransform>();canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
            var scaler=go.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1440,900);scaler.matchWidthOrHeight=1;
            if(EventSystem.current==null){var es=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();}
            hud=Panel("HUD",root,new Color(0,0,0,0));Stretch(hud);
            var top=Panel("Header",hud,new Color(.025f,.05f,.065f,.94f));Horizontal(top,24,24,22,112);
            hudTitle=Text("Title",top,game.Profile.title,20,12,900,30,24,accent,FontStyles.Bold);hudObjective=Text("Objective",top,"",20,47,1000,54,18,Color.white);
            Horizontal(hudTitle.rectTransform,20,390,12,30);Horizontal(hudObjective.rectTransform,20,390,47,54);
            hudNetwork=Text("Network",top,"Solo campaign",1030,12,340,30,15,new Color(.72f,.83f,.86f));Right(hudNetwork.rectTransform,20,12,340,30);
            var bar=Panel("HealthBackground",top,new Color(.15f,.22f,.24f));Right(bar,34,53,310,18);healthFill=Panel("HealthFill",bar,new Color(.49f,.78f,.62f)).GetComponent<UnityEngine.UI.Image>();Stretch(healthFill.rectTransform);
            hudVitals=Text("Vitals",top,"",1040,77,330,22,14,Color.white);Right(hudVitals.rectTransform,20,77,330,22);
            var bottom=Panel("Status",hud,new Color(.025f,.05f,.065f,.94f));Bottom(bottom,24,24,24,116);
            hudResources=Text("Resources",bottom,"",18,10,900,26,17,accent);hudMessage=Text("Message",bottom,"",18,44,1000,52,17,Color.white);
            Horizontal(hudResources.rectTransform,18,350,10,26);Horizontal(hudMessage.rectTransform,18,350,44,52);
            hudContext=Text("Context",hud,"",28,687,1100,58,18,new Color(.9f,.92f,.86f));Bottom(hudContext.rectTransform,28,28,155,58);
            var actions=SmallButton(bottom,"Actions",1050,14,145,38,()=>game.OpenPanel("actions"));Right(actions.GetComponent<RectTransform>(),176,14,145,38);var journalButton=SmallButton(bottom,"Journal",1208,14,145,38,()=>game.OpenPanel("journal"));Right(journalButton.GetComponent<RectTransform>(),18,14,145,38);var pauseButton=SmallButton(bottom,"Pause",1208,62,145,36,()=>game.OpenPanel("pause"));Right(pauseButton.GetComponent<RectTransform>(),18,62,145,36);
        }
        static void Stretch(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        static void Place(RectTransform rect,float x,float y,float w,float h){rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);}
        static void Horizontal(RectTransform rect,float left,float right,float top,float height){rect.anchorMin=new Vector2(0,1);rect.anchorMax=new Vector2(1,1);rect.pivot=new Vector2(0,1);rect.offsetMin=new Vector2(left,-top-height);rect.offsetMax=new Vector2(-right,-top);}
        static void Bottom(RectTransform rect,float left,float right,float bottom,float height){rect.anchorMin=new Vector2(0,0);rect.anchorMax=new Vector2(1,0);rect.pivot=new Vector2(0,0);rect.offsetMin=new Vector2(left,bottom);rect.offsetMax=new Vector2(-right,bottom+height);}
        static void Right(RectTransform rect,float right,float top,float width,float height){rect.anchorMin=rect.anchorMax=new Vector2(1,1);rect.pivot=new Vector2(1,1);rect.anchoredPosition=new Vector2(-right,-top);rect.sizeDelta=new Vector2(width,height);}
        RectTransform Panel(string name,Transform parent,Color color){var go=new GameObject(name,typeof(RectTransform),typeof(UnityEngine.UI.Image));go.transform.SetParent(parent,false);var image=go.GetComponent<UnityEngine.UI.Image>();image.color=color;image.raycastTarget=false;return go.GetComponent<RectTransform>();}
        TMP_Text Text(string name,Transform parent,string value,float x,float y,float w,float h,float size,Color color,FontStyles style=FontStyles.Normal)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);var t=go.GetComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.color=color;t.fontStyle=style;t.textWrappingMode=TextWrappingModes.Normal;t.overflowMode=TextOverflowModes.Truncate;t.raycastTarget=false;Place(go.GetComponent<RectTransform>(),x,y,w,h);return t;
        }
        UnityEngine.UI.Button SmallButton(Transform parent,string label,float x,float y,float w,float h,Action action)
        {
            var rect=Panel(label,parent,new Color(.11f,.2f,.23f,.98f));Place(rect,x,y,w,h);rect.GetComponent<UnityEngine.UI.Image>().raycastTarget=true;
            var b=rect.gameObject.AddComponent<UnityEngine.UI.Button>();var colors=b.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(.7f,.9f,.9f);colors.selectedColor=new Color(.6f,.85f,.86f);colors.pressedColor=accent;b.colors=colors;b.onClick.AddListener(()=>action());
            var t=Text("Label",rect,label,14,4,w-28,h-8,18,Color.white);t.alignment=TextAlignmentOptions.MidlineLeft;
            var nav=b.navigation;nav.mode=UnityEngine.UI.Navigation.Mode.Automatic;b.navigation=nav;buttons.Add(b);return b;
        }
        float row;
        void Button(string label,Action action){SmallButton(menu,label,28,row,548,42,action);row+=50;}
        void Paragraph(string value,float height=65){Text("Paragraph",menu,value,28,row,548,height,19,new Color(.8f,.86f,.86f));row+=height+10;}
        public void Show(string screen)
        {
            if(screen=="options"||screen=="controls"||screen=="credits")if(previous!="options"&&previous!="controls"&&previous!="credits")menuBack=previous=="pause"?"pause":"title";
            previous=screen;var oldMenu=menuRoot;buttons.RemoveAll(b=>b==null||(oldMenu!=null&&b.transform.IsChildOf(oldMenu)));if(oldMenu!=null)Destroy(oldMenu.gameObject);menuRoot=null;hud.gameObject.SetActive(screen=="play");
            if(screen=="play"){menu=null;EventSystem.current?.SetSelectedGameObject(null);return;}
            var shade=Panel("MenuShade",root,new Color(.008f,.025f,.03f,.38f));Stretch(shade);menuRoot=shade;menu=shade;
            var box=Panel("MenuCard",shade,new Color(.025f,.065f,.075f,.97f));Place(box,54,44,604,812);menu=box;
            Text("Eyebrow",menu,screen=="title"?game.Profile.genre.Replace('-',' ').ToUpperInvariant():screen.ToUpperInvariant(),28,23,548,24,15,accent,FontStyles.Bold);
            Text("GameTitle",menu,game.Profile.title,28,62,548,105,42,Color.white,FontStyles.Bold);row=178;
            if(screen=="title")Title();else if(screen=="story")Story();else if(screen=="pause")Pause();else if(screen=="result")Result();else if(screen=="options")Options();else if(screen=="controls")Controls();else if(screen=="credits")Credits();else if(screen=="journal")Journal();else if(screen=="actions")Actions();else if(screen=="lan"||screen=="connecting"||screen=="lobby")Lan(screen);else if(screen=="new")ConfirmNew();else if(screen=="rooms")Rooms();
            Text("Notice",menu,game.Notice,28,742,548,52,16,accent);
            var first=buttons.FirstOrDefault(b=>b!=null&&b.transform.IsChildOf(menu));if(first!=null)EventSystem.current?.SetSelectedGameObject(first.gameObject);
        }
        void Title()
        {
            Paragraph(game.Profile.brief,92);Button(game.Progress.completedMask==0?"Begin campaign":"Continue campaign",game.StartSolo);Button("Chapter "+game.ChapterChoice+" / "+game.Progress.unlocked+" unlocked",()=>{game.ChapterChoice=game.ChapterChoice%game.Progress.unlocked+1;Show("title");});Button("Host or join LAN",()=>game.Show("lan"));Button("Settings",()=>game.Show("options"));Button("Controls",()=>game.Show("controls"));Button("Credits",()=>game.Show("credits"));Button("New campaign",()=>game.Show("new"));Button("Quit",game.Quit);
        }
        ChapterInfo CurrentChapter(){return game.Profile.chapters[Mathf.Clamp((game.State?.chapter??game.ChapterChoice)-1,0,2)];}
        void Story(){var chapter=CurrentChapter();Paragraph("CHAPTER "+chapter.number+" / "+chapter.title,55);Paragraph(chapter.story,145);Paragraph(game.State?.objective??game.Profile.brief,100);Button("Enter chapter",game.BeginChapter);Button("Return to title",game.BackToTitle);}
        void Pause(){Paragraph(game.IsNetworked?"Your LAN party keeps playing while this menu is open.":"Campaign paused. Your unfinished chapter stays in memory until you leave.",86);Button("Resume",game.Resume);Button("Build, craft and interaction choices",()=>game.Show("actions"));Button("Story and field guide",()=>game.Show("journal"));Button("Settings",()=>game.Show("options"));Button("Controls",()=>game.Show("controls"));Button("Save chapter unlocks and return to title",game.BackToTitle);}
        void Result(){bool won=game.State.phase=="won";Paragraph(won?"CHAPTER COMPLETE":"EXPEDITION LOST",50);Paragraph(game.State.message,118);Paragraph("Score "+game.State.score+"  /  Chapter "+game.State.chapter+" of 3",45);if(game.IsClient){Button("Disconnect and return to title",game.BackToTitle);Paragraph("Your host chooses the next chapter.",65);}else{if(won&&game.State.chapter<3)Button("Read the next chapter",game.Advance);else if(won)Paragraph("The campaign is complete. Revisit any unlocked chapter from the title screen.",88);Button("Retry this chapter",game.Retry);Button("Return to title",game.BackToTitle);}}
        void ConfirmNew(){Paragraph("Reset chapter unlocks and completed chapters? Audio, display and bindings stay as you set them.",112);Button("Keep my campaign",()=>game.Show("title"));Button("Confirm new campaign",game.NewCampaign);}
        void Options()
        {
            Paragraph("Audio, display and controls",45);Button("Master volume: "+Mathf.RoundToInt(game.Progress.volume*100)+"%",()=>{game.Progress.volume=game.Progress.volume>=.99f?0:Mathf.Min(1,game.Progress.volume+.1f);game.ApplySettings();game.SaveSettings();Show("options");});
            Button("Quality: "+new[]{"Low","Medium","High"}[game.Progress.quality],()=>{game.Progress.quality=(game.Progress.quality+1)%3;game.ApplySettings();game.SaveSettings();Show("options");});Button(game.Progress.fullScreen?"Display: borderless fullscreen":"Display: windowed",()=>{game.Progress.fullScreen=!game.Progress.fullScreen;game.ApplySettings();game.SaveSettings();Show("options");});
            foreach(string action in new[]{"Interact","Attack","Build","Craft"}){string name=action;Button("Rebind "+name+": "+game.Gameplay.FindAction(name).GetBindingDisplayString(0),()=>game.Rebind(name));}
            Button("Restore default bindings",game.RestoreBindings);Button("Back",()=>game.Show(menuBack));
        }
        void Controls(){Paragraph("MOVE: WASD / left stick\nAIM: mouse / right stick\nINTERACT: E / A (south)\nATTACK: left mouse / right trigger / X (west)\nBUILD: B / left bumper\nCRAFT: Q / right bumper\nPAUSE: Escape / Start\nJOURNAL: J / Select\nMENUS: arrows, D-pad or left stick; Enter / A confirms. B (east) goes back.",330);Paragraph("Choose structures, recipes and shop stock in the Actions menu. LAN discovery and the IP keypad can be used with a controller.",95);Button("Back",()=>game.Show(menuBack));}
        void Credits(){Paragraph("Original game design, characters, story and source\nMayo Games",95);Paragraph("Original stylized character and environment kit. Selected purchased assets are credited in each project's asset manifest. Existing purchases remain private source dependencies.",130);Paragraph("Liberation Sans / SIL Open Font License\nUnity runtime and Input System\nLAN host authority and local progression",100);Button("Back",()=>game.Show(menuBack));}
        void Journal(){Paragraph(CurrentChapter().title+"\n"+CurrentChapter().story,132);Paragraph("YOUR CREW\n"+string.Join("\n",game.Profile.cast.Select(c=>c.name+" - "+c.role)),118);Paragraph(game.State?.objective??game.Profile.brief,100);Paragraph(Guide(),115);Button("Return to play",game.Resume);}
        string Guide()
        {
            switch(game.Profile.id){case "market-day":return "Buy stock at the supplier, deliver matching crates to shelves, then meet shoppers at checkout. Actions selects groceries, tools or produce.";case "hearth-rush":return "Pantry -> chopping board (3 seconds) -> pan (6 seconds) -> serving counter -> sink. Collect cooked dishes before they burn at 16 seconds.";case "dead-orbit":return "Seal airlocks to slow oxygen loss. Collect emergency canisters and repair breaches before the uplink and escape pod.";case "skyhold-siege":return "Interact with an existing tower to add levels. Spend wood and metal carefully; protect the sky-core through every wave.";case "gatebreakers-guild":return "Choose spike traps, barricades and bolt towers in Actions. Protect the portal and fight alongside your traps.";case "emberwake-rebuilders":return "Clear the dungeon gate, recover relics and place homes near the hearth. Return to the council once the village is repaired.";case "fjordbound":return "Take the longship to the raid shore, bank plunder at the harbour and upgrade the longhouse before claiming victory.";case "stormline-express":return "Repair rails, radio and boiler, rescue passengers, then start the engine. Actions selects fuel; the steam valve lowers pressure.";default:return "Stay close to interact. Loot supplies, clear threats and complete the marked objectives. Craft bandages, ammunition and upgrades in Actions.";}
        }
        void Actions()
        {
            Paragraph("Choose what B/LB, Q/RB or your next interaction does. Return to play to use it near a valid target.",80);
            string[] builds=game.Profile.id=="skyhold-siege"?new[]{"tower","barricade"}:game.Profile.id=="gatebreakers-guild"?new[]{"spike-trap","barricade","bolt-tower"}:game.Profile.id=="rift-runners"?new[]{"barrier","ramp"}:game.Profile.id=="havenfall-coalition"?new[]{"barricade","clinic"}:game.Profile.id=="emberwake-rebuilders"?new[]{"home"}:new string[0];
            foreach(string choice in builds){string option=choice;Button("Build: "+Friendly(option),()=>game.SelectBuild(option));}
            if(game.Profile.id=="market-day")foreach(string s in new[]{"groceries","tools","produce"}){string option=s;Button("Supplier stock: "+option,()=>game.SelectInteraction(option));}
            else if(game.Profile.id=="hearth-rush")foreach(string s in new[]{"stew","fish","bread"}){string option=s;Button("Pantry recipe: "+option,()=>game.SelectInteraction(option));}
            else if(game.Profile.id=="stormline-express"){Button("Engine: load wood as fuel",()=>game.SelectInteraction("fuel"));Button("Engine: start / stop",()=>game.SelectInteraction(""));}
            if(game.Profile.id!="market-day"&&game.Profile.id!="hearth-rush")foreach(string s in new[]{"bandage","ammo","upgrade"}){string option=s;Button("Craft: "+option,()=>game.SelectCraft(option));}
            Button("Return to play",game.Resume);
        }
        void Lan(string screen)
        {
            if(screen=="lobby"){Paragraph(game.ConnectionLabel,50);Paragraph(CurrentChapter().story,125);Paragraph("Everyone shares this chapter's objectives, supplies and threats. The host starts the expedition; leaving a menu during LAN play does not pause teammates.",130);if(!game.IsClient)Button("Begin expedition",game.BeginChapter);Button("Leave LAN",game.BackToTitle);return;}
            if(screen=="connecting"){Paragraph("Connecting to "+game.HostAddress+":"+game.Port+"…",100);Button("Cancel",game.BackToTitle);return;}
            Paragraph("1-4 players on the same LAN. Host authority keeps shared objectives and resource spending consistent.",90);Button("Host LAN campaign",game.Host);Button("Find nearby hosts",()=>{game.Notice="Searching for hosts…";game.ScanRooms(rooms=>{foundRooms=rooms;game.Show("rooms");});});
            Paragraph("Host IPv4: "+game.HostAddress,40);
            float top=row;string[] keys={"1","2","3","4","5","6","7","8","9",".","0","Delete"};for(int i=0;i<keys.Length;i++){string key=keys[i];SmallButton(menu,key,28+(i%6)*92,top+(i/6)*44,84,38,()=>{if(key=="Delete")game.HostAddress=game.HostAddress.Length>0?game.HostAddress.Substring(0,game.HostAddress.Length-1):"";else if(game.HostAddress.Length<15)game.HostAddress+=key;Show("lan");var selected=buttons.LastOrDefault(b=>b!=null&&b.gameObject.name==key);if(selected!=null)EventSystem.current.SetSelectedGameObject(selected.gameObject);});}row+=96;
            Button("Clear address",()=>{game.HostAddress="";Show("lan");});Button("Join this host",game.Join);Button("Back",game.BackToTitle);
        }
        void Rooms(){Paragraph(foundRooms.Length==0?"No hosts answered discovery. Direct-address joining is still available.":"Nearby hosts for "+game.Profile.title,90);foreach(var room in foundRooms.Take(6)){var selected=room;Button(selected.Name+" / "+selected.Address,()=>{game.HostAddress=selected.Address;game.Join();});}Button("Join by address / Back",()=>game.Show("lan"));}
        static string Friendly(string value){return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('-',' '));}
        public void RefreshHUD()
        {
            if(game.State==null)return;var state=game.State;var player=state.players.Find(p=>p.id==game.LocalPlayerId);
            hudTitle.text=game.Profile.title+" / "+CurrentChapter().title;hudObjective.text=state.objective;hudNetwork.text=game.ConnectionLabel;
            if(player==null){hudVitals.text="Waiting for player…";return;}
            healthFill.rectTransform.anchorMax=new Vector2(Mathf.Clamp01(player.hp/player.maxHp),1);hudVitals.text="HP "+Mathf.CeilToInt(player.hp)+(game.Profile.id=="dead-orbit"?"   O2 "+Mathf.CeilToInt(player.oxygen):"   Base "+Mathf.CeilToInt(state.baseHealth));
            hudResources.text="Food "+player.food+"   Timber "+player.wood+"   Metal "+player.metal+"   Coins "+player.coins+"   Ammo "+player.ammo+(player.carried!=""?"   Carrying "+player.carried.Replace(':',' '):"");
            hudMessage.text=(string.IsNullOrEmpty(game.Notice)?state.message:game.Notice)+"\nE / A interact   |   LMB / RT attack   |   B / LB build   |   Q / RB craft";
            string nearest="";float distance=2.5f;
            foreach(var task in state.tasks)if(task.status=="open"){float d=Simulation.Distance(player.x,player.z,task.x,task.z);if(d<distance){distance=d;nearest=task.label+(task.kind=="order"?" / "+Mathf.CeilToInt(task.remaining)+"s":"");}}
            foreach(var structure in state.structures)if(structure.hp>0){float d=Simulation.Distance(player.x,player.z,structure.x,structure.z);if(d<distance){distance=d;nearest=Friendly(structure.kind)+(structure.stock>0?" / stock "+structure.stock:"")+(structure.item!=""?" / "+structure.item+" "+structure.progress.ToString("0.0")+"s":"");}}
            foreach(var pickup in state.pickups)if(pickup.active){float d=Simulation.Distance(player.x,player.z,pickup.x,pickup.z);if(d<distance){distance=d;nearest="Collect "+pickup.amount+" "+pickup.kind;}}
            hudContext.text=nearest==""?"Progress "+state.progress+" / "+state.goal+"   |   "+Mathf.CeilToInt(state.remaining)+"s remaining":"E / A: "+nearest;
        }
        public void Back(){if(previous=="options"||previous=="controls"||previous=="credits")game.Show(menuBack);else if(previous=="pause"||previous=="journal"||previous=="actions")game.Resume();else if(previous=="rooms")game.Show("lan");else game.BackToTitle();}
        public int ActiveMenuButtonCount {get{return buttons.Count(b=>b!=null&&menu!=null&&b.transform.IsChildOf(menu));}}
    }
}
