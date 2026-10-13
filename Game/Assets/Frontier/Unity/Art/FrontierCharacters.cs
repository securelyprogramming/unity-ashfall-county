using UnityEngine;

namespace Mayo.Frontier.Presentation
{
    public sealed partial class FrontierWorldView
    {
        private void BuildCharacter(Visual v,string kind,int id)
        {
            bool hero=kind.StartsWith("hero-");
            if(kind=="wolf") { BuildWolf(v);return; }
            if(kind=="treant" || kind=="elder-treant") { BuildTreant(v,kind=="elder-treant");return; }
            Color clothes=hero?accent:kind=="customer"?Color.Lerp(accent,cream,(id%3)*.25f):kind.Contains("orc")?new Color(.34f,.25f,.18f):kind.Contains("zombie") || kind=="brute"?new Color(.32f,.36f,.27f):new Color(.43f,.27f,.2f);
            Color face=kind.Contains("orc")?new Color(.32f,.5f,.24f):kind.Contains("zombie") || kind=="brute"?new Color(.48f,.57f,.41f):skin;
            if(!hero && id%3==1 && !kind.Contains("zombie") && !kind.Contains("orc"))face=new Color(.43f,.28f,.2f);
            Transform rig=v.model;
            v.torso=P(rig,"bevel",0,1.28f,0,.72f,.76f,.46f,clothes);
            P(rig,"bevel",0,.89f,0,.65f,.24f,.43f,ink);P(rig,"box",0,.98f,.24f,.67f,.09f,.07f,copper);
            v.head=Group(rig,"Articulated head");v.head.localPosition=new Vector3(0,1.87f,0);
            P(v.head,"bevel",0,0,0,.46f,.48f,.44f,face);
            P(v.head,"bevel",0,-.025f,.245f,.12f,.14f,.12f,face);
            P(v.head,"box",-.105f,.04f,.226f,.065f,.045f,.025f,ink);P(v.head,"box",.105f,.04f,.226f,.065f,.045f,.025f,ink);
            P(v.head,"box",0,-.11f,.231f,.15f,.035f,.02f,ink);
            v.leftArm=Arm(rig,-1,clothes,face);v.rightArm=Arm(rig,1,clothes,face);
            v.leftLeg=Leg(rig,-1);v.rightLeg=Leg(rig,1);
            if(hero)
            {
                P(rig,"bevel",0,1.32f,-.34f,.55f,.7f,.3f,Color.Lerp(clothes,ink,.4f));
                P(rig,"box",-.25f,1.4f,.25f,.08f,.6f,.055f,cream);P(rig,"box",.25f,1.4f,.25f,.08f,.6f,.055f,cream);
                if(gameId=="dead-orbit") { SpaceSuit(v,clothes); Rifle(v); }
                else if(gameId=="fjordbound") Viking(v);
                else if(gameId=="thornvale") Ranger(v);
                else if(gameId=="emberwake-rebuilders") Armor(v,copper,true);
                else if(gameId=="gatebreakers-guild") { Armor(v,metal,false);Goggles(v.head);Hammer(v.rightArm); }
                else if(gameId=="hearth-rush") Chef(v);
                else if(gameId=="market-day") Shopkeeper(v);
                else if(gameId=="stormline-express") Conductor(v);
                else if(gameId=="skyhold-siege") { HardHat(v.head);ToolBelt(rig);Hammer(v.rightArm); }
                else if(gameId=="ashfall-county") { Medic(v);Rifle(v); }
                else if(gameId=="rift-runners") { Goggles(v.head);Armor(v,metal,false);Rifle(v); }
                else { Cap(v.head,clothes);ToolBelt(rig);Rifle(v); }
            }
            else if(kind.Contains("space")) { SpaceSuit(v,new Color(.44f,.49f,.4f));P(v.head,"box",0,.04f,.315f,.38f,.04f,.03f,danger,glow:true); }
            else if(kind.Contains("orc")) Orc(v,kind.Contains("brute"));
            else if(kind=="sentinel" || kind=="dungeon-guardian" || kind=="ember-lord")
            {
                Armor(v,kind=="ember-lord"?copper:metal,true);
                if(kind!="sentinel") { rig.localScale=Vector3.one*1.45f;P(v.head,"cone",-.2f,.15f,0,.15f,.48f,.15f,copper,new Vector3(0,0,-28));P(v.head,"cone",.2f,.15f,0,.15f,.48f,.15f,copper,new Vector3(0,0,28)); }
            }
            else if(kind=="bot") { Armor(v,new Color(.37f,.31f,.48f),false);Goggles(v.head);Rifle(v); }
            else if(kind.Contains("raider")) { Bandanna(v.head);if(kind.Contains("archer"))Bow(v.rightArm);else Axe(v.rightArm); }
            else if(kind=="customer") { Cap(v.head,clothes);ShoppingBasket(v.leftArm); }
            else if(kind=="healer") Medic(v);
            else if(kind=="elder") Ranger(v);
            else if(kind=="villager") { Cap(v.head,accent);ToolBelt(v.model); }
            else
            {
                P(rig,"box",-.28f,1.18f,.25f,.19f,.31f,.04f,new Color(.4f,.23f,.2f),new Vector3(0,0,18));
                P(v.head,"box",0,-.12f,.25f,.26f,.065f,.04f,new Color(.32f,.19f,.17f));
                P(v.head,"bevel",0,.2f,-.04f,.47f,.14f,.39f,ink);
                if(kind.Contains("brute")) { rig.localScale=Vector3.one*1.4f;P(rig,"bevel",0,1.42f,0,1.03f,.7f,.5f,new Color(.27f,.3f,.2f)); }
            }
        }
        private Transform Arm(Transform rig,int side,Color sleeve,Color face)
        {
            Transform arm=Group(rig,side<0?"Left shoulder":"Right shoulder");arm.localPosition=new Vector3(side*.43f,1.55f,0);
            P(arm,"bevel",0,-.2f,0,.23f,.46f,.26f,sleeve);P(arm,"bevel",0,-.48f,.015f,.2f,.18f,.23f,face);return arm;
        }
        private Transform Leg(Transform rig,int side)
        {
            Transform leg=Group(rig,side<0?"Left hip":"Right hip");leg.localPosition=new Vector3(side*.19f,.87f,0);
            P(leg,"bevel",0,-.3f,0,.26f,.59f,.29f,new Color(.17f,.23f,.25f));P(leg,"bevel",0,-.67f,.09f,.3f,.22f,.49f,ink);return leg;
        }
        private void Cap(Transform head,Color color)
        {
            P(head,"bevel",0,.23f,-.01f,.5f,.17f,.46f,color);P(head,"bevel",0,.16f,.17f,.55f,.045f,.37f,color);
        }
        private void HardHat(Transform head)
        {
            P(head,"round",0,.23f,0,.57f,.37f,.54f,cream);P(head,"cylinder",0,.12f,0,.62f,.05f,.61f,accent);P(head,"box",0,.36f,0,.07f,.08f,.46f,copper);
        }
        private void Goggles(Transform head)
        {
            P(head,"box",0,.035f,.245f,.45f,.16f,.05f,ink);P(head,"bevel",-.12f,.035f,.28f,.18f,.1f,.035f,new Color(.3f,.76f,.8f),glow:true);P(head,"bevel",.12f,.035f,.28f,.18f,.1f,.035f,new Color(.3f,.76f,.8f),glow:true);
        }
        private void Bandanna(Transform head)
        {
            P(head,"box",0,.13f,.01f,.48f,.13f,.46f,danger);P(head,"roof",.26f,.1f,-.14f,.15f,.26f,.18f,danger,new Vector3(0,0,-22));
        }
        private void ToolBelt(Transform root)
        {
            for(int i=-1;i<=1;i++)P(root,"bevel",i*.23f,.98f,.31f,.16f,.18f,.12f,wood);P(root,"cylinder",.38f,1.04f,.05f,.1f,.33f,.1f,metal,new Vector3(0,0,12));
        }
        private void Medic(Visual v)
        {
            P(v.model,"box",0,1.28f,.253f,.26f,.1f,.035f,cream);P(v.model,"box",0,1.28f,.26f,.1f,.26f,.04f,cream);
            P(v.model,"box",0,1.31f,-.505f,.31f,.1f,.025f,cream);P(v.model,"box",0,1.31f,-.51f,.1f,.31f,.03f,cream);Cap(v.head,cream);
        }
        private void Rifle(Visual v)
        {
            Transform root=Group(v.rightArm,"Original carbine");root.localPosition=new Vector3(0,-.43f,.2f);
            P(root,"bevel",0,0,.16f,.12f,.17f,.6f,metal);P(root,"box",0,-.06f,-.16f,.13f,.23f,.3f,wood);P(root,"cylinder",0,.015f,.43f,.065f,.28f,.065f,ink,new Vector3(90,0,0));
            v.muzzle=P(root,"crystal",0,.02f,.73f,.22f,.4f,.22f,cream,new Vector3(90,0,0),true);v.muzzle.gameObject.SetActive(false);
        }
        private void Sword(Transform arm)
        {
            P(arm,"cylinder",0,-.42f,.1f,.09f,.26f,.09f,wood);P(arm,"box",0,-.17f,.1f,.35f,.07f,.11f,copper);P(arm,"bevel",0,.23f,.1f,.13f,.75f,.055f,cream);P(arm,"cone",0,.6f,.1f,.13f,.23f,.06f,cream);
        }
        private void Axe(Transform arm)
        {
            P(arm,"cylinder",0,-.46f,.08f,.1f,.95f,.1f,wood);P(arm,"roof",.1f,.22f,.08f,.45f,.36f,.12f,metal,new Vector3(0,0,-90));P(arm,"box",0,.21f,.08f,.12f,.24f,.15f,copper);
        }
        private void Hammer(Transform arm)
        {
            P(arm,"cylinder",0,-.47f,.1f,.09f,.7f,.09f,wood);P(arm,"bevel",0,.18f,.1f,.42f,.21f,.23f,metal);
        }
        private void Bow(Transform arm)
        {
            Beam(arm,new Vector3(0,-.44f,.12f),new Vector3(0,-.05f,.3f),.08f,wood);Beam(arm,new Vector3(0,-.44f,.12f),new Vector3(0,-.83f,.3f),.08f,wood);Beam(arm,new Vector3(0,-.05f,.3f),new Vector3(0,-.83f,.3f),.018f,cream);
        }
        private void Armor(Visual v,Color armor,bool sword)
        {
            P(v.model,"bevel",0,1.33f,.21f,.64f,.61f,.14f,armor);
            P(v.model,"bevel",-.47f,1.6f,0,.38f,.24f,.48f,armor);P(v.model,"bevel",.47f,1.6f,0,.38f,.24f,.48f,armor);
            P(v.head,"bevel",0,.14f,-.03f,.52f,.3f,.48f,armor);P(v.head,"box",0,.03f,.25f,.43f,.08f,.08f,ink);P(v.head,"box",0,-.12f,.28f,.09f,.26f,.07f,copper);
            P(v.head,"box",-.11f,.025f,.296f,.1f,.025f,.025f,accent,glow:true);P(v.head,"box",.11f,.025f,.296f,.1f,.025f,.025f,accent,glow:true);
            if(sword)
            {
                Sword(v.rightArm);P(v.leftArm,"bevel",-.12f,-.4f,.15f,.12f,.66f,.5f,armor);P(v.leftArm,"bevel",-.19f,-.4f,.15f,.05f,.41f,.29f,copper);
            }
        }
        private void SpaceSuit(Visual v,Color color)
        {
            P(v.head,"round",0,.04f,0,.66f,.65f,.62f,cream);P(v.head,"bevel",0,.02f,.26f,.5f,.35f,.14f,new Color(.055f,.17f,.22f));
            P(v.head,"bevel",-.13f,.1f,.34f,.19f,.07f,.025f,new Color(.48f,.81f,.85f),glow:true);
            P(v.model,"bevel",0,1.3f,.26f,.36f,.3f,.08f,ink);P(v.model,"box",.08f,1.33f,.31f,.08f,.08f,.03f,signal,Vector3.zero,true);
            P(v.model,"cylinder",-.19f,1.01f,-.42f,.25f,.8f,.25f,color);P(v.model,"cylinder",.19f,1.01f,-.42f,.25f,.8f,.25f,color);
        }
        private void Viking(Visual v)
        {
            P(v.model,"bevel",0,1.43f,0,.84f,.28f,.52f,new Color(.47f,.4f,.32f));
            P(v.head,"round",0,.15f,0,.54f,.36f,.51f,metal);P(v.head,"box",0,-.01f,.28f,.07f,.33f,.08f,copper);
            P(v.head,"roof",0,-.26f,.17f,.35f,.33f,.2f,new Color(.52f,.3f,.15f),new Vector3(0,0,180));
            Axe(v.rightArm);P(v.leftArm,"cylinder",-.13f,-.4f,.03f,.66f,.12f,.66f,wood,new Vector3(0,0,90));P(v.leftArm,"round",-.23f,-.4f,.03f,.2f,.2f,.2f,metal);
        }
        private void Ranger(Visual v)
        {
            P(v.head,"cone",0,.16f,-.05f,.67f,.45f,.6f,leaf);P(v.head,"bevel",0,.02f,-.18f,.58f,.53f,.24f,leaf);
            P(v.model,"roof",0,.8f,-.25f,.75f,.78f,.15f,leaf,new Vector3(0,0,180));
            P(v.rightArm,"cylinder",0,-.75f,.11f,.08f,1.75f,.08f,wood);P(v.rightArm,"crystal",0,1.04f,.11f,.25f,.35f,.25f,signal,glow:true);
            P(v.model,"bevel",.27f,1.16f,-.4f,.18f,.76f,.18f,wood,new Vector3(0,0,-15));
        }
        private void Chef(Visual v)
        {
            P(v.model,"box",0,1.23f,.255f,.52f,.67f,.05f,cream);
            P(v.head,"cylinder",0,.19f,0,.45f,.27f,.43f,cream);
            for(int i=-1;i<=1;i++)P(v.head,"round",i*.16f,.48f,0,.3f,.32f,.34f,cream);
            P(v.rightArm,"cylinder",0,-.52f,.12f,.045f,.55f,.045f,metal);P(v.rightArm,"box",0,.04f,.12f,.18f,.22f,.035f,metal);
        }
        private void Shopkeeper(Visual v)
        {
            Cap(v.head,accent);P(v.model,"box",0,1.15f,.26f,.54f,.78f,.04f,cream);P(v.model,"box",.19f,1.48f,.29f,.15f,.08f,.03f,accent);
            P(v.leftArm,"box",0,-.39f,.2f,.32f,.45f,.045f,wood,new Vector3(10,0,0));P(v.leftArm,"box",0,-.37f,.23f,.25f,.34f,.02f,cream,new Vector3(10,0,0));
        }
        private void Conductor(Visual v)
        {
            Cap(v.head,ink);P(v.head,"box",0,.2f,.26f,.17f,.075f,.02f,copper);P(v.model,"box",0,1.41f,.25f,.1f,.23f,.04f,copper);ToolBelt(v.model);Hammer(v.rightArm);
        }
        private void Orc(Visual v,bool brute)
        {
            P(v.head,"bevel",0,-.16f,.16f,.5f,.22f,.4f,new Color(.37f,.52f,.26f));
            P(v.head,"cone",-.16f,-.2f,.35f,.07f,.28f,.08f,cream);P(v.head,"cone",.16f,-.2f,.35f,.07f,.28f,.08f,cream);
            P(v.head,"roof",-.29f,.03f,0,.32f,.12f,.25f,leaf,new Vector3(0,0,90));P(v.head,"roof",.29f,.03f,0,.32f,.12f,.25f,leaf,new Vector3(0,0,-90));
            P(v.model,"bevel",-.43f,1.62f,0,.44f,.23f,.5f,metal);P(v.model,"cone",-.43f,1.73f,0,.12f,.33f,.12f,cream);Axe(v.rightArm);
            if(brute)v.model.localScale=Vector3.one*1.45f;
        }
        private void ShoppingBasket(Transform arm)
        {
            Transform root=Group(arm,"Shopping basket");root.localPosition=new Vector3(-.1f,-.67f,.1f);
            P(root,"bevel",0,0,0,.49f,.25f,.33f,copper);P(root,"box",0,.13f,.17f,.48f,.05f,.04f,cream);P(root,"box",0,.13f,-.17f,.48f,.05f,.04f,cream);
            Beam(root,new Vector3(-.2f,.12f,0),new Vector3(0,.35f,0),.025f,metal);Beam(root,new Vector3(.2f,.12f,0),new Vector3(0,.35f,0),.025f,metal);
        }
        private void BuildWolf(Visual v)
        {
            v.quadruped=true;Color fur=new Color(.36f,.39f,.36f);
            P(v.model,"bevel",0,.69f,0,.65f,.53f,1.19f,fur);P(v.model,"bevel",0,.91f,.63f,.48f,.43f,.55f,fur);P(v.model,"bevel",0,.78f,.94f,.28f,.22f,.39f,fur);P(v.model,"bevel",0,.81f,1.13f,.18f,.15f,.11f,ink);
            P(v.model,"cone",-.17f,1.08f,.63f,.2f,.3f,.18f,fur);P(v.model,"cone",.17f,1.08f,.63f,.2f,.3f,.18f,fur);
            P(v.model,"box",-.19f,.98f,.8f,.05f,.045f,.08f,danger);P(v.model,"box",.19f,.98f,.8f,.05f,.045f,.08f,danger);
            v.leftArm=WolfLeg(v.model,-.22f,.39f);v.rightArm=WolfLeg(v.model,.22f,.39f);v.leftLeg=WolfLeg(v.model,-.22f,-.4f);v.rightLeg=WolfLeg(v.model,.22f,-.4f);
            P(v.model,"cone",0,.72f,-.55f,.2f,.82f,.2f,fur,new Vector3(-70,0,0));
        }
        private Transform WolfLeg(Transform parent,float x,float z)
        {
            Transform leg=Group(parent,"Wolf leg");leg.localPosition=new Vector3(x,.65f,z);P(leg,"bevel",0,-.25f,0,.15f,.5f,.17f,new Color(.25f,.29f,.27f));P(leg,"bevel",0,-.55f,.06f,.19f,.14f,.3f,ink);return leg;
        }
        private Transform TreantRoot(Transform root,int side)
        {
            Transform leg=Group(root,"Articulated treant root");leg.localPosition=new Vector3(side*.22f,.88f,0);P(leg,"trunk",0,-.8f,0,.3f,.8f,.3f,wood);P(leg,"bevel",0,-.8f,.15f,.36f,.16f,.7f,wood);return leg;
        }
        private void BuildTreant(Visual v,bool elder)
        {
            P(v.model,"trunk",0,.52f,0,.94f,1.57f,.81f,wood);v.head=Group(v.model,"Treant crown");v.head.localPosition=new Vector3(0,1.96f,0);P(v.head,"bevel",0,0,0,.7f,.72f,.65f,wood);
            P(v.head,"box",-.16f,.06f,.34f,.15f,.08f,.035f,signal,glow:true);P(v.head,"box",.16f,.06f,.34f,.15f,.08f,.035f,signal,glow:true);P(v.head,"box",0,-.16f,.33f,.32f,.08f,.04f,ink);
            for(int i=0;i<5;i++) { float a=i*Mathf.PI*2/5;P(v.head,"cone",Mathf.Cos(a)*.35f,.22f,Mathf.Sin(a)*.35f,.18f,.8f,.18f,wood,new Vector3(Mathf.Sin(a)*24,0,-Mathf.Cos(a)*24));P(v.head,"round",Mathf.Cos(a)*.45f,.85f,Mathf.Sin(a)*.45f,.84f,.61f,.72f,leaf); }
            v.leftArm=Arm(v.model,-1,wood,wood);v.rightArm=Arm(v.model,1,wood,wood);v.leftLeg=TreantRoot(v.model,-1);v.rightLeg=TreantRoot(v.model,1);
            if(elder)v.model.localScale=Vector3.one*1.4f;
        }
    }
}
