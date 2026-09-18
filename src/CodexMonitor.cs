using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;

namespace SilverWolfPet {
public sealed class QuotaReading {
    public string Bucket;
    public int Minutes;
    public double Remaining;
    public DateTime ResetUtc;
    public bool IsCredits;
    public string Balance;
    public bool Unlimited;
}
public sealed class CodexMonitor : IDisposable {
    sealed class PendingCompletion { public string Id;public DateTime ReadyUtc;public DateTime EventUtc;public bool Notify; }
    public volatile bool UsageEnabled=true, TasksEnabled=true;
    public Action<string,string> Notice;
    public Action<string> Status;
    public Action<List<QuotaReading>> Quotas;
    public Action<string> Failure;
    public Action<bool> Working;
    public Action<string> TaskState;
    readonly ManualResetEvent stop=new ManualResetEvent(false);
    readonly string home, ledgerPath;
    readonly Dictionary<string,string> ledger=new Dictionary<string,string>();
    readonly Dictionary<string,long> offsets=new Dictionary<string,long>();
    readonly HashSet<string> completed=new HashSet<string>();
    readonly Dictionary<string,string> active=new Dictionary<string,string>();
    readonly Dictionary<string,PendingCompletion> pendingCompletions=new Dictionary<string,PendingCompletion>();
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
        var alertWindows=new Dictionary<string,QuotaReading>();
        foreach(var bucket in buckets) {
            var bucketValue=Obj(bucket.Value);
            foreach(string window in new[]{"primary","secondary"}) {
            var rate=Obj(Get(bucketValue,window));double used;long reset;int minutes;
            if(rate==null||!Double.TryParse(Str(Get(rate,"usedPercent")),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out used)||Double.IsNaN(used)||Double.IsInfinity(used)||used<0||used>100||!Int64.TryParse(Str(Get(rate,"resetsAt")),out reset)||!Int32.TryParse(Str(Get(rate,"windowDurationMins")),out minutes)||(minutes!=300&&minutes!=10080))continue;
            DateTime resetTime;try { resetTime=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(reset); }catch { continue; }
            if(resetTime<=DateTime.UtcNow)continue;
            double remaining=100-used;string label=(bucket.Key=="codex"?"Codex":bucket.Key)+" "+(minutes==300?"5 小时":"周额度");
            readings.Add(new QuotaReading {Bucket=bucket.Key,Minutes=minutes,Remaining=remaining,ResetUtc=resetTime});
            summaries.Add(label+"：剩余 "+remaining.ToString("0.#")+"%\n重置："+resetTime.ToLocalTime().ToString("MM-dd HH:mm"));
            string alertKey=minutes+"/"+reset;QuotaReading candidate;
            if(!alertWindows.TryGetValue(alertKey,out candidate)||remaining<candidate.Remaining)alertWindows[alertKey]=new QuotaReading {Bucket=bucket.Key,Minutes=minutes,Remaining=remaining,ResetUtc=resetTime};
            }
            var credits=Obj(Get(bucketValue,"credits"));
            if(credits!=null) {
                bool unlimited;Boolean.TryParse(Str(Get(credits,"unlimited")),out unlimited);
                decimal balance;bool hasBalance=Decimal.TryParse(Str(Get(credits,"balance")),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out balance);
                if(unlimited||hasBalance)readings.Add(new QuotaReading {Bucket=bucket.Key,IsCredits=true,Unlimited=unlimited,Balance=unlimited?"无限":balance.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture),ResetUtc=DateTime.MaxValue});
            }
        }
        foreach(var pair in alertWindows) {
            var q=pair.Value;string key="window/"+pair.Key,old;int previous=100;
            if(ledger.TryGetValue(key,out old))Int32.TryParse(old,out previous);
            if(notify&&ShouldAlert(q.Remaining,previous)) {
                string label=(q.Bucket=="codex"?"Codex":q.Bucket)+" "+(q.Minutes==300?"5 小时":"周额度");
                alerts.Add(label+"剩余 "+q.Remaining.ToString("0.#")+"%，"+(q.Remaining<=0?"这条能量槽见底了。":q.Remaining<5?"快见底了，先把进度存好。":"留点额度给下一关。"));
                ledger[key]=Level(q.Remaining).ToString(System.Globalization.CultureInfo.InvariantCulture);
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
    public static string StartedId(string line) {return EventId(line,"task_started");}
    public static string EndedId(string line) {
        string value=EventId(line,"task_complete");return value??EventId(line,"turn_aborted");
    }
    static string EventId(string line,string kind) {
        if(line.IndexOf(kind,StringComparison.Ordinal)<0)return null;
        try {var root=Parse(line);if(Str(Get(root,"type"))!="event_msg")return null;var payload=Obj(Get(root,"payload"));if(Str(Get(payload,"type"))!=kind)return null;string id=Str(Get(payload,"turn_id"));return String.IsNullOrEmpty(id)?"current":id;}catch{return null;}
    }
    static string JsonStringAfterKey(string line,string key,int start=0) {
        if(String.IsNullOrEmpty(line))return "";int at=line.IndexOf("\""+key+"\"",start,StringComparison.Ordinal);
        if(at<0)return "";int colon=line.IndexOf(':',at+key.Length+2);if(colon<0)return "";int quote=line.IndexOf('"',colon+1);if(quote<0)return "";
        int end=quote+1;while(end<line.Length) {if(line[end]=='"'&&line[end-1]!='\\')break;end++;}return end<line.Length?line.Substring(quote+1,end-quote-1):"";
    }
    static DateTime LineTimeUtc(string line) {
        DateTime parsed;if(!DateTime.TryParse(JsonStringAfterKey(line,"timestamp"),null,System.Globalization.DateTimeStyles.RoundtripKind,out parsed))return DateTime.MinValue;return parsed.ToUniversalTime();
    }
    public static string StateForLine(string line) {
        try {
            int payloadAt=line.IndexOf("\"payload\"",StringComparison.Ordinal);if(payloadAt<0)return null;
            string type=JsonStringAfterKey(line,"type",payloadAt);
            if(type=="task_started")return "thinking";
            if(type=="turn_aborted")return "failed";
            if(type=="task_complete")return null; // Published only after debounce confirms the task really ended.
            if(type=="custom_tool_call"||type=="function_call") {
                string name=JsonStringAfterKey(line,"name",payloadAt);
                if(name.IndexOf("request_user_input",StringComparison.OrdinalIgnoreCase)>=0||line.IndexOf("require_escalated",StringComparison.OrdinalIgnoreCase)>=0)return "waiting-input";
                return "executing";
            }
            // A failed individual tool call is recoverable and is common while an
            // agent tries alternate routes. Only a turn-level abort is treated as
            // failure; tool output and item completion must not flash the error pose.
            if(type=="custom_tool_call_output"||type=="function_call_output")return null;
            if(type=="reasoning")return "thinking";
            if(type=="item_completed")return null;
        }catch { }
        return null;
    }
    void PublishWorking(bool previous) {if(previous!=active.Any()&&Working!=null)Working(active.Any());}
    void ExpireInactive() {
        bool previous=active.Any();DateTime cutoff=DateTime.UtcNow.AddMinutes(-30);
        foreach(string file in active.Keys.ToArray()) {
            try {if(!File.Exists(file)||File.GetLastWriteTimeUtc(file)<cutoff)active.Remove(file);}
            catch {active.Remove(file);}
        }
        foreach(string file in pendingCompletions.Keys.Where(x=>!active.ContainsKey(x)).ToArray())pendingCompletions.Remove(file);
        PublishWorking(previous);
    }
    void FlushCompletions(DateTime now) {
        bool wasWorking=active.Any();
        foreach(var pair in pendingCompletions.ToArray()) {
            string file=pair.Key;var pendingCompletion=pair.Value;
            if(now<pendingCompletion.ReadyUtc)continue;
            try {if(File.Exists(file)&&(now-File.GetLastWriteTimeUtc(file)).TotalSeconds<3) {pendingCompletion.ReadyUtc=now.AddSeconds(3);continue;}}catch { }
            string current;if(active.TryGetValue(file,out current)&&(pendingCompletion.Id=="current"||current==pendingCompletion.Id))active.Remove(file);
            pendingCompletions.Remove(file);
            if(!completed.Add(file+"/"+pendingCompletion.Id)||!pendingCompletion.Notify||!TasksEnabled)continue;
            if(TaskState!=null)TaskState("completed");
            string name=Path.GetFileNameWithoutExtension(file);name=name.Length>36?name.Substring(name.Length-36):name;
            Alert("Codex 任务运行结束","本轮运行结束，去看看战利品吧。\n任务尾号 "+name.Substring(Math.Max(0,name.Length-8))+" · 请在 Codex 验收结果");
        }
        PublishWorking(wasWorking);
    }
    void Scan(bool baseline) {
        string directory=Path.Combine(home,"sessions");if(!Directory.Exists(directory))return;
        foreach(string file in Directory.EnumerateFiles(directory,"*.jsonl",SearchOption.AllDirectories)) {
            if(stop.WaitOne(0))return;
            try {
                var info=new FileInfo(file);long offset;
                if(baseline) {offsets[file]=info.Length;pending.Remove(file);continue;}
                if(!offsets.TryGetValue(file,out offset)) { offsets[file]=baseline?Math.Max(0,info.Length-1024*1024):0;offset=offsets[file]; }
                if(info.Length<offset) { offset=0;pending.Remove(file); }
                if(info.Length==offset)continue;
                using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                    List<byte> bytes;if(!pending.TryGetValue(file,out bytes))pending[file]=bytes=new List<byte>();
                    stream.Position=offset;int value;long consumed=0;
                    while(consumed<4*1024*1024&&(value=stream.ReadByte())!=-1) {
                        consumed++;
                        if(value==10) {
                            string line=Encoding.UTF8.GetString(bytes.ToArray());string id=CompletionId(line);bytes.Clear();
                            bool wasWorking=active.Any();string taskStarted=StartedId(line),aborted=EventId(line,"turn_aborted");
                            DateTime timestamp=LineTimeUtc(line);bool fresh=timestamp!=DateTime.MinValue&&timestamp>=started;
                            if(taskStarted!=null&&fresh) {active[file]=taskStarted;pendingCompletions.Remove(file);}
                            string current;
                            if(aborted!=null&&active.TryGetValue(file,out current)&&(aborted=="current"||current==aborted)) {active.Remove(file);pendingCompletions.Remove(file);}
                            PublishWorking(wasWorking);
                            string inferred=StateForLine(line);
                            if(!baseline&&fresh&&TasksEnabled&&inferred!=null&&(taskStarted!=null||aborted!=null||active.ContainsKey(file))&&TaskState!=null)TaskState(inferred);
                            if(id!=null&&(timestamp==DateTime.MinValue||timestamp<started))id=null;
                            if(!baseline&&!String.IsNullOrEmpty(id)&&active.TryGetValue(file,out current)&&(id=="current"||current==id))pendingCompletions[file]=new PendingCompletion {Id=id,EventUtc=timestamp,ReadyUtc=DateTime.UtcNow.AddSeconds(8),Notify=TasksEnabled};
                        } else if(bytes.Count<16*1024*1024) { bytes.Add((byte)value); }
                    }
                    offsets[file]=stream.Position;
                }
            }catch(IOException) { }catch(UnauthorizedAccessException) { }
        }
        ExpireInactive();
        FlushCompletions(DateTime.UtcNow);
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
        if(CompletionId("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"test\"}}")!="test"||StartedId("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"go\"}}")!="go"||EndedId("{\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_aborted\",\"turn_id\":\"stop\"}}")!="stop"||CompletionId("not json")!=null)throw new Exception("task event parsing failed");
        if(StateForLine("{\"type\":\"response_item\",\"payload\":{\"type\":\"custom_tool_call\",\"name\":\"exec\",\"input\":\"{}\"}}")!="executing"||StateForLine("{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"request_user_input_async\"}}")!="waiting-input"||StateForLine("{\"type\":\"response_item\",\"payload\":{\"type\":\"custom_tool_call_output\",\"output\":\"source contains \\\"exit_code\\\":1 but tool succeeded\"}}")!=null||StateForLine("{\"type\":\"event_msg\",\"payload\":{\"type\":\"item_completed\",\"item\":{\"type\":\"CommandExecution\",\"status\":\"failed\",\"exit_code\":1}}}")!=null||StateForLine("{\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_aborted\",\"turn_id\":\"x\"}}")!="failed")throw new Exception("task state classification failed");
        string fixture=Path.Combine(root,"qa","codex-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(fixture,"sessions"));
        string log=Path.Combine(fixture,"sessions","rollout-test.jsonl");
        Func<string,string,string> record=(id,time)=>"{\"timestamp\":\""+time+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\""+id+"\"}}\n";
        File.WriteAllText(log,record("old",DateTime.UtcNow.AddHours(-1).ToString("o")),new UTF8Encoding(false));
        File.AppendAllText(log,"{\"timestamp\":\""+DateTime.UtcNow.AddMinutes(-2).ToString("o")+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"abandoned-history\"}}\n");
        int notices=0;
        using(var monitor=new CodexMonitor(fixture,fixture)) {
            var states=new List<bool>();var taskStates=new List<string>();monitor.Working=v=>states.Add(v);monitor.TaskState=v=>taskStates.Add(v);monitor.Notice=delegate { notices++; };monitor.Scan(true);monitor.Scan(false);if(notices!=0)throw new Exception("historical completion replayed");
            if(states.Count!=0||monitor.active.Count!=0)throw new Exception("historical start activated working state");
            File.WriteAllText(Path.Combine(fixture,"sessions","imported.jsonl"),"{\"timestamp\":\""+DateTime.UtcNow.AddMinutes(-2).ToString("o")+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"imported-old\"}}\n");monitor.Scan(false);
            if(states.Count!=0)throw new Exception("imported old start activated working state");
            File.AppendAllText(log,"{\"timestamp\":\""+DateTime.UtcNow.ToString("o")+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"live\"}}\n");monitor.Scan(false);if(states.Count==0||!states.Last()||taskStates.Last()!="thinking")throw new Exception("working state did not start");
            File.SetLastWriteTimeUtc(log,DateTime.UtcNow.AddMinutes(-1));monitor.Scan(false);if(!states.Last())throw new Exception("working state expired during a quiet task");
            File.SetLastWriteTimeUtc(log,DateTime.UtcNow.AddMinutes(-31));monitor.Scan(false);if(states.Last())throw new Exception("abandoned working state did not expire");
            File.AppendAllText(log,"{\"timestamp\":\""+DateTime.UtcNow.ToString("o")+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"live\"}}\n");monitor.Scan(false);if(!states.Last())throw new Exception("working state did not restart");
            File.AppendAllText(log,"{\"timestamp\":\""+DateTime.UtcNow.ToString("o")+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_aborted\",\"turn_id\":\"live\"}}\n");monitor.Scan(false);if(states.Last()||taskStates.Last()!="failed")throw new Exception("working state did not stop");
            string completion=record("new",DateTime.UtcNow.ToString("o"));
            File.AppendAllText(log,"{\"timestamp\":\""+DateTime.UtcNow.ToString("o")+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"new\"}}\n");monitor.Scan(false);
            File.AppendAllText(log,completion.Substring(0,completion.Length-1));monitor.Scan(false);if(notices!=0)throw new Exception("partial line consumed");
            File.AppendAllText(log,"\n");monitor.Scan(false);if(notices!=0||!states.Last())throw new Exception("completion was not debounced");
            monitor.pendingCompletions[log].ReadyUtc=DateTime.UtcNow.AddSeconds(-1);File.SetLastWriteTimeUtc(log,DateTime.UtcNow.AddSeconds(-5));monitor.FlushCompletions(DateTime.UtcNow);if(notices!=1||states.Last()||taskStates.Last()!="completed")throw new Exception("confirmed completion missing");
            File.AppendAllText(log,completion);monitor.Scan(false);if(notices!=1)throw new Exception("duplicate completion");
            File.AppendAllText(log,"{\"timestamp\":\""+DateTime.UtcNow.ToString("o")+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"muted\"}}\n");monitor.Scan(false);monitor.TasksEnabled=false;File.AppendAllText(log,record("muted",DateTime.UtcNow.ToString("o")));monitor.Scan(false);monitor.pendingCompletions[log].ReadyUtc=DateTime.UtcNow.AddSeconds(-1);File.SetLastWriteTimeUtc(log,DateTime.UtcNow.AddSeconds(-5));monitor.FlushCompletions(DateTime.UtcNow);monitor.TasksEnabled=true;if(notices!=1||states.Last())throw new Exception("muted completion replayed or working state stuck");
            File.WriteAllText(Path.Combine(fixture,"sessions","history.jsonl"),record("history",DateTime.UtcNow.AddHours(-1).ToString("o")));monitor.Scan(false);if(notices!=1)throw new Exception("imported history replayed");
            long reset=(long)(DateTime.UtcNow.AddHours(4)-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;
            Func<int,long,Dictionary<string,object>> rates=(used,at)=>Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":"+used+",\"windowDurationMins\":300,\"resetsAt\":"+at+"}}}");
            monitor.ApplyRates(rates(60,reset),true);monitor.ApplyRates(rates(61,reset),true);if(notices!=2)throw new Exception("quota repeated");
            monitor.ApplyRates(rates(97,reset),true);if(notices!=3)throw new Exception("quota severity jump missing");
            monitor.ApplyRates(Parse("{\"rateLimits\":{\"primary\":null}}"),true);if(notices!=3)throw new Exception("null quota alerted");
            List<QuotaReading> creditReadings=null;monitor.Quotas=value=>creditReadings=value;
            monitor.ApplyRates(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":60,\"windowDurationMins\":300,\"resetsAt\":"+reset+"},\"credits\":{\"hasCredits\":true,\"unlimited\":false,\"balance\":\"381.1772210000\"}}}"),false);
            var credit=creditReadings==null?null:creditReadings.FirstOrDefault(x=>x.IsCredits);if(credit==null||credit.Balance!="381.18")throw new Exception("credit balance missing");
            using(var restarted=new CodexMonitor(fixture,fixture)) { restarted.Notice=delegate { notices++; };restarted.ApplyRates(rates(97,reset),true);if(notices!=3)throw new Exception("quota repeated after restart");restarted.ApplyRates(rates(60,reset+3600),true);if(notices!=4)throw new Exception("quota rollover missing");
                long weeklyReset=reset+7200;
                var duplicateWeekly=Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"secondary\":{\"usedPercent\":60,\"windowDurationMins\":10080,\"resetsAt\":"+weeklyReset+"}},\"other\":{\"secondary\":{\"usedPercent\":61,\"windowDurationMins\":10080,\"resetsAt\":"+weeklyReset+"}}}}");
                restarted.ApplyRates(duplicateWeekly,true);restarted.ApplyRates(duplicateWeekly,true);if(notices!=5)throw new Exception("same weekly window alerted more than once");
            }
        }
    }
}
}
