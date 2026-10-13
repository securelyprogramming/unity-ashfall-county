using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Mayo.Frontier.Presentation
{
    [Serializable] public sealed class CastMember { public string name,role; }
    [Serializable] public sealed class ChapterInfo { public int number,layoutSeed; public string title,story; }
    [Serializable] public sealed class GameProfile
    {
        public string id,title,mode,genre,brief,accent,setting,repository;
        public int seed=1,difficulty=1;
        public CastMember[] cast;
        public ChapterInfo[] chapters;
        public Config Rules(){return new Config{id=id,title=title,mode=mode,seed=seed,difficulty=difficulty};}
    }
    [Serializable] public sealed class GameProfiles { public GameProfile[] games; }
    [Serializable] public sealed class ProgressData
    {
        public int schema=1,unlocked=1,completedMask,quality=1;
        public string gameId,bindings="";
        public float volume=.65f;
        public bool fullScreen=false;
    }
    [Serializable] sealed class ProgressEnvelope { public int schema=1; public string gameId,data,sha256; }
    public sealed class FrontierProgress
    {
        readonly string path,id;
        public string Notice {get;private set;}
        public FrontierProgress(string directory,string gameId){id=gameId;path=Path.Combine(directory,gameId,"progress-v1.json");}
        static string Hash(string value){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        ProgressData Decode(string value)
        {
            if(value.Length>131072)throw new InvalidDataException("Save exceeds maximum size.");
            var envelope=JsonUtility.FromJson<ProgressEnvelope>(value);
            if(envelope==null||envelope.schema!=1||envelope.gameId!=id||envelope.data==null||envelope.sha256!=Hash(envelope.data))throw new InvalidDataException("Save checksum/version does not match.");
            var progress=JsonUtility.FromJson<ProgressData>(envelope.data);
            if(progress==null||progress.schema!=1||progress.gameId!=id||progress.unlocked<1||progress.unlocked>3||progress.completedMask<0||progress.completedMask>7||progress.quality<0||progress.quality>2||float.IsNaN(progress.volume)||float.IsInfinity(progress.volume)||progress.volume<0||progress.volume>1||(progress.bindings??"").Length>65536)throw new InvalidDataException("Save values are invalid.");
            return progress;
        }
        public ProgressData Load()
        {
            Notice="";
            foreach(string candidate in new[]{path,path+".bak"})if(File.Exists(candidate))try{var p=Decode(File.ReadAllText(candidate));if(candidate!=path)Notice="Recovered your last valid backup. The damaged save was preserved.";return p;}catch(Exception e){if(!(e is IOException)&&!(e is ArgumentException))Debug.LogWarning("[FrontierSave] Invalid local progression; trying backup.");}
            if(File.Exists(path))Notice="Local progression could not be read. Existing files are preserved; starting from chapter one.";
            return new ProgressData{gameId=id};
        }
        public bool Save(ProgressData progress)
        {
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                string data=JsonUtility.ToJson(progress);string bytes=JsonUtility.ToJson(new ProgressEnvelope{gameId=id,data=data,sha256=Hash(data)});Decode(bytes);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var raw=Encoding.UTF8.GetBytes(bytes);stream.Write(raw,0,raw.Length);stream.Flush(true);}
                if(File.Exists(path))
                {
                    string old=File.ReadAllText(path);bool valid=false;try{Decode(old);valid=true;}catch(Exception){/* Keep the last valid backup when recovering a damaged primary. */}
                    if(valid){string backupTemp=path+".bak.tmp";File.WriteAllText(backupTemp,old);if(File.Exists(path+".bak"))File.Replace(backupTemp,path+".bak",null);else File.Move(backupTemp,path+".bak");}
                    File.Replace(temp,path,null);
                }
                else File.Move(temp,path);
                Notice="";return true;
            }
            catch(Exception e){Notice="Progress could not be saved: "+e.Message;Debug.LogWarning("[FrontierSave] "+Notice);return false;}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        public string SavePath {get{return path;}}
    }
}
