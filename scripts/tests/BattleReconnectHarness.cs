// Protocol regression harness: production BattleReconnect and ReliableBattleChannel are compiled
// alongside these fake Unity/Fusion surfaces. The runner script only substitutes UniTask with
// Task/manual yields; no production protocol/state-machine methods are copied or rewritten.
// This proves in-memory message/state behavior, not real Photon transport or Unity presentation.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Fusion;
namespace Cysharp.Threading.Tasks { static class Ext { public static void Forget(this Task t) {} } }
namespace Fusion.Sockets { public struct ReliableKey { public static ReliableKey FromInts(int a,int b,int c,int d) { return new ReliableKey(); } } }
namespace UnityEngine {
    public class MonoBehaviour { public T GetComponent<T>() where T:class { return Lab.Current.session as T; } }
    public static class Time { public static float realtimeSinceStartup, unscaledDeltaTime=0.1f; }
    public static class Mathf { public static int CeilToInt(float a) {return (int)Math.Ceiling(a);} public static float Max(float a,float b) { return Math.Max(a,b); } }
    public static class Debug { public static void Log(string s) {} public static void LogWarning(string s) {} public static void LogError(string s) { Console.WriteLine("LOG "+s); } }
}
namespace Fusion {
    public struct PlayerRef { public int id; public PlayerRef(int n) {id=n;} public static PlayerRef None=new PlayerRef(-1); public static bool operator==(PlayerRef a,PlayerRef b) {return a.id==b.id;} public static bool operator!=(PlayerRef a,PlayerRef b) {return a.id!=b.id;} public override bool Equals(object o) {return o is PlayerRef && ((PlayerRef)o).id==id;} public override int GetHashCode() {return id;} }
    public class SessionInfo { public bool IsVisible=true; }
    public class NetworkRunner { public PlayerRef LocalPlayer; public bool IsRunning=true; public PlayerRef[] ActivePlayers; public SessionInfo SessionInfo=new SessionInfo(); public void SendReliableDataToPlayer(PlayerRef p,Fusion.Sockets.ReliableKey k,byte[] b) { Lab.Send(LocalPlayer,p,b); } }
}
class NetworkSession { public NetworkRunner Runner; public string PairingKey="room"; public Task<bool> RejoinExistingRoom(string s,Func<bool> f) {return Task.FromResult(false);} }
static class TurnState { public static bool BattleEnded {get {return Lab.Current.ended;} set {Lab.Current.ended=value;}} public static bool ReconnectPaused {get {return Lab.Current.paused;} set {Lab.Current.paused=value;}} }
static class DeckConfig { public static bool AiTakeover {get {return Lab.Current.ai;}} }
static class ServerWaitOverlay { public static void SetStatus(object o,string s){}  public static void Hold(object x) {} public static void Release(object x) {} }
static class NetTimeouts {public const float OpponentDropGraceSec=30,AnimHandshakeSec=20,ReconnectHeartbeatSec=3;}
enum EMatchEndReason {InitError,Timeout,Desync}
class TurnRunner { public static TurnRunner Instance=new TurnRunner(); public void AbortMatch(EMatchEndReason r) {Lab.Current.ended=true; Lab.Current.abort=r.ToString();} public void HandleReconnectExpired(){Lab.Current.ai=true;} }
class BattleFieldState {public ulong hash=100;}
static class BattleStateHash {public static ulong Compute(BattleFieldState a,BattleFieldState b) {return a.hash;}}
class NetworkGameController { public void RejectTransportMessage(string s) {TurnRunner.Instance.AbortMatch(EMatchEndReason.Desync);}
    public static NetworkGameController Instance=new NetworkGameController();
    public void HandleMessage(PlayerRef sender,byte[] b) { if(b[0]==14) { int n=(b[1]<<24)|(b[2]<<16)|(b[3]<<8)|b[4]; ulong h=0;for(int i=0;i<8;i++)h=(h<<8)|b[5+i];Lab.Current.reconnect.ReceiveCheckpoint(n,h); } else { if(b[0]==15) Lab.Current.reconnect.AttackStarted(); Lab.Current.received.Add(b[0]); } }
    public void SendCheckpoint(int n,ulong h) {var b=new byte[13];b[0]=14;for(int i=0;i<4;i++)b[1+i]=(byte)(n>>(24-i*8));for(int i=0;i<8;i++)b[5+i]=(byte)(h>>(56-i*8));Lab.Current.reconnect.Send(b);}
}
static class FakeLoop {
    public struct Pause { public Awaiter GetAwaiter(){return new Awaiter();} }
    public struct Awaiter:INotifyCompletion {public bool IsCompleted {get{return false;}} public void GetResult(){} public void OnCompleted(Action a){Lab.Yields.Add(Tuple.Create(Lab.Current,a));}}
    public static Pause Yield(CancellationToken ct=default(CancellationToken)){return new Pause();}
}
class Peer {public NetworkSession session;public BattleReconnect reconnect; public bool ended,paused,ai;public string abort;public List<byte> received=new List<byte>();}
static class Lab {
    public static Peer Current;
    public static List<Tuple<Peer,Action>> Yields=new List<Tuple<Peer,Action>>();
    static Queue<Tuple<PlayerRef,PlayerRef,byte[]>> wire=new Queue<Tuple<PlayerRef,PlayerRef,byte[]>>();
    static Dictionary<int,Peer> peers=new Dictionary<int,Peer>();
    static bool drop;
    static void Call(Peer p,string method) {Current=p;typeof(BattleReconnect).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(p.reconnect,null);}
    public static void Send(PlayerRef source,PlayerRef to,byte[] b) {if(!drop)wire.Enqueue(Tuple.Create(source,to,(byte[])b.Clone()));}
    static void Pump() {int limit=5000;while(wire.Count>0 && --limit>0){var m=wire.Dequeue();Peer p;if(!peers.TryGetValue(m.Item2.id,out p))continue;Current=p;p.reconnect.Receive(m.Item1,m.Item3);}if(limit<=0)throw new Exception("protocol packet loop");}
    static void Tick(int times=1) {for(int i=0;i<times;i++){UnityEngine.Time.realtimeSinceStartup+=0.1f; foreach(var p in peers.Values)Call(p,"Update");Pump();var y=Yields;Yields=new List<Tuple<Peer,Action>>();foreach(var x in y){Current=x.Item1;x.Item2();}Pump();}}
    static Peer Make(int id) {var p=new Peer();Current=p;p.session=new NetworkSession {Runner=new NetworkRunner {LocalPlayer=new PlayerRef(id),ActivePlayers=new[]{new PlayerRef(1),new PlayerRef(2)}}};p.reconnect=new BattleReconnect();peers[id]=p;Call(p,"Awake");return p;}
    static void Check(bool ok,string label) {if(!ok)throw new Exception("FAIL "+label);Console.WriteLine("PASS "+label);}
    static Task Checkpoint(Peer p,ulong hash=100){Current=p;return p.reconnect.CheckpointAsync(new BattleFieldState{hash=hash},new BattleFieldState(),CancellationToken.None);}
    static void Start(Peer p){Current=p;p.reconnect.BeginBattle();}
    static void Fresh(out Peer a,out Peer b) {
        peers.Clear(); wire.Clear(); Yields.Clear(); drop=false;
        UnityEngine.Time.realtimeSinceStartup=0;
        a=Make(1); b=Make(2); Tick(8); Start(a); Start(b);
        Checkpoint(a); Checkpoint(b); Tick(8);
    }
    static bool Authenticated(Peer p) {
        return (bool)typeof(BattleReconnect).GetField("authenticated",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p.reconnect);
    }
    public static void Main() {
        var a=Make(1);var b=Make(2);
        Current=a;a.reconnect.Send(new byte[]{40});Pump();Tick(8);
        Check(b.received.Count==1 && b.received[0]==40,"bootstrap and initial reliable payload");
        Start(a);Start(b);Check(!a.ended&&!b.ended,"begin battle after mutual key exchange");
        var ca=Checkpoint(a);var cb=Checkpoint(b);Tick(8);
        Check(ca.IsCompleted&&cb.IsCompleted&&!a.ended&&!b.ended,"normal checkpoint barrier");
        drop=true;Current=a;a.reconnect.Send(new byte[]{41});Tick(35);
        Check(a.paused&&b.paused,"heartbeat loss pauses both peers");
        drop=false;Tick(20);
        Check(!a.paused&&!b.paused&&!a.ended&&!b.ended,"challenge proof and checkpoint recovery release both peers");
        Check(b.received.Count==2&&b.received[1]==41,"unacknowledged data replayed exactly once");
        Tick(20);Check(b.received.Count==2,"ack retries do not redeliver payload");
        // A keeps its battle identity while replacing Photon PlayerRef.
        drop=true;Current=b;b.reconnect.PeerLeft(new PlayerRef(1));Current=a;a.reconnect.ConnectionLost();
        peers.Remove(1);peers[3]=a;a.session.Runner.LocalPlayer=new PlayerRef(3);a.session.Runner.ActivePlayers=new[]{new PlayerRef(3),new PlayerRef(2)};b.session.Runner.ActivePlayers=a.session.Runner.ActivePlayers;
        drop=false;Current=b;b.reconnect.PeerJoined(new PlayerRef(3));Current=a;a.reconnect.PeerJoined(new PlayerRef(2));Tick(20);
        Check(!a.paused&&!b.paused&&!a.ended&&!b.ended,"changed PlayerRef authenticated with original session key");
        Current=a;a.reconnect.AttackStarted();Current=b;b.reconnect.AttackStarted();
        var da=Checkpoint(a,999);var db=Checkpoint(b,888);Tick(8);
        Check(a.ended&&b.ended,"mismatched checkpoint fails closed");
        Fresh(out a,out b);
        drop=true;
        Current=a;a.reconnect.AttackStarted();a.reconnect.Send(new byte[]{15});
        var catchup=Checkpoint(a,200);
        Tick(35);drop=false;Tick(12);
        Check(a.paused&&b.paused&&!catchup.IsCompleted,"recovery waits while peer has not resolved replayed attack");
        var mirrored=Checkpoint(b,200);Tick(12);
        Check(catchup.IsCompleted&&mirrored.IsCompleted&&!a.paused&&!b.paused,"replayed attack catchup reaches shared checkpoint before resume");
        Fresh(out a,out b);
        drop=true;
        Current=b;b.reconnect.PeerLeft(new PlayerRef(1));
        var forged=new byte[33]; forged[0]=226;
        b.reconnect.Receive(new PlayerRef(99),forged);
        Check(b.paused&&!Authenticated(b)&&!b.ended,"forged proof cannot bind an unknown PlayerRef");
        Current=a;a.reconnect.ConnectionLost();
        Tick(305);
        Check(b.ai&&!b.ended&&!b.paused,"remote departure grace expiry hands off to AI");
        Check(a.ended&&a.abort=="Timeout"&&!a.paused,"local connection failure expires without AI takeover");
        Fresh(out a,out b);
        drop=true;
        Current=a;a.reconnect.PeerLeft(new PlayerRef(2));
        var input=a.reconnect.WaitForInputAsync(CancellationToken.None);
        var boundary=Checkpoint(a,777);
        Check(!input.IsCompleted&&!boundary.IsCompleted,"input and checkpoint await during recovery");
        a.reconnect.Stop(); Tick(2);
        Check(input.IsCompleted&&boundary.IsCompleted&&!a.paused&&!a.reconnect.Active,"cleanup releases pending waits and input gate");
        int oldCount=a.received.Count;
        Current=b;b.reconnect.Send(new byte[]{42}); drop=false; Tick(10);
        Check(a.received.Count==oldCount,"stopped transport ignores late data");
        Current=a;a.reconnect.ResetTransport();
        Check(!a.reconnect.Active&&!a.reconnect.IsRecovering&&!Authenticated(a),"new match reset clears old authentication and recovery state");
    }
}