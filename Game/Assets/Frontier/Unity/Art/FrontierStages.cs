using UnityEngine;

namespace Mayo.Frontier.Presentation
{
    public sealed partial class FrontierWorldView
    {
        private Transform stormRing;
        private void BuildScenery(int chapter)
        {
            if(scenery!=null){scenery.gameObject.SetActive(false);Destroy(scenery.gameObject);}
            if(stormRing!=null){Destroy(stormRing.gameObject);stormRing=null;}
            scenery=Group(ownedRoot,"Original "+gameId+" chapter "+chapter+" scenery");
            P(scenery,"box",0,-.48f,0,45,.9f,45,ground);
            if(gameId=="dead-orbit") SpaceStage(chapter);
            else if(gameId=="market-day") ShopStage(chapter);
            else if(gameId=="hearth-rush") KitchenStage(chapter);
            else if(gameId=="fjordbound") FjordStage(chapter);
            else if(gameId=="skyhold-siege") SkyholdStage(chapter);
            else if(gameId=="rift-runners") ArenaStage(chapter);
            else if(gameId=="thornvale") ForestStage(chapter);
            else if(gameId=="emberwake-rebuilders") RuinsStage(chapter);
            else if(gameId=="gatebreakers-guild") GuildStage(chapter);
            else if(gameId=="stormline-express") RailwayStage(chapter);
            else CountyStage(chapter,gameId=="havenfall-coalition");
            kit.CombineStatic(scenery);
        }
        private void Road(Transform root,Vector3 a,Vector3 b,float width,Color color)
        {
            Vector3 delta=b-a;Transform path=P(root,"box",(a.x+b.x)*.5f,.012f,(a.z+b.z)*.5f,width,.035f,delta.magnitude,color);
            path.localRotation=Quaternion.Euler(0,Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg,0);
        }
        private void GroundTiles(Transform root,Color a,Color b,int across,float tile)
        {
            for(int x=0;x<across;x++)for(int z=0;z<across;z++)P(root,"box",(x-(across-1)*.5f)*tile,.014f,(z-(across-1)*.5f)*tile,tile-.06f,.04f,tile-.06f,((x+z)&1)==0?a:b);
        }
        private void Fence(Transform root,Vector3 a,Vector3 b,bool iron=false)
        {
            float length=(b-a).magnitude;int steps=Mathf.CeilToInt(length/2.2f);Color color=iron?metal:wood;
            for(int i=0;i<=steps;i++){Vector3 p=Vector3.Lerp(a,b,(float)i/steps);P(root,"box",p.x,.65f,p.z,.18f,1.3f,.18f,color);P(root,"cone",p.x,1.3f,p.z,.24f,.22f,.24f,iron?cream:copper);}
            Beam(root,a+Vector3.up*.48f,b+Vector3.up*.48f,.13f,color);Beam(root,a+Vector3.up*1.01f,b+Vector3.up*1.01f,.13f,color);
        }
        private void Tree(Transform root,Vector3 position,float scale,bool fir=false,bool autumn=false)
        {
            Transform g=Group(root,fir?"Layered fir":"Faceted broadleaf tree");g.localPosition=position;g.localScale=Vector3.one*scale;
            P(g,"trunk",0,0,0,.45f,2.1f,.43f,wood);
            Color foliage=autumn?new Color(.67f,.38f,.16f):leaf;
            if(fir){for(int i=0;i<3;i++)P(g,"cone",0,1+i*.68f,0,2.3f-i*.47f,1.6f,2.3f-i*.47f,Color.Lerp(foliage,ink,i*.1f));}
            else
            {
                Beam(g,new Vector3(0,1.3f,0),new Vector3(-.75f,2.15f,.2f),.18f,wood);Beam(g,new Vector3(0,1.5f,0),new Vector3(.65f,2.5f,-.2f),.18f,wood);
                P(g,"round",0,2.8f,0,2.5f,2,2.25f,foliage);P(g,"round",-.92f,2.28f,.16f,1.55f,1.4f,1.5f,Color.Lerp(foliage,cream,.1f));P(g,"round",.85f,2.6f,-.2f,1.55f,1.5f,1.65f,Color.Lerp(foliage,ink,.14f));
            }
        }
        private void Rock(Transform root,Vector3 p,float scale)
        {
            P(root,"bevel",p.x,p.y+scale*.4f,p.z,scale*1.2f,scale,scale,stone,new Vector3(7,p.x*17,13));
            P(root,"bevel",p.x+scale*.38f,p.y+scale*.2f,p.z+scale*.28f,scale*.7f,scale*.65f,scale*.8f,Color.Lerp(stone,ink,.15f),new Vector3(17,p.z*23,26));
        }
        private void House(Transform root,Vector3 position,Vector3 dimensions,Color wall,Color roof,bool longhouse=false)
        {
            Transform g=Group(root,longhouse?"Timber longhouse":"Original pitched-roof building");g.localPosition=position;
            float w=dimensions.x,h=dimensions.y,d=dimensions.z;
            P(g,"bevel",0,h*.5f,0,w,h,d,wall);P(g,"roof",0,h,0,w+.45f,h*.6f,d+.45f,roof);
            P(g,"box",0,.75f,-d*.505f,w*.24f,1.5f,.07f,ink);P(g,"box",0,.73f,-d*.53f,w*.19f,1.4f,.04f,wood);P(g,"round",w*.065f,.7f,-d*.56f,.07f,.07f,.07f,copper);
            for(int side=-1;side<=1;side+=2)
            {
                P(g,"box",side*w*.32f,h*.6f,-d*.52f,w*.2f,h*.33f,.07f,cream);P(g,"box",side*w*.32f,h*.6f,-d*.55f,w*.15f,h*.25f,.06f,new Color(.29f,.47f,.47f));
                P(g,"box",side*w*.32f,h*.6f,-d*.59f,.055f,h*.28f,.025f,wood);
                P(g,"box",side*w*.49f,h*.5f,-d*.5f,.15f,h,.15f,wood);
            }
            P(g,"box",0,h*.27f,-d*.52f,w,.13f,.1f,wood);P(g,"box",0,h*.84f,-d*.52f,w,.13f,.1f,wood);
            P(g,"bevel",w*.32f,h*1.33f,d*.2f,.35f,h*.6f,.4f,stone);
            if(longhouse)
            {
                for(int i=-1;i<=1;i+=2)Beam(g,new Vector3(0,h*1.6f,i*d*.58f),new Vector3(0,h*1.84f,i*d*.78f),.13f,wood);
                for(int i=-1;i<=1;i+=2)P(g,"cylinder",i*w*.33f,h*.55f,-d*.58f,.45f,.07f,.45f,accent,new Vector3(90,0,0));
            }
        }
        private void Longship(Transform root)
        {
            P(root,"hull",0,.02f,0,2.3f,1.3f,5.5f,wood);P(root,"box",0,.42f,0,1.65f,.16f,3.8f,copper);
            for(int i=-2;i<=2;i++)P(root,"box",0,.6f,i*.65f,1.9f,.14f,.22f,wood);
            P(root,"cylinder",0,.6f,0,.17f,3.7f,.17f,wood);P(root,"box",0,3.2f,0,2.8f,1.9f,.07f,cream);for(int i=-2;i<=2;i++)P(root,"box",i*.48f,3.2f,-.045f,.22f,1.9f,.025f,accent);
            Beam(root,new Vector3(0,4.2f,0),new Vector3(0,.7f,2.1f),.024f,cream);Beam(root,new Vector3(0,4.2f,0),new Vector3(0,.7f,-2.1f),.024f,cream);
            for(int i=-2;i<=2;i++)for(int side=-1;side<=1;side+=2){P(root,"cylinder",side*1.13f,.8f,i*.72f,.58f,.08f,.58f,i%2==0?accent:copper,new Vector3(0,0,90));P(root,"round",side*1.2f,.8f,i*.72f,.13f,.13f,.13f,metal);}
            P(root,"trunk",0,.35f,2.6f,.2f,1.2f,.2f,wood,new Vector3(25,0,0));P(root,"bevel",0,1.55f,2.9f,.35f,.3f,.6f,copper);
        }
        private void Train(Transform root,bool cargo)
        {
            P(root,"bevel",0,.55f,0,2.3f,.42f,3.8f,ink);
            for(int side=-1;side<=1;side+=2)for(int i=-1;i<=1;i++)
            {
                P(root,"cylinder",side*1.12f,.42f,i*1.25f,.75f,.15f,.75f,metal,new Vector3(0,0,90));P(root,"cylinder",side*1.22f,.42f,i*1.25f,.3f,.06f,.3f,copper,new Vector3(0,0,90));
            }
            if(cargo)
            {
                P(root,"bevel",0,1.26f,0,2.1f,1.4f,3.5f,wood);P(root,"roof",0,1.96f,0,2.3f,.45f,3.7f,metal);
                for(int i=-1;i<=1;i++)P(root,"box",-1.08f,1.25f,i*1.05f,.07f,1.15f,.75f,copper);
            }
            else
            {
                P(root,"cylinder",0,1.1f,-.4f,1.5f,2.7f,1.5f,Color.Lerp(accent,ink,.45f),new Vector3(90,0,0));
                P(root,"cylinder",0,1.1f,2.32f,1.55f,.13f,1.55f,metal,new Vector3(90,0,0));
                P(root,"cylinder",0,1.5f,1.3f,.46f,1.13f,.46f,metal);P(root,"cone",0,2.5f,1.3f,.65f,.28f,.65f,ink,new Vector3(180,0,0));
                P(root,"bevel",0,1.55f,-1.12f,2.15f,1.8f,1.3f,accent);P(root,"roof",0,2.45f,-1.12f,2.4f,.35f,1.65f,ink);
                P(root,"box",-1.1f,1.94f,-1.1f,.05f,.62f,.67f,new Color(.36f,.62f,.64f));P(root,"box",1.1f,1.94f,-1.1f,.05f,.62f,.67f,new Color(.36f,.62f,.64f));
                P(root,"cone",0,1.1f,2.5f,.5f,.55f,.5f,cream,new Vector3(90,0,0),true);
            }
        }
        private void CountyStage(int chapter,bool community)
        {
            Color asphalt=new Color(.22f,.25f,.25f);Road(scenery,new Vector3(-21,0,-3),new Vector3(21,0,-3),5,asphalt);Road(scenery,new Vector3(-3,0,-21),new Vector3(-3,0,21),4,asphalt);
            for(int i=-4;i<=4;i++) { P(scenery,"box",i*4.6f,.04f,-3,.9f,.015f,.1f,cream);P(scenery,"box",-3,.04f,i*4.6f,.1f,.015f,.9f,cream); }
            for(int i=0;i<7;i++)
            {
                float x=-18+i*6;House(scenery,new Vector3(x,0,25),new Vector3(4.2f,2.8f,3.8f),i%2==0?cream:stone,i%2==0?wood:accent);Tree(scenery,new Vector3(x+2.5f,0,23),1.1f,false,i%3==0);
            }
            House(scenery,new Vector3(-26,0,9),new Vector3(5,3.6f,5),community?stone:copper,wood);House(scenery,new Vector3(26,0,-8),new Vector3(5,3,4),cream,wood);
            Fence(scenery,new Vector3(-22,0,-22),new Vector3(22,0,-22));Fence(scenery,new Vector3(-22,0,22),new Vector3(-22,0,-22));
            for(int i=0;i<6;i++){Tree(scenery,new Vector3(23.2f,0,-20+i*7.5f),1+(i%2)*.25f,false,chapter==3);Barrel(scenery,new Vector3(-23,0,-16+i*6),metal,.8f);}
            Car(scenery,new Vector3(24,0,1),chapter==2?copper:accent);Car(scenery,new Vector3(-25,0,-16),cream);
            if(community){P(scenery,"box",-11,.018f,-11,8,.045f,8,new Color(.37f,.36f,.29f));Road(scenery,new Vector3(-11,0,-11),new Vector3(0,0,0),2.3f,stone);}
        }
        private void Car(Transform root,Vector3 position,Color color)
        {
            Transform g=Group(root,"Original abandoned utility car");g.localPosition=position;P(g,"bevel",0,.65f,0,1.8f,.55f,3.3f,color);P(g,"bevel",0,1.15f,-.2f,1.48f,.55f,1.7f,metal);P(g,"bevel",0,1.18f,.69f,1.28f,.4f,.07f,new Color(.31f,.55f,.58f));
            for(int side=-1;side<=1;side+=2)for(int end=-1;end<=1;end+=2)P(g,"cylinder",side*.93f,.35f,end*1.08f,.65f,.17f,.65f,ink,new Vector3(0,0,90));
            P(g,"box",-.6f,.72f,1.67f,.3f,.13f,.05f,cream);P(g,"box",.6f,.72f,1.67f,.3f,.13f,.05f,cream);
        }
        private void SpaceStage(int chapter)
        {
            GroundTiles(scenery,ground,Color.Lerp(ground,cream,.05f),14,3);
            P(scenery,"box",0,.045f,0,5,.025f,42,new Color(.17f,.3f,.34f));P(scenery,"box",0,.046f,0,42,.025f,4,new Color(.17f,.3f,.34f));
            for(int i=-7;i<=7;i++)for(int side=-1;side<=1;side+=2) { P(scenery,"box",side*21.6f,.45f,i*3,.35f,.9f,2.8f,metal);P(scenery,"box",i*3,.45f,side*21.6f,2.8f,.9f,.35f,metal);P(scenery,"box",i*3,.04f,side*20.8f,.9f,.025f,.12f,accent,Vector3.zero,true); }
            for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
            {
                P(scenery,"bevel",x*25,-.1f,z*14,5,2,7,metal);P(scenery,"box",x*25,1.1f,z*14,4.2f,.15f,6,ink);
                for(int i=0;i<6;i++)P(scenery,"box",x*25,1.2f,z*14-2.5f+i,4,.06f,.55f,new Color(.16f,.32f,.49f));
                P(scenery,"cylinder",x*22.9f,0,z*19,.8f,3,.8f,metal);P(scenery,"box",x*22.9f,2.25f,z*19,.83f,.15f,.83f,accent,glow:true);
            }
            P(scenery,"box",13,.05f,13,10,.045f,10,new Color(.23f,.25f,.32f));P(scenery,"box",-13,.05f,-13,10,.045f,10,new Color(.2f,.32f,.3f));
            for(int i=0;i<35;i++){float x=Mathf.Sin(i*19)*34,z=Mathf.Cos(i*23)*34;if(Mathf.Abs(x)<23 && Mathf.Abs(z)<23)continue;P(scenery,"round",x,-1.8f,z,.1f+(i%3)*.07f,.1f,.1f,cream,glow:true);}
        }
        private void ForestStage(int chapter)
        {
            Road(scenery,new Vector3(-16,0,-16),new Vector3(-1,0,0),3.4f,new Color(.39f,.35f,.23f));Road(scenery,new Vector3(-1,0,0),new Vector3(16,0,16),2.4f,new Color(.39f,.35f,.23f));Road(scenery,new Vector3(-1,0,0),new Vector3(-15,0,14),2.2f,new Color(.39f,.35f,.23f));
            for(int i=0;i<35;i++){float a=i*Mathf.PI*2/35;float r=23.4f+(i%3)*1.1f;Tree(scenery,new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r),1+(i%4)*.17f,i%3==0,chapter==3 && i%4==0);if(i%3==0)Rock(scenery,new Vector3(Mathf.Cos(a)*22.9f,0,Mathf.Sin(a)*22.9f),1.2f);}
            House(scenery,new Vector3(-25,0,-13),new Vector3(4,2.6f,4),cream,leaf);House(scenery,new Vector3(-26,0,-20),new Vector3(4,2.5f,4),wood,leaf);
            for(int i=0;i<60;i++){float x=Mathf.Sin(i*31)*20,z=Mathf.Cos(i*17)*20;P(scenery,"cone",x,.02f,z,.12f,.22f,.12f,leaf);if(i%3==0)P(scenery,"round",x,.23f,z,.18f,.12f,.18f,i%2==0?cream:accent);}
            for(int i=0;i<4;i++)P(scenery,"bevel",23.5f,1+i*.58f,13.5f,.9f,.53f,.75f,stone,new Vector3(0,i*7,0));
        }
        private void SkyholdStage(int chapter)
        {
            P(scenery,"bevel",0,-2.5f,0,40,4.2f,40,stone);P(scenery,"bevel",0,-5.1f,0,30,4,30,Color.Lerp(stone,ink,.22f),new Vector3(0,12,0));
            Road(scenery,new Vector3(-20,0,0),new Vector3(20,0,0),3.2f,stone);Road(scenery,new Vector3(0,0,-20),new Vector3(0,0,20),3.2f,stone);
            for(int i=0;i<16;i++){float a=i*Mathf.PI/8;Vector3 p=new Vector3(Mathf.Cos(a)*22.4f,-1.2f,Mathf.Sin(a)*22.4f);Rock(scenery,p,2);if(i%3==0){P(scenery,"cylinder",p.x,0,p.z,.45f,3.6f,.45f,wood);P(scenery,"box",p.x,2.9f,p.z,2.1f,.7f,.08f,cream,new Vector3(0,i*23,20));}}
            for(int i=-1;i<=1;i+=2) { House(scenery,new Vector3(i*25,0,-13),new Vector3(4,3,5),stone,wood);Fence(scenery,new Vector3(i*22.8f,0,-20),new Vector3(i*22.8f,0,20)); }
            P(scenery,"ring",0,.06f,0,8,1,8,accent,glow:true);
        }
        private void ArenaStage(int chapter)
        {
            for(int i=0;i<8;i++){float a=i*Mathf.PI/4;Vector3 p=new Vector3(Mathf.Cos(a)*24.7f,0,Mathf.Sin(a)*24.7f);P(scenery,"bevel",p.x,1.8f,p.z,4,3.6f,4,stone,new Vector3(0,-a*Mathf.Rad2Deg,0));P(scenery,"bevel",p.x,4.3f,p.z,3.4f,1.6f,3.4f,ink);P(scenery,"box",p.x,4.15f,p.z-1.75f,2.3f,.7f,.07f,accent,glow:true);Tree(scenery,new Vector3(p.x*.96f,0,p.z*.96f),.75f);}
            Road(scenery,new Vector3(-19,0,-19),new Vector3(19,0,19),3.2f,stone);Road(scenery,new Vector3(-19,0,19),new Vector3(19,0,-19),3.2f,stone);
            P(scenery,"hex",0,.03f,0,9,.07f,9,new Color(.31f,.34f,.4f));
            stormRing=kit.Part(ownedRoot,"ring",new Vector3(0,.075f,0),new Vector3(48,1,48),new Color(.63f,.35f,.89f),glow:true);
            for(int i=0;i<12;i++){float a=i*Mathf.PI/6;P(scenery,"bevel",Mathf.Cos(a)*22.8f,1.4f,Mathf.Sin(a)*22.8f,.25f,2.8f,.25f,accent);P(scenery,"crystal",Mathf.Cos(a)*22.8f,3,Mathf.Sin(a)*22.8f,.38f,.6f,.38f,accent,glow:true);}
        }
        private void RuinsStage(int chapter)
        {
            GroundTiles(scenery,ground,Color.Lerp(stone,ground,.65f),14,3);
            P(scenery,"box",-14,.05f,-14,11,.05f,11,new Color(.34f,.3f,.24f));Road(scenery,new Vector3(-14,0,-14),new Vector3(11,0,14),2.7f,stone);
            for(int i=-3;i<=3;i++)
            {
                float x=i*6.8f;P(scenery,"bevel",x,1.7f,23.3f,5.6f,3.4f,1.4f,stone);P(scenery,"box",x,3.5f,23.3f,6,.3f,1.7f,copper);
                for(int n=-1;n<=1;n++)P(scenery,"bevel",x+n*1.7f,3.95f,23.3f,.9f,.75f,1.4f,stone);
                if(i%2==0){P(scenery,"trunk",-23.2f,0,x,1.3f,3.3f,1.3f,stone);P(scenery,"hex",-23.2f,3.3f,x,1.6f,.35f,1.6f,copper);}
            }
            for(int i=0;i<8;i++){Rock(scenery,new Vector3(23.4f,0,-19+i*5.4f),1.3f);if(i%2==0)Tree(scenery,new Vector3(-24,0,-19+i*5.4f),.8f);}
            House(scenery,new Vector3(-24,0,-14),new Vector3(4,2.6f,4),wood,accent);P(scenery,"ring",12,.06f,13,13,1,13,copper);
        }
        private void FjordStage(int chapter)
        {
            P(scenery,"box",0,-1.3f,0,85,.3f,85,new Color(.17f,.34f,.4f));P(scenery,"bevel",0,-1.2f,0,43,1.8f,43,stone);
            P(scenery,"box",-14,.016f,-10,9,.045f,8,new Color(.18f,.37f,.41f));
            for(int i=0;i<5;i++)P(scenery,"box",-18.2f,.12f,-13+i*1.4f,1.2f,.16f,1.3f,wood);
            for(int i=0;i<23;i++){float a=i*Mathf.PI*2/23;Rock(scenery,new Vector3(Mathf.Cos(a)*22.8f,-.4f,Mathf.Sin(a)*22.8f),1.5f);if(i%2==0)Tree(scenery,new Vector3(Mathf.Cos(a)*25,0,Mathf.Sin(a)*25),1.1f,true);}
            for(int i=-2;i<=2;i++)House(scenery,new Vector3(i*7.5f,0,25),new Vector3(4.3f,2.9f,4.5f),wood,stone,true);
            Road(scenery,new Vector3(-14,0,-16),new Vector3(15,0,16),2.8f,new Color(.41f,.45f,.43f));
            for(int i=0;i<8;i++) { float x=Mathf.Sin(i*7)*19,z=Mathf.Cos(i*11)*19;P(scenery,"bevel",x,.028f,z,1.8f,.05f,1.3f,new Color(.78f,.83f,.8f),new Vector3(0,i*17,0)); }
        }
        private void GuildStage(int chapter)
        {
            GroundTiles(scenery,ground,Color.Lerp(stone,ground,.6f),14,3);
            Road(scenery,new Vector3(0,0,-20),new Vector3(0,0,20),7,stone);
            for(int side=-1;side<=1;side+=2)for(int i=-3;i<=3;i++)
            {
                float z=i*6.7f;P(scenery,"bevel",side*23.3f,2,z,2,4,6,stone);P(scenery,"box",side*23.3f,4.1f,z,2.3f,.25f,6.2f,copper);P(scenery,"bevel",side*23.3f,4.6f,z,2.3f,.8f,1.6f,stone);
                if(i%2==0){P(scenery,"box",side*22.2f,2.5f,z,.06f,1.6f,.8f,accent);Lantern(scenery,new Vector3(side*22.1f,1.45f,z),new Color(1,.57f,.2f));}
            }
            for(int side=-1;side<=1;side+=2){P(scenery,"hex",side*19,0,24,4.4f,6,4.4f,stone);P(scenery,"cone",side*19,6,24,4.8f,2.6f,4.8f,wood);}
            P(scenery,"ring",0,.05f,-11,7.4f,1,7.4f,accent,glow:true);
        }
        private void ShopStage(int chapter)
        {
            GroundTiles(scenery,new Color(.7f,.69f,.57f),new Color(.62f,.63f,.52f),14,3);
            P(scenery,"box",0,.058f,-6,25,.02f,8,new Color(.37f,.42f,.36f));
            P(scenery,"box",0,2,22.3f,45,4,.35f,cream);P(scenery,"box",0,4.1f,22.3f,45,.3f,.7f,accent);
            for(int side=-1;side<=1;side+=2){P(scenery,"box",side*22.3f,1.2f,0,.35f,2.4f,44,cream);P(scenery,"box",side*22.3f,2.4f,0,.6f,.18f,44,wood);}
            for(int i=-3;i<=3;i++){P(scenery,"box",i*6,2.2f,22,4.9f,1.45f,.04f,new Color(.29f,.45f,.46f));P(scenery,"box",i*6,2.2f,21.94f,.12f,1.6f,.05f,wood);}
            for(int i=-3;i<=3;i++){Transform stall=Group(scenery,"Perimeter shop facade");stall.localPosition=new Vector3(i*6.5f,0,25);Counter(stall,wood,5);P(stall,"box",0,3.2f,0,5.5f,.2f,2.2f,i%2==0?accent:cream);for(int s=-1;s<=1;s+=2)P(stall,"cylinder",s*2.4f,0,0,.15f,3.2f,.15f,wood);}
            for(int side=-1;side<=1;side+=2){Tree(scenery,new Vector3(side*24,0,-15),1);Bench(scenery,new Vector3(side*24,0,-20),3,wood);}
        }
        private void KitchenStage(int chapter)
        {
            GroundTiles(scenery,new Color(.66f,.59f,.47f),new Color(.53f,.48f,.39f),14,3);
            P(scenery,"box",0,.058f,1,23,.02f,24,new Color(.36f,.4f,.35f));
            P(scenery,"box",0,1.8f,22.3f,45,3.6f,.4f,cream);P(scenery,"box",-22.3f,1.1f,0,.4f,2.2f,44,cream);P(scenery,"box",22.3f,1.1f,0,.4f,2.2f,44,cream);
            for(int i=-3;i<=3;i++)
            {
                Transform bench=Group(scenery,"Perimeter bakery bench");bench.localPosition=new Vector3(i*6.2f,0,24);Counter(bench,wood,5.5f);P(bench,"box",0,2.35f,.3f,4.8f,.18f,.75f,metal);
                for(int j=-2;j<=2;j++){P(bench,"cylinder",j*.72f,2.5f,.3f,.45f,.56f,.45f,copper);P(bench,"cylinder",j*.72f,3.08f,.3f,.47f,.05f,.47f,cream);}
            }
            for(int side=-1;side<=1;side+=2)for(int i=0;i<3;i++){Transform table=Group(scenery,"Dining terrace table");table.localPosition=new Vector3(side*25,0,-14+i*9);P(table,"cylinder",0,0,0,.2f,1,.2f,wood);P(table,"cylinder",0,1,0,2.6f,.16f,2.6f,cream);for(int j=0;j<3;j++){float a=j*Mathf.PI*2/3;P(table,"cylinder",Mathf.Cos(a)*1.65f,0,Mathf.Sin(a)*1.65f,.2f,.65f,.2f,metal);P(table,"cylinder",Mathf.Cos(a)*1.65f,.65f,Mathf.Sin(a)*1.65f,.65f,.12f,.65f,accent);}}
        }
        private void RailwayStage(int chapter)
        {
            Road(scenery,new Vector3(-12,0,-22),new Vector3(-12,0,22),4.5f,stone);Road(scenery,new Vector3(-15,0,-22),new Vector3(-15,0,22),3.3f,stone);
            for(int i=-15;i<=15;i++)for(int lane=0;lane<2;lane++)P(scenery,"box",-12-lane*3,.05f,i*1.45f,2.6f,.16f,.28f,wood);
            for(int lane=0;lane<2;lane++)for(int side=-1;side<=1;side+=2)P(scenery,"box",-12-lane*3+side*.79f,.17f,0,.12f,.12f,44,metal);
            P(scenery,"box",-7,.04f,-11,3,.1f,9,stone);
            for(int i=-3;i<=3;i++){P(scenery,"cylinder",-22.6f,0,i*7,.18f,5.2f,.18f,wood);P(scenery,"box",-22.6f,4.8f,i*7,3,.15f,.15f,wood);Beam(scenery,new Vector3(-23.5f,4.8f,i*7),new Vector3(-23.5f,4.8f,(i+1)*7),.025f,ink);Tree(scenery,new Vector3(24,0,i*7),1.15f,true);Rock(scenery,new Vector3(23,0,i*7+2),1.4f);}
            House(scenery,new Vector3(-26,0,-10),new Vector3(5,3.2f,7),wood,metal);House(scenery,new Vector3(26,0,16),new Vector3(4,3,5),stone,metal);
            for(int i=0;i<6;i++) { float x=Mathf.Sin(i*13)*18,z=Mathf.Cos(i*7)*18;P(scenery,"bevel",x,.02f,z,1.8f,.04f,1.1f,new Color(.32f,.34f,.33f),new Vector3(0,i*32,0)); }
        }
    }
}
