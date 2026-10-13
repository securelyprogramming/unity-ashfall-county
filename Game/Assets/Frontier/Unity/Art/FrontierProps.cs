using UnityEngine;

namespace Mayo.Frontier.Presentation
{
    public sealed partial class FrontierWorldView
    {
        private void BuildItem(Transform root,string kind,float size)
        {
            if(kind=="wood")
            {
                for(int i=0;i<3;i++)P(root,"cylinder",(i-1)*.17f,.17f+(i==1?.12f:0),-.34f,.22f,.7f,.22f,wood,new Vector3(90,0,0));
                P(root,"box",0,.17f,0,.65f,.08f,.15f,copper);
            }
            else if(kind=="metal") { P(root,"bevel",0,.16f,0,.65f,.28f,.48f,metal);P(root,"box",0,.32f,0,.52f,.08f,.36f,cream); }
            else if(kind=="ammo") { P(root,"bevel",0,.24f,0,.58f,.46f,.4f,new Color(.28f,.36f,.22f));P(root,"box",0,.34f,.21f,.36f,.09f,.02f,cream);P(root,"box",0,.5f,0,.26f,.04f,.05f,ink); }
            else if(kind=="oxygen") { P(root,"cylinder",0,.12f,0,.4f,.7f,.4f,cream);P(root,"round",0,.82f,0,.4f,.2f,.4f,cream);P(root,"box",0,.91f,0,.2f,.11f,.1f,accent);P(root,"cylinder",0,.41f,0,.415f,.14f,.415f,accent); }
            else if(kind=="herbs")
            {
                P(root,"bevel",0,.12f,0,.42f,.25f,.4f,wood);
                for(int i=0;i<5;i++){float a=i*Mathf.PI*2/5;Beam(root,new Vector3(0,.2f,0),new Vector3(Mathf.Cos(a)*.25f,.65f,Mathf.Sin(a)*.25f),.035f,leaf);P(root,"round",Mathf.Cos(a)*.22f,.6f,Mathf.Sin(a)*.22f,.24f,.13f,.35f,signal,new Vector3(0,-a*Mathf.Rad2Deg,30));}
            }
            else if(kind=="fish") { P(root,"round",0,.15f,0,.28f,.22f,.66f,new Color(.44f,.63f,.65f));P(root,"roof",0,.16f,-.35f,.29f,.22f,.18f,new Color(.34f,.48f,.55f),new Vector3(90,0,0));P(root,"round",-.09f,.2f,.19f,.055f,.055f,.055f,ink); }
            else if(kind=="stew")
            {
                P(root,"cylinder",0,.04f,0,.62f,.25f,.62f,cream);P(root,"cylinder",0,.29f,0,.55f,.025f,.55f,new Color(.67f,.32f,.12f));
                for(int i=0;i<4;i++)P(root,"bevel",Mathf.Sin(i*7)*.15f,.32f,Mathf.Cos(i*7)*.15f,.1f,.06f,.1f,i%2==0?leaf:cream);
            }
            else if(kind=="burnt")
            {
                P(root,"bevel",0,.12f,0,.5f,.24f,.5f,ink);P(root,"round",-.13f,.42f,0,.2f,.34f,.2f,new Color(.18f,.18f,.17f));P(root,"round",.08f,.69f,0,.24f,.27f,.24f,new Color(.23f,.23f,.21f));
            }
            else if(kind=="bread") { P(root,"round",0,.2f,0,.67f,.35f,.42f,new Color(.83f,.58f,.29f));for(int i=-1;i<=1;i++)P(root,"box",i*.15f,.36f,0,.04f,.018f,.24f,cream,new Vector3(0,18,0)); }
            else if(kind=="relic") { P(root,"cylinder",0,.05f,0,.52f,.13f,.52f,copper);P(root,"crystal",0,.48f,0,.45f,.6f,.45f,accent,glow:true); }
            else
            {
                P(root,"bevel",0,.18f,0,.65f,.32f,.45f,wood);
                for(int i=-1;i<=1;i++) { P(root,"round",i*.18f,.43f,0,.23f,.25f,.24f,i==0?new Color(.75f,.25f,.12f):new Color(.72f,.63f,.24f));P(root,"box",i*.18f,.56f,0,.025f,.07f,.025f,leaf); }
            }
        }
        private void Crate(Transform root,Vector3 position,float scale,bool metalBox=false)
        {
            Transform g=Group(root,"Braced supply crate");g.localPosition=position;g.localScale=Vector3.one*scale;
            P(g,"bevel",0,.44f,0,.9f,.84f,.8f,metalBox?metal:wood);P(g,"box",0,.8f,0,.95f,.12f,.85f,metalBox?cream:copper);
            P(g,"box",-.3f,.43f,.41f,.08f,.8f,.04f,cream);P(g,"box",.3f,.43f,.41f,.08f,.8f,.04f,cream);
            P(g,"box",0,.43f,.43f,.1f,.9f,.035f,cream,new Vector3(0,0,40));P(g,"box",0,.51f,-.42f,.4f,.1f,.04f,ink);
        }
        private void Barrel(Transform root,Vector3 position,Color color,float scale=1)
        {
            Transform g=Group(root,"Banded barrel");g.localPosition=position;g.localScale=Vector3.one*scale;
            P(g,"cylinder",0,0,0,.78f,1.1f,.78f,color);P(g,"cylinder",0,.12f,0,.81f,.1f,.81f,metal);P(g,"cylinder",0,.85f,0,.81f,.1f,.81f,metal);P(g,"cylinder",0,1.1f,0,.75f,.045f,.75f,wood);
        }
        private void Lantern(Transform root,Vector3 position,Color glow)
        {
            Transform g=Group(root,"Hooded lantern");g.localPosition=position;P(g,"bevel",0,0,0,.2f,.33f,.2f,glow,glow:true);P(g,"cone",0,.17f,0,.35f,.2f,.35f,ink);P(g,"box",0,-.19f,0,.31f,.07f,.31f,ink);
            for(int i=-1;i<=1;i+=2)P(g,"box",i*.13f,0,0,.03f,.36f,.03f,metal);
        }
        private void Bench(Transform root,Vector3 position,float width,Color color)
        {
            Transform g=Group(root,"Crafted bench");g.localPosition=position;P(g,"box",0,.65f,0,width,.14f,.65f,color);
            for(int i=-1;i<=1;i+=2)P(g,"box",i*(width*.35f),.32f,0,.15f,.65f,.46f,wood);
        }
        private void BuildStructure(Visual v,string kind)
        {
            Transform g=v.model;
            if(kind=="safehouse" || kind=="community" || kind=="clinic" || kind=="home" || kind=="longhouse")
            {
                House(g,Vector3.zero,kind=="longhouse"?new Vector3(3.5f,2.6f,2.4f):new Vector3(2.8f,2.2f,2.2f),kind=="clinic"?cream:wood,kind=="home"?accent:Color.Lerp(accent,ink,.35f),kind=="longhouse");
                if(kind=="clinic") { P(g,"box",0,1.4f,-1.16f,.75f,.2f,.05f,danger);P(g,"box",0,1.4f,-1.17f,.2f,.75f,.06f,danger); }
                v.tiers=Group(g,"Visible upgrades");
            }
            else if(kind=="tower" || kind=="bolt-tower") { v.tiers=Group(g,"Stacked defensive levels");TowerLevels(v,1); }
            else if(kind=="portal" || kind=="sky-core")
            {
                P(g,"hex",0,0,0,2.6f,.3f,2.6f,stone);P(g,"hex",0,.3f,0,1.7f,.2f,1.7f,copper);
                for(int i=0;i<4;i++){float a=i*Mathf.PI/2;P(g,"trunk",Mathf.Cos(a)*1.08f,.2f,Mathf.Sin(a)*1.08f,.35f,2.3f,.35f,stone);P(g,"crystal",Mathf.Cos(a)*1.08f,2.6f,Mathf.Sin(a)*1.08f,.25f,.45f,.25f,accent,glow:true);}
                v.movingPart=P(g,"crystal",0,1.5f,0,.8f,1.5f,.8f,accent,glow:true);
                P(g,"ring",0,1.4f,0,1.8f,1,1.8f,cream,new Vector3(55,0,0),true);
            }
            else if(kind=="hearth")
            {
                P(g,"cylinder",0,0,0,1.75f,.2f,1.75f,stone);for(int i=0;i<6;i++){float a=i*Mathf.PI/3;P(g,"bevel",Mathf.Cos(a)*.7f,.27f,Mathf.Sin(a)*.7f,.43f,.4f,.39f,stone,new Vector3(0,-a*Mathf.Rad2Deg,0));}
                P(g,"cylinder",0,.2f,-.4f,.18f,.9f,.18f,wood,new Vector3(90,0,25));v.movingPart=P(g,"crystal",0,.72f,0,.65f,.9f,.65f,new Color(.94f,.46f,.13f),glow:true);
            }
            else if(kind=="longship") Longship(g);
            else if(kind=="train-engine")
            {
                Train(g,false);v.movingPart=Group(g,"Boiler steam");v.effects=new Transform[4];
                for(int i=0;i<v.effects.Length;i++)v.effects[i]=P(v.movingPart,"round",0,2.9f+i*.35f,1.3f,.3f,.3f,.3f,cream);
            }
            else if(kind=="cargo") Train(g,true);
            else if(kind=="steam-valve")
            {
                P(g,"cylinder",0,0,0,.38f,1.1f,.38f,metal);v.movingPart=P(g,"ring",0,1.12f,0,.85f,1,.85f,copper);P(g,"box",0,1.14f,0,.7f,.08f,.08f,copper);P(g,"box",0,1.14f,0,.08f,.08f,.7f,copper);
            }
            else if(kind=="airlock" || kind=="dungeon-gate")
            {
                for(int i=-1;i<=1;i+=2) { P(g,"bevel",i*1.15f,1.6f,0,.36f,3.2f,.5f,stone);P(g,"box",i*1.15f,.25f,0,.6f,.5f,.75f,metal); }
                P(g,"bevel",0,3.1f,0,2.8f,.35f,.55f,stone);
                v.movingPart=Group(g,"Gate leaves");P(v.movingPart,"bevel",0,1.45f,0,2.1f,2.8f,.21f,kind=="airlock"?metal:wood);
                for(int i=-1;i<=1;i++)P(v.movingPart,"box",i*.58f,1.45f,-.14f,.08f,2.4f,.05f,copper);
            }
            else if(kind=="spike-trap")
            {
                P(g,"bevel",0,.13f,0,2.15f,.25f,2.15f,wood);v.movingPart=Group(g,"Retractable spikes");
                for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)P(v.movingPart,"cone",x*.58f,.24f,z*.58f,.23f,.76f,.23f,metal);
            }
            else if(kind=="barrier" || kind=="barricade")
            {
                for(int i=-1;i<=1;i+=2)P(g,"box",i*.85f,.67f,0,.25f,1.35f,.3f,wood);
                for(int i=0;i<3;i++)P(g,"box",0,.32f+i*.36f,0,2.1f,.24f,.18f,wood,new Vector3(0,0,(i-1)*6));
                P(g,"box",0,.7f,-.13f,.14f,1.5f,.06f,copper,new Vector3(0,0,50));
            }
            else if(kind=="ramp")
            {
                P(g,"ramp",0,0,0,2.2f,1.35f,2.8f,wood);for(int i=0;i<8;i++)P(g,"box",0,.1f+i*.16f,-1.25f+i*.35f,2.25f,.1f,.14f,copper);
            }
            else if(kind=="rubble") { for(int i=0;i<5;i++)P(g,"bevel",Mathf.Sin(i*13)*.65f,.3f+(i%2)*.35f,Mathf.Cos(i*17)*.6f,.7f,.65f,.7f,stone,new Vector3(i*13,i*37,i*11)); }
            else if(kind=="oxygen-station")
            {
                P(g,"bevel",0,.85f,0,1.5f,1.7f,.72f,cream);P(g,"bevel",0,1.3f,-.39f,.75f,.45f,.08f,ink);P(g,"box",0,1.3f,-.44f,.55f,.12f,.025f,signal,glow:true);
                for(int i=-1;i<=1;i+=2)P(g,"cylinder",i*.55f,.15f,-.1f,.31f,1.1f,.31f,accent);
            }
            else if(kind.StartsWith("shelf-")) { Shelf(g,kind.Substring(6));v.stockRoot=Group(g,"Available stock"); }
            else if(kind=="supplier")
            {
                Crate(g,new Vector3(-.65f,0,0),1.2f);Crate(g,new Vector3(.6f,0,.2f),1);Crate(g,new Vector3(-.65f,1.05f,0),.9f);P(g,"box",.7f,.6f,-.5f,.6f,.5f,.05f,cream);
            }
            else if(kind=="checkout")
            {
                Counter(g,cream,2.7f);P(g,"bevel",.6f,1.4f,.08f,.55f,.3f,.45f,ink);P(g,"box",.6f,1.65f,.14f,.42f,.28f,.06f,accent,new Vector3(15,0,0),true);P(g,"box",-.5f,1.15f,0,1.1f,.04f,.7f,ink);
            }
            else if(kind=="pantry")
            {
                Shelf(g,"food");v.stockRoot=Group(g,"Pantry ingredients");for(int i=-1;i<=1;i++){Transform p=Group(v.stockRoot,"Ingredient basket");p.localPosition=new Vector3(i*.55f,.85f,0);p.localScale=Vector3.one*.6f;BuildItem(p,i==0?"fish":i<0?"bread":"food",1);}
            }
            else if(kind=="pan" || kind=="sink" || kind=="chopping-board" || kind=="serving-counter")
            {
                Counter(g,kind=="serving-counter"?wood:cream,2.1f);
                if(kind=="pan") { P(g,"cylinder",0,1.16f,0,1.1f,.09f,1.1f,ink);P(g,"cylinder",0,1.25f,0,.85f,.12f,.85f,metal);P(g,"box",.7f,1.27f,0,.65f,.08f,.11f,ink);v.statusItem=Group(g,"Cooking contents"); }
                else if(kind=="sink") { P(g,"bevel",0,1.16f,0,1.5f,.08f,.7f,metal);P(g,"bevel",0,1.2f,0,1.17f,.025f,.52f,new Color(.19f,.36f,.42f));P(g,"cylinder",.6f,1.2f,.27f,.08f,.65f,.08f,metal);P(g,"cylinder",.6f,1.83f,.27f,.08f,.42f,.08f,metal,new Vector3(-90,0,0)); }
                else if(kind=="chopping-board") { P(g,"bevel",0,1.18f,0,1.4f,.1f,.7f,wood);P(g,"box",.36f,1.27f,0,.04f,.09f,.5f,metal,new Vector3(0,25,0));v.statusItem=Group(g,"Preparation contents"); }
                else { P(g,"bevel",0,1.15f,0,1.5f,.06f,.65f,cream);P(g,"cylinder",.7f,1.19f,.1f,.25f,.22f,.25f,copper); }
            }
            else Crate(g,Vector3.zero,1.2f);
        }
        private void Counter(Transform g,Color color,float width)
        {
            P(g,"bevel",0,.53f,0,width,1.05f,1.1f,color);P(g,"bevel",0,1.09f,0,width+.12f,.14f,1.22f,stone);
            for(int i=-1;i<=1;i+=2) { P(g,"box",i*width*.25f,.57f,-.57f,width*.43f,.75f,.04f,Color.Lerp(color,wood,.18f));P(g,"box",i*width*.25f,.8f,-.61f,.3f,.04f,.04f,metal); }
        }
        private void Shelf(Transform g,string goods)
        {
            Color trim=goods=="produce"?leaf:goods=="tools"?metal:wood;
            for(int i=-1;i<=1;i+=2)P(g,"box",i*1.1f,1.23f,.36f,.15f,2.45f,.15f,trim);
            P(g,"box",0,1.15f,.43f,2.35f,2.3f,.13f,Color.Lerp(trim,cream,.3f));
            for(int i=0;i<3;i++)P(g,"box",0,.25f+i*.75f,0,2.4f,.12f,1.05f,cream);
            P(g,"box",0,2.43f,.36f,2.45f,.26f,.2f,accent);
        }
        private void TowerLevels(Visual v,int levels)
        {
            if(v.tiers!=null)Destroy(v.tiers.gameObject);v.tiers=Group(v.model,"Stacked defensive levels");
            int visible=Mathf.Clamp(levels,1,7);
            for(int level=0;level<visible;level++)
            {
                float y=level*1.45f;
                for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2) { P(v.tiers,"box",x*.65f,y+.72f,z*.65f,.2f,1.5f,.2f,wood);Footing(v.tiers,new Vector3(x*.65f,y,z*.65f),.36f); }
                P(v.tiers,"box",0,y+1.45f,0,1.7f,.16f,1.7f,wood);Beam(v.tiers,new Vector3(-.65f,y,.68f),new Vector3(.65f,y+1.4f,.68f),.1f,copper);
                for(int i=0;i<5;i++)P(v.tiers,"box",-.91f,y+.2f+i*.26f,0,.13f,.08f,.7f,cream);
            }
            float top=visible*1.45f;
            for(int i=-1;i<=1;i+=2)P(v.tiers,"box",i*.8f,top+.38f,0,.1f,.7f,1.7f,wood);
            v.movingPart=Group(v.tiers,"Defensive bolt launcher");v.movingPart.localPosition=new Vector3(0,top+.37f,0);P(v.movingPart,"bevel",0,0,0,.4f,.3f,.85f,metal);P(v.movingPart,"box",0,.07f,.17f,1.1f,.1f,.15f,copper);P(v.movingPart,"cone",0,.07f,.68f,.1f,.4f,.1f,cream,new Vector3(90,0,0));
            v.muzzle=P(v.movingPart,"crystal",0,.07f,.95f,.16f,.3f,.16f,cream,new Vector3(90,0,0),true);v.muzzle.gameObject.SetActive(false);
            v.level=levels;
        }
        private void UpdateStructure(Visual v,Mayo.Frontier.Structure s,float clock,Mayo.Frontier.World state)
        {
            if((s.kind=="tower" || s.kind=="bolt-tower") && s.level!=v.level)TowerLevels(v,s.level);
            if(s.kind=="longhouse" && s.level!=v.level)
            {
                if(v.tiers!=null)Destroy(v.tiers.gameObject);v.tiers=Group(v.model,"Longhouse upgrade banners");
                for(int i=0;i<Mathf.Min(s.level,6);i++)P(v.tiers,"box",-.95f+i*.38f,2.05f,-1.27f,.24f,.54f,.05f,accent);v.level=s.level;
            }
            if(v.movingPart!=null)
            {
                if(s.kind=="train-engine")
                {
                    v.movingPart.gameObject.SetActive(s.active);
                    for(int i=0;i<v.effects.Length;i++){float phase=Mathf.Repeat(clock*.8f+i*.4f,1.7f);v.effects[i].localPosition=new Vector3(phase*.12f,2.8f+phase,1.3f);v.effects[i].localScale=Vector3.one*(.22f+phase*.3f)*Mathf.Clamp(state.pressure/45,.4f,1.5f);}
                }
                else if(s.kind=="dungeon-gate" || s.kind=="airlock")v.movingPart.localScale=new Vector3(1,s.active?1:.075f,1);
                else if(s.kind=="spike-trap")v.movingPart.localScale=new Vector3(1,s.cooldown>1.7f?1.12f:.65f,1);
                else if(s.kind=="steam-valve")v.movingPart.localRotation=Quaternion.Euler(0,state.pressure*3,0);
                else if(s.kind=="portal" || s.kind=="sky-core")v.movingPart.localRotation=Quaternion.Euler(0,clock*24,0);
                else if(s.kind=="hearth")v.movingPart.localScale=new Vector3(.65f,.82f+Mathf.Sin(clock*7)*.08f,.65f);
                else if(s.kind=="tower" || s.kind=="bolt-tower")
                {
                    float nearest=10000;Vector3 target=Vector3.zero;bool found=false;
                    foreach(Mayo.Frontier.Enemy e in state.enemies){if(!e.alive || e.kind=="customer")continue;float d=(e.x-s.x)*(e.x-s.x)+(e.z-s.z)*(e.z-s.z);if(d<nearest){nearest=d;target=new Vector3(e.x-s.x,0,e.z-s.z);found=true;}}
                    if(found && target.sqrMagnitude>.01f)v.movingPart.localRotation=Quaternion.LookRotation(target);
                    if(v.muzzle!=null)v.muzzle.gameObject.SetActive(s.cooldown>.72f);
                }
            }
            if(s.kind.StartsWith("shelf-") && v.stock!=s.stock)
            {
                if(v.stockRoot!=null)Destroy(v.stockRoot.gameObject);v.stockRoot=Group(v.model,"Available "+s.kind+" stock");
                for(int i=0;i<Mathf.Min(s.stock,12);i++){Transform item=Group(v.stockRoot,"Stock item");item.localPosition=new Vector3((i%4-1.5f)*.48f,.33f+(i/4)*.75f,0);item.localScale=Vector3.one*.48f;BuildItem(item,s.kind=="shelf-tools"?"metal":"food",1);}v.stock=s.stock;
            }
            if((s.kind=="pan" || s.kind=="chopping-board") && v.stockKind!=s.item)
            {
                if(v.statusItem!=null)Destroy(v.statusItem.gameObject);v.statusItem=Group(v.model,"Station contents");v.statusItem.localPosition=new Vector3(0,1.3f,0);v.statusItem.localScale=Vector3.one*.58f;
                if(!string.IsNullOrEmpty(s.item))BuildItem(v.statusItem,s.item.Contains("burnt")?"burnt":s.item.Contains("fish")?"fish":s.item.Contains("bread")?"bread":"stew",1);v.stockKind=s.item;
            }
            Color status=s.hp<s.maxHp*.3f?danger:accent;
            if(s.kind=="pan" && !string.IsNullOrEmpty(s.item))status=s.item.StartsWith("burnt:")?danger:s.progress>=6?signal:accent;
            if(s.kind.StartsWith("shelf-") && s.stock==0)status=danger;
            v.ringRenderer.sharedMaterial=kit.Material(status,true);
        }
        private void BuildTask(Visual v,string kind,int id)
        {
            Transform g=v.model;
            if(kind=="survivor" || kind=="resident" || kind=="passenger" || kind=="healer" || kind=="elder" || kind=="council" || kind=="hearth-council")BuildCharacter(v,kind=="healer"?"healer":kind=="elder"?"elder":"villager",id);
            else if(kind=="raid-cache" || kind=="supply-crate" || kind=="medicine")
            {
                Crate(g,Vector3.zero,1.2f,kind=="medicine");if(kind=="medicine"){P(g,"box",0,.57f,-.52f,.55f,.12f,.03f,cream);P(g,"box",0,.57f,-.53f,.12f,.55f,.03f,cream);}
            }
            else if(kind=="relic-chest")
            {
                Crate(g,Vector3.zero,1.2f);P(g,"roof",0,1.02f,0,1.2f,.37f,1.05f,copper);P(g,"box",0,.81f,-.54f,.14f,.28f,.05f,cream);P(g,"crystal",0,1.57f,0,.36f,.5f,.36f,accent,glow:true);
            }
            else if(kind=="shrine")
            {
                P(g,"hex",0,0,0,1.7f,.2f,1.7f,stone);P(g,"trunk",0,.2f,0,.8f,1.3f,.8f,stone);P(g,"crystal",0,1.8f,0,.55f,.7f,.55f,signal,glow:true);
                for(int i=0;i<4;i++){float a=i*Mathf.PI/2;P(g,"bevel",Mathf.Cos(a)*.75f,.25f,Mathf.Sin(a)*.75f,.27f,.54f,.3f,stone);}
            }
            else if(kind=="evacuation" || kind=="uplink" || kind=="radio-repair")
            {
                P(g,"bevel",0,.5f,0,.8f,1,1.1f,metal);P(g,"box",0,.65f,-.57f,.55f,.35f,.03f,ink);P(g,"box",0,.65f,-.6f,.4f,.08f,.025f,accent,glow:true);
                P(g,"cylinder",.27f,1.02f,.25f,.07f,2,.07f,metal);P(g,"ring",.27f,2.3f,.25f,.8f,1,.8f,cream,new Vector3(55,0,0));P(g,"round",.27f,3.07f,.25f,.16f,.16f,.16f,danger,glow:true);
            }
            else if(kind=="escape-pod")
            {
                P(g,"bevel",0,.65f,0,2.2f,1.3f,3.1f,cream);P(g,"bevel",0,1.4f,.2f,1.6f,.75f,1.8f,metal);P(g,"bevel",0,1.45f,-.72f,1.2f,.45f,.1f,accent,glow:true);P(g,"cone",0,.9f,1.6f,1.6f,.9f,1.3f,cream,new Vector3(90,0,0));
            }
            else if(kind=="hull-breach")
            {
                P(g,"ring",0,.04f,0,2.1f,1,2.1f,danger,glow:true);P(g,"bevel",-.55f,.15f,0,.3f,.32f,1.5f,metal,new Vector3(0,15,24));v.statusItem=Group(g,"Escaping oxygen");
                for(int i=0;i<3;i++)P(v.statusItem,"crystal",(i-1)*.3f,.65f+i*.25f,0,.15f,.65f,.15f,accent,glow:true);
            }
            else if(kind=="water-pump")
            {
                P(g,"cylinder",0,0,0,1,.5f,1,stone);P(g,"cylinder",0,.5f,0,.3f,1,.3f,metal);P(g,"cylinder",0,1.4f,0,.25f,.8f,.25f,metal,new Vector3(0,0,90));Beam(g,new Vector3(.1f,1.65f,0),new Vector3(.7f,1.85f,0),.1f,wood);
            }
            else if(kind=="thorn-vine")
            {
                for(int i=-2;i<=2;i++){P(g,"trunk",i*.45f,0,Mathf.Sin(i)*.4f,.25f,1.6f,.25f,leaf,new Vector3(i*9,0,i*12));P(g,"cone",i*.45f,1.1f,Mathf.Sin(i)*.4f,.3f,.5f,.3f,wood,new Vector3(0,0,48));}
            }
            else if(kind=="rail-repair")
            {
                for(int i=-1;i<=1;i++){P(g,"box",0,.1f,i*.7f,2.4f,.2f,.25f,wood);P(g,"box",-.76f,.26f,i*.5f,.12f,.12f,.65f,metal,new Vector3(0,i*13,0));P(g,"box",.76f,.26f,i*.5f,.12f,.12f,.65f,metal,new Vector3(0,-i*13,0));}Crate(g,new Vector3(1.5f,0,0),.6f,true);
            }
            else if(kind=="boiler-leak")
            {
                Barrel(g,Vector3.zero,copper);P(g,"crystal",.3f,1.4f,0,.16f,.8f,.16f,cream,glow:true);
            }
            else Crate(g,Vector3.zero,1);
            v.marker=P(v.root,"crystal",0,2.65f,0,.2f,.34f,.2f,accent,glow:true);v.ring.localScale=Vector3.one*2;
        }
        private void Footing(Transform root,Vector3 position,float scale)
        {
            if(licensedFooting!=null)
            {
                GameObject item=Instantiate(licensedFooting,root,false);item.name="Owner's optional Animpic stone footing";item.transform.localPosition=position;item.transform.localScale=Vector3.one*scale;
            }
            else P(root,"bevel",position.x,position.y+.1f,position.z,scale,.2f,scale,stone);
        }
    }
}
