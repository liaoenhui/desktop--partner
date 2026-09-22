using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using System.Diagnostics;
using System.Windows.Automation;

namespace SilverWolfPet {
// Values crossing into the WPF dispatcher are copies, never mutable monitor state.
public sealed class CodexTaskView {
    public string Key,ThreadId,TurnId,Title,State,WaitingKind;
    public DateTime StartedUtc,EndedUtc;
    public bool Running;
    public CodexTaskView Copy() {return (CodexTaskView)MemberwiseClone();}
}
public sealed class CodexTaskSnapshot {
    public CodexTaskView Display;
    public CodexTaskView[] Active=new CodexTaskView[0];
    public string MainKey,Notice;
    public bool Attention,Multiple;
}
public sealed class CodexTaskTracker {
    sealed class Entry {
        public CodexTaskView View;
        public string RequestCall,RequestKind;
        public DateTime RequestAt;
        public bool AsyncQuestion;
    }
    readonly Dictionary<string,Entry> tasks=new Dictionary<string,Entry>();
    readonly Dictionary<string,string> titles=new Dictionary<string,string>();
    string mainKey,notice;
    bool multiRun,selectionInitialized;
    public Func<bool> ApprovalVisible;
    static Dictionary<string,object> Obj(object x){return x as Dictionary<string,object>;}
    static object Get(Dictionary<string,object> d,string key){object value;return d!=null&&d.TryGetValue(key,out value)?value:null;}
    static string Str(object x){return x==null?"":Convert.ToString(x);}
    public static string ThreadId(string file) {string name=Path.GetFileNameWithoutExtension(file);Guid id;return name.Length>=36&&Guid.TryParse(name.Substring(name.Length-36),out id)?id.ToString():name;}
    public void Rename(string id,string title) {
        if(String.IsNullOrWhiteSpace(title))return;
        title=title.Replace('\r',' ').Replace('\n',' ').Trim();if(title.Length>100)title=title.Substring(0,100);
        titles[id]=title;foreach(var entry in tasks.Values.Where(x=>x.View.ThreadId==id))entry.View.Title=title;
    }
    public void Start(string file,string id,DateTime at) {
        string key=file+"/"+id;Entry existing;
        if(tasks.TryGetValue(key,out existing))return; // Duplicate starts must not reset time/state.
        foreach(var old in tasks.Values.Where(x=>x.View.ThreadId==ThreadId(file)&&x.View.Running)) {old.View.Running=false;old.View.State="idle";old.View.EndedUtc=at;}
        string thread=ThreadId(file),title;
        if(!titles.TryGetValue(thread,out title))title="任务 "+thread.Substring(Math.Max(0,thread.Length-6));
        tasks[key]=new Entry {View=new CodexTaskView {Key=key,ThreadId=thread,TurnId=id,Title=title,State="thinking",Running=true,StartedUtc=at}};
        if(mainKey==null||!selectionInitialized&&at<tasks[mainKey].View.StartedUtc)mainKey=key;
        if(tasks.Values.Count(x=>x.View.Running)>1)multiRun=true;
    }
    public void Forget(string file,string id) {Finish(file,id,"idle",DateTime.UtcNow,false);}
    public void Finish(string file,string id,string state,DateTime now,bool notify) {
        Entry task;if(!tasks.TryGetValue(file+"/"+id,out task)||!task.View.Running)return;
        task.View.State=state;task.View.Running=false;task.View.EndedUtc=now;ClearRequest(task);
        if(notify&&task.View.Key!=mainKey&&tasks.Values.Any(x=>x.View.Running))notice=task.View.Title+(state=="completed"?"：已完成。":"：运行已停止。");
    }
    static void ClearRequest(Entry task){task.RequestCall=null;task.RequestKind=null;task.View.WaitingKind=null;task.AsyncQuestion=false;}
    public void Observe(string file,string id,string line,DateTime now) {
        Entry task;if(!tasks.TryGetValue(file+"/"+id,out task)||!task.View.Running)return;
        int payloadAt=line.IndexOf("\"payload\"",StringComparison.Ordinal);
        if(payloadAt<0)return;
        // Token counters and ordinary messages cannot change a work action. Skip
        // their often large JSON bodies before allocating a deserialized graph.
        if(line.IndexOf("\"token_count\"",payloadAt,StringComparison.Ordinal)>=0&&line.IndexOf("\"token_count\"",payloadAt,StringComparison.Ordinal)<payloadAt+40)return;
        try {
            var root=new JavaScriptSerializer {MaxJsonLength=16*1024*1024}.DeserializeObject(line) as Dictionary<string,object>;
            var payload=Obj(Get(root,"payload"));string type=Str(Get(payload,"type"));string turn=Str(Get(payload,"turn_id"));
            if(turn.Length>0&&turn!=id)return; // A late item from another turn cannot steal this one.
            if(Str(Get(root,"type"))=="event_msg") {
                if(type=="user_message"&&task.RequestKind=="input"){ClearRequest(task);task.View.State="thinking";}
                if(type=="exec_approval_request"||type=="apply_patch_approval_request") {
                    task.RequestCall=Str(Get(payload,"call_id"));task.RequestKind="approval";task.RequestAt=now;return;
                }
                if(type=="item_completed") {
                    var item=Obj(Get(payload,"item"));
                    if(task.RequestCall!=null&&Str(Get(item,"id"))==task.RequestCall&&task.RequestKind=="approval") {ClearRequest(task);task.View.State="executing";}
                }
                return;
            }
            if(Str(Get(root,"type"))!="response_item")return;
            if(type=="custom_tool_call"||type=="function_call") {
                string name=Str(Get(payload,"name")),call=Str(Get(payload,"call_id"));
                // Inspect actual function arguments only; output/source code quoting an
                // escalation request is never itself an approval request.
                string input=Str(Get(payload,type=="function_call"?"arguments":"input"));
                bool question=name.IndexOf("request_user_input",StringComparison.OrdinalIgnoreCase)>=0;
                bool escalation=input.IndexOf("require_escalated",StringComparison.Ordinal)>=0;
                if(task.RequestKind=="input"&&!task.AsyncQuestion)ClearRequest(task);
                if(question||escalation) {task.RequestCall=call;task.RequestKind=question?"input":"approval";task.RequestAt=now;task.AsyncQuestion=question&&name.IndexOf("async",StringComparison.OrdinalIgnoreCase)>=0;}
                else if(task.RequestKind=="approval")ClearRequest(task);
                if(task.View.State!="waiting-input"||task.RequestKind==null)task.View.State="executing";
            } else if(type=="custom_tool_call_output"||type=="function_call_output") {
                if(task.RequestCall!=null&&Str(Get(payload,"call_id"))==task.RequestCall&&!task.AsyncQuestion){ClearRequest(task);task.View.State="executing";}
            } else if(type=="reasoning"&&task.RequestKind==null)task.View.State="thinking";
        }catch(ArgumentException){}catch(InvalidOperationException){}
    }
    public void Select(string key) {Entry task;if(tasks.TryGetValue(key,out task)&&task.View.Running)mainKey=key;}
    public CodexTaskSnapshot Snapshot(DateTime now) {
        var active=tasks.Values.Where(x=>x.View.Running).OrderBy(x=>x.View.StartedUtc).ThenBy(x=>x.View.Key,StringComparer.Ordinal).ToArray();
        if(active.Length>0)selectionInitialized=true;
        // A slow command is not evidence of a permission prompt. Confirm the
        // actual Codex allow/deny controls before showing an approval action.
        var approvalCandidates=active.Where(x=>x.RequestKind=="approval"&&(now-x.RequestAt).TotalSeconds>=3).ToArray();
        bool approval=approvalCandidates.Length==1&&ApprovalVisible!=null&&ApprovalVisible();
        foreach(var task in active) {
            bool waiting=task.RequestKind=="input"&&(now-task.RequestAt).TotalSeconds>=2||approval&&approvalCandidates[0]==task;
            if(waiting){task.View.State="waiting-input";task.View.WaitingKind=task.RequestKind;}
            else if(task.View.State=="waiting-input"){task.View.State="executing";task.View.WaitingKind=null;}
        }
        Entry main=null;if(mainKey!=null)tasks.TryGetValue(mainKey,out main);
        if(main==null||!main.View.Running&&(main.View.State=="idle"||(now-main.View.EndedUtc).TotalSeconds>=8)) {
            main=active.FirstOrDefault();mainKey=main==null?null:main.View.Key;
        }
        var attention=active.FirstOrDefault(x=>x.View.State=="waiting-input");
        var display=attention??main;
        var snapshot=new CodexTaskSnapshot {Display=display==null?null:display.View.Copy(),MainKey=mainKey,Attention=attention!=null&&attention!=main,Active=active.Select(x=>x.View.Copy()).ToArray(),Multiple=multiRun&&(active.Length>1||display!=null&&!display.View.Running&&active.Length>0),Notice=notice};
        notice=null;
        foreach(string key in tasks.Where(x=>!x.Value.View.Running&&x.Key!=mainKey&&(now-x.Value.View.EndedUtc).TotalSeconds>=8).Select(x=>x.Key).ToArray())tasks.Remove(key);
        if(active.Length==0&&main==null){multiRun=false;selectionInitialized=false;}
        return snapshot;
    }
    public static void SelfTest() {
        var tracker=new CodexTaskTracker();DateTime start=new DateTime(2026,9,22,1,0,0,DateTimeKind.Utc);
        tracker.Start("b.jsonl","b",start.AddMinutes(2));tracker.Start("a.jsonl","a",start);
        string exec="{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"exec_command\",\"call_id\":\"c1\",\"arguments\":\"{}\"}}";
        string reasoning="{\"type\":\"response_item\",\"payload\":{\"type\":\"reasoning\"}}";
        tracker.Observe("a.jsonl","a",exec,start.AddMinutes(3));tracker.Observe("b.jsonl","b",reasoning,start.AddMinutes(3));
        var shot=tracker.Snapshot(start.AddMinutes(3));
        if(shot.Display.TurnId!="a"||shot.Display.State!="executing"||shot.Display.StartedUtc!=start||!shot.Multiple)throw new Exception("parallel reasoning stole the main action/time");
        tracker.Select("b.jsonl/b");shot=tracker.Snapshot(start.AddMinutes(3));
        if(shot.Display.TurnId!="b"||shot.Display.StartedUtc!=start.AddMinutes(2))throw new Exception("selected timer did not belong to the selected task");
        tracker.Select("a.jsonl/a");
        string escalation=exec.Replace("{}","{\\\"sandbox_permissions\\\":\\\"require_escalated\\\"}");
        tracker.Observe("b.jsonl","b",escalation,start.AddMinutes(3));tracker.ApprovalVisible=()=>false;
        if(tracker.Snapshot(start.AddMinutes(4)).Display.State=="waiting-input")throw new Exception("automatic approval or slow command caused a permission prompt");
        tracker.ApprovalVisible=()=>true;shot=tracker.Snapshot(start.AddMinutes(4));
        if(!shot.Attention||shot.Display.TurnId!="b"||shot.Display.WaitingKind!="approval")throw new Exception("confirmed background approval did not get attention");
        string result="{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call_output\",\"call_id\":\"c1\",\"output\":\"ok\"}}";
        tracker.Observe("b.jsonl","b",result,start.AddMinutes(4));shot=tracker.Snapshot(start.AddMinutes(4));
        if(shot.Attention||shot.Display.TurnId!="a")throw new Exception("resolved permission did not return to main task");
        tracker.Finish("b.jsonl","b","completed",start.AddMinutes(5),true);shot=tracker.Snapshot(start.AddMinutes(5));
        if(shot.Display.TurnId!="a"||shot.Display.State!="executing"||shot.Notice==null)throw new Exception("background completion interrupted main task");
        tracker.Start("c.jsonl","c",start.AddMinutes(6));tracker.Finish("a.jsonl","a","completed",start.AddMinutes(7),true);
        if(tracker.Snapshot(start.AddMinutes(7).AddSeconds(7)).Display.State!="completed"||tracker.Snapshot(start.AddMinutes(7).AddSeconds(9)).Display.TurnId!="c")throw new Exception("main result hold/handoff failed");
        tracker.Observe("c.jsonl","c",exec.Replace("exec_command","request_user_input"),start.AddMinutes(8));
        if(tracker.Snapshot(start.AddMinutes(8).AddSeconds(3)).Display.WaitingKind!="input")throw new Exception("user question was not tracked");
        tracker.Observe("c.jsonl","c",result,start.AddMinutes(8).AddSeconds(4));
        if(tracker.Snapshot(start.AddMinutes(8).AddSeconds(4)).Display.State!="executing")throw new Exception("answered question kept the waiting action");
        tracker.Finish("c.jsonl","wrong-turn","completed",start.AddMinutes(9),true);
        if(tracker.Snapshot(start.AddMinutes(9)).Active.Length!=1)throw new Exception("mismatched completion ended another turn");
        tracker.Finish("c.jsonl","c","failed",start.AddMinutes(9),true);
        if(tracker.Snapshot(start.AddMinutes(9)).Display.State!="failed"||tracker.Snapshot(start.AddMinutes(10)).Display!=null)throw new Exception("failed task did not retire to idle");
    }
}

// Called on the monitor thread only while a permission candidate is outstanding.
// At most one UIA probe may be in flight; a hung accessibility provider never
// blocks monitoring or starts unbounded background workers.
public sealed class CodexApprovalProbe {
    int busy;volatile bool visible;DateTime next=DateTime.MinValue,validUntil=DateTime.MinValue;
    public bool Visible() {
        DateTime now=DateTime.UtcNow;
        if(now>=next&&Interlocked.CompareExchange(ref busy,1,0)==0) {
            next=now.AddSeconds(3);
            new Thread(delegate() {
                bool found=false;
                try {
                    foreach(var p in Process.GetProcessesByName("Codex").Concat(Process.GetProcessesByName("ChatGPT"))) {
                        using(p) {if(p.MainWindowHandle==IntPtr.Zero)continue;
                            var window=AutomationElement.FromHandle(p.MainWindowHandle);
                            var buttons=window.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
                            bool allow=false,deny=false;
                            foreach(AutomationElement button in buttons) {
                                if(!button.Current.IsEnabled||button.Current.IsOffscreen)continue;
                                string text=button.Current.Name.Trim();
                                allow|=new[]{"允许一次","允许本次","批准一次","批准","允许","Approve once","Allow once","Approve","Allow"}.Any(x=>String.Equals(x,text,StringComparison.OrdinalIgnoreCase));
                                deny|=new[]{"拒绝","不允许","Decline","Deny","Reject"}.Any(x=>String.Equals(x,text,StringComparison.OrdinalIgnoreCase));
                            }
                            if(allow&&deny){found=true;break;}
                        }
                    }
                } catch { }
                visible=found;validUntil=DateTime.UtcNow.AddSeconds(5);Interlocked.Exchange(ref busy,0);
            }) {IsBackground=true,Name="Codex permission controls"}.Start();
        }
        return visible&&now<validUntil;
    }
}
}
