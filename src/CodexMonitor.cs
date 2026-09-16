using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;

namespace SilverWolfPet {
public sealed class QuotaReading { public string Bucket;public int Minutes;public double Remaining;public DateTime ResetUtc; }
public sealed class CodexMonitor : IDisposable {
    public volatile bool UsageEnabled=true, TasksEnabled=true;
    public Action<string,string> Notice;
    public Action<string> Status;
    public Action<List<QuotaReading>> Quotas;
    public Action<string> Failure;
    readonly ManualResetEvent stop=new ManualResetEvent(false);
    readonly string home, ledgerPath;
    readonly Dictionary<string,string> ledger=new Dictionary<string,string>();
    readonly Dictionary<string,long> offsets=new Dictionary<string,long>();
    readonly HashSet<string> completed=new HashSet<string>();
    readonly Dictionary<string,List<byte>> pending=new Dictionary<string,List<byte>>();
    readonly DateTime started=DateTime.UtcNow;
    Process server;
    readonly object processLock=new object();
    public CodexMonitor(string stateFolder,string dataHome=null) {
        home=dataHome??Environment.GetEnvironmentVariable("CODEX_HOME");
        if(String.IsNullOrWhiteSpace(home))home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
        ledgerPath=Path.Combine(stateFolder,"codex-alerts.json");
        try { ledger=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(ledgerPath))??ledger; } catch { }
    }
    public void Start() { new Thread(Run) { IsBackground=true,Name="Codex pet monitor" }.Start(); }
    static Dictionary<string,object> Obj(object x) { return x as Dictionary<string,object>; }
    static object Get(Dictionary<string,object> x,string key) { object value;return x!=null&&x.TryGetValue(key,out value)?value:null; }
    static string Str(object x) { return x==null?"":Convert.ToString(x,System.Globalization.CultureInfo.InvariantCulture); }
    static Dictionary<string,object> Parse(string line) { return new JavaScriptSerializer { MaxJsonLength=16*1024*1024 }.DeserializeObject(line) as Dictionary<string,object>; }
    public static int Level(double remaining) { return remaining<=0?0:remaining<5?5:remaining<20?20:remaining<50?50:100; }
    public static bool ShouldAlert(double remaining,int previous) { return Level(remaining)<previous; }
    void Publish(string text) { if(Status!=null)Status(text); }
    void Alert(string title,string text) { if(Notice!=null)Notice(title,text); }
    static string FindCodex() {
        foreach(string directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)) {
            try { string p=Path.Combine(directory.Trim('"'),"codex.exe");if(File.Exists(p))return p; } catch { }
        }
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(Directory.Exists(root))return Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        return null;
    }
    Dictionary<string,object> Query() {
        string exe=FindCodex();if(exe==null)throw new IOException("未找到 Codex，请安装并登录 Codex 桌面版或 CLI。");
        var process=new Process { StartInfo=new ProcessStartInfo(exe,"app-server") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 } };
        var ready=new ManualResetEvent(false); Dictionary<string,object> result=null;
        Action wake=delegate { try { ready.Set(); }catch(ObjectDisposedException) { } };
        process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e) {
            if(e.Data==null) { wake();return; }
            try {
                var response=Parse(e.Data);string id=Str(Get(response,"id"));
                if(id=="1") {
                    if(Get(response,"error")!=null) { wake();return; }
                    process.StandardInput.WriteLine("{\"method\":\"initialized\"}");
                    process.StandardInput.WriteLine("{\"method\":\"account/rateLimits/read\",\"id\":2}");
                    process.StandardInput.Flush();
                } else if(id=="2") { result=Obj(Get(response,"result"));wake(); }
            } catch { wake(); }
        };
        // Drain diagnostics without retaining credentials, paths or conversation content.
        process.ErrorDataReceived+=delegate { };
        try {
            lock(processLock) { if(stop.WaitOne(0))throw new OperationCanceledException();server=process;process.Start(); }
            process.BeginOutputReadLine();process.BeginErrorReadLine();
            process.StandardInput.WriteLine("{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"silver_wolf_pet\",\"title\":\"Silver Wolf Pet\",\"version\":\"2.3.0\"}}}");process.StandardInput.Flush();
            WaitHandle.WaitAny(new WaitHandle[]{ready,stop},20000);
            if(result==null)throw new IOException("暂时无法读取额度，请确认 Codex 已登录且网络可用；60 秒后重试。");
            return result;
        } finally {
            lock(processLock) { try { if(!process.HasExited)process.Kill();process.WaitForExit(2000); } catch { } server=null; }
            process.Dispose();ready.Close();
        }
    }
    string ApplyRates(Dictionary<string,object> result,bool notify) {
        var buckets=Obj(Get(result,"rateLimitsByLimitId"));
        if(buckets==null||buckets.Count==0)buckets=new Dictionary<string,object>{{"codex",Get(result,"rateLimits")}};
        var summaries=new List<string>();var alerts=new List<string>();var readings=new List<QuotaReading>();
        foreach(var bucket in buckets)foreach(string window in new[]{"primary","secondary"}) {
            var rate=Obj(Get(Obj(bucket.Value),window));double used;long reset;int minutes;
            if(rate==null||!Double.TryParse(Str(Get(rate,"usedPercent")),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out used)||Double.IsNaN(used)||Double.IsInfinity(used)||used<0||used>100||!Int64.TryParse(Str(Get(rate,"resetsAt")),out reset)||!Int32.TryParse(Str(Get(rate,"windowDurationMins")),out minutes)||(minutes!=300&&minutes!=10080))continue;
            DateTime resetTime;try { resetTime=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(reset); }catch { continue; }
            if(resetTime<=DateTime.UtcNow)continue;
            double remaining=100-used;string label=(bucket.Key=="codex"?"Codex":bucket.Key)+" "+(minutes==300?"5 小时":"周额度");
            readings.Add(new QuotaReading {Bucket=bucket.Key,Minutes=minutes,Remaining=remaining,ResetUtc=resetTime});
            summaries.Add(label+"：剩余 "+remaining.ToString("0.#")+"%\n重置："+resetTime.ToLocalTime().ToString("MM-dd HH:mm"));
            string key=bucket.Key+"/"+minutes,old;int previous=100;
            if(ledger.TryGetValue(key,out old)) { string[] parts=old.Split(':');if(parts.Length==2&&parts[0]==reset.ToString())Int32.TryParse(parts[1],out previous); }
            if(notify&&ShouldAlert(remaining,previous)) {
                alerts.Add(label+"剩余 "+remaining.ToString("0.#")+"%，"+(remaining<=0?"这条能量槽见底了。":remaining<5?"快见底了，先把进度存好。":"留点额度给下一关。"));
                ledger[key]=reset+":"+Level(remaining);
            }
        }
        if(Quotas!=null)Quotas(readings);
        if(alerts.Count>0) {
            try { Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath));string temp=ledgerPath+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(ledger),Encoding.UTF8);if(File.Exists(ledgerPath))File.Replace(temp,ledgerPath,null);else File.Move(temp,ledgerPath); } catch { }
            Alert("Codex 额度提醒",String.Join("\n",alerts));
        }
        return summaries.Count==0?"额度不可用：当前登录方式或返回数据未提供有效的 5 小时/周窗口。":String.Join("\n\n",summaries)+"\n更新："+DateTime.Now.ToString("HH:mm:ss");
    }
    public static string CompletionId(string line) {
        if(line.IndexOf("task_complete",StringComparison.Ordinal)<0)return null;
        try { var root=Parse(line);if(Str(Get(root,"type"))!="event_msg")return null;var payload=Obj(Get(root,"payload"));if(Str(Get(payload,"type"))!="task_complete")return null;return Str(Get(payload,"turn_id")); }catch { return null; }
    }
    void Scan(bool baseline) {
        string directory=Path.Combine(home,"sessions");if(!Directory.Exists(directory))return;
        foreach(string file in Directory.EnumerateFiles(directory,"*.jsonl",SearchOption.AllDirectories)) {
            if(stop.WaitOne(0))return;
            try {
                var info=new FileInfo(file);long offset;
                if(!offsets.TryGetValue(file,out offset)) { offsets[file]=baseline?info.Length:0;offset=offsets[file]; }
                if(info.Length<offset) { offset=0;pending.Remove(file); }
                if(info.Length==offset)continue;
                using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                    List<byte> bytes;if(!pending.TryGetValue(file,out bytes))pending[file]=bytes=new List<byte>();
                    stream.Position=offset;int value;long consumed=0;
                    while(consumed<4*1024*1024&&(value=stream.ReadByte())!=-1) {
                        consumed++;
                        if(value==10) {
                            string line=Encoding.UTF8.GetString(bytes.ToArray());string id=CompletionId(line);bytes.Clear();
                            if(id!=null) { DateTime timestamp;if(!DateTime.TryParse(Str(Get(Parse(line),"timestamp")),null,System.Globalization.DateTimeStyles.RoundtripKind,out timestamp)||timestamp.ToUniversalTime()<started)id=null; }
                            if(TasksEnabled&&!String.IsNullOrEmpty(id)&&completed.Add(file+"/"+id)) {
                                string name=Path.GetFileNameWithoutExtension(file);name=name.Length>36?name.Substring(name.Length-36):name;
                                Alert("Codex 任务运行结束","本轮运行结束，去看看战利品吧。\n任务尾号 "+name.Substring(Math.Max(0,name.Length-8))+" · 请在 Codex 验收结果");
                            }
                        } else if(bytes.Count<16*1024*1024) { bytes.Add((byte)value); }
                    }
                    offsets[file]=stream.Position;
                }
            }catch(IOException) { }catch(UnauthorizedAccessException) { }
        }
        if(completed.Count>2000)completed.Clear();
    }
    void Run() {
        try { Scan(true); }catch { }
        DateTime next=DateTime.MinValue;
        while(!stop.WaitOne(0)) {
            if(UsageEnabled&&DateTime.UtcNow>=next) {
                try { Publish(ApplyRates(Query(),UsageEnabled)); }catch(Exception e) { string message=e is IOException?e.Message:"额度暂时不可用；60 秒后重试。";Publish(message);if(Failure!=null)Failure(message); }
                next=DateTime.UtcNow.AddSeconds(60);
            }
            try { Scan(false); }catch { Publish("本地任务事件读取失败，请检查 Codex 数据目录权限。"); }
            if(stop.WaitOne(2000))break;
        }
    }
    public static string Probe(string folder) { using(var monitor=new CodexMonitor(folder))return monitor.ApplyRates(monitor.Query(),false); }
    public void Dispose() { stop.Set();lock(processLock) { try { if(server!=null&&!server.HasExited)server.Kill(); } catch { } } }
    public static void SelfTest(string root) {
        if(Level(50)!=100||Level(49)!=50||Level(20)!=50||Level(19)!=20||Level(5)!=20||Level(4)!=5||Level(0)!=0||ShouldAlert(19,20)||!ShouldAlert(4,50))throw new Exception("quota thresholds failed");
        if(CompletionId("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"test\"}}")!="test"||CompletionId("{\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_aborted\"}}")!=null||CompletionId("not json")!=null)throw new Exception("completion parsing failed");
        string fixture=Path.Combine(root,"qa","codex-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(fixture,"sessions"));
        string log=Path.Combine(fixture,"sessions","rollout-test.jsonl");
        Func<string,string,string> record=(id,time)=>"{\"timestamp\":\""+time+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\""+id+"\"}}\n";
        File.WriteAllText(log,record("old",DateTime.UtcNow.AddHours(-1).ToString("o")),new UTF8Encoding(false));
        int notices=0;
        using(var monitor=new CodexMonitor(fixture,fixture)) {
            monitor.Notice=delegate { notices++; };monitor.Scan(true);monitor.Scan(false);if(notices!=0)throw new Exception("historical completion replayed");
            string completion=record("new",DateTime.UtcNow.ToString("o"));
            File.AppendAllText(log,completion.Substring(0,completion.Length-1));monitor.Scan(false);if(notices!=0)throw new Exception("partial line consumed");
            File.AppendAllText(log,"\n");monitor.Scan(false);if(notices!=1)throw new Exception("new completion missing");
            File.AppendAllText(log,completion);monitor.Scan(false);if(notices!=1)throw new Exception("duplicate completion");
            monitor.TasksEnabled=false;File.AppendAllText(log,record("muted",DateTime.UtcNow.ToString("o")));monitor.Scan(false);monitor.TasksEnabled=true;monitor.Scan(false);if(notices!=1)throw new Exception("muted completion replayed");
            File.WriteAllText(Path.Combine(fixture,"sessions","history.jsonl"),record("history",DateTime.UtcNow.AddHours(-1).ToString("o")));monitor.Scan(false);if(notices!=1)throw new Exception("imported history replayed");
            long reset=(long)(DateTime.UtcNow.AddHours(4)-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;
            Func<int,long,Dictionary<string,object>> rates=(used,at)=>Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":"+used+",\"windowDurationMins\":300,\"resetsAt\":"+at+"}}}");
            monitor.ApplyRates(rates(60,reset),true);monitor.ApplyRates(rates(61,reset),true);if(notices!=2)throw new Exception("quota repeated");
            monitor.ApplyRates(rates(97,reset),true);if(notices!=3)throw new Exception("quota severity jump missing");
            monitor.ApplyRates(Parse("{\"rateLimits\":{\"primary\":null}}"),true);if(notices!=3)throw new Exception("null quota alerted");
            using(var restarted=new CodexMonitor(fixture,fixture)) { restarted.Notice=delegate { notices++; };restarted.ApplyRates(rates(97,reset),true);if(notices!=3)throw new Exception("quota repeated after restart");restarted.ApplyRates(rates(60,reset+3600),true);if(notices!=4)throw new Exception("quota rollover missing"); }
        }
    }
}
}
