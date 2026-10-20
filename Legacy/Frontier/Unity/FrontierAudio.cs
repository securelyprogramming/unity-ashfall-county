using UnityEngine;
namespace Mayo.Frontier.Presentation
{
    public sealed class FrontierAudio:MonoBehaviour
    {
        AudioSource music,effects;
        AudioClip confirm,strike,collect;
        void Awake(){music=gameObject.AddComponent<AudioSource>();music.loop=true;music.spatialBlend=0;music.volume=.34f;music.clip=Resources.Load<AudioClip>("FrontierAudio/Theme");if(music.clip!=null)music.Play();effects=gameObject.AddComponent<AudioSource>();effects.playOnAwake=false;effects.spatialBlend=0;confirm=Resources.Load<AudioClip>("FrontierAudio/Confirm");strike=Resources.Load<AudioClip>("FrontierAudio/Strike");collect=Resources.Load<AudioClip>("FrontierAudio/Collect");}
        public void Cue(string name){var clip=name=="attack"?strike:name=="interact"?collect:confirm;if(clip!=null)effects.PlayOneShot(clip,.5f);}
    }
}
